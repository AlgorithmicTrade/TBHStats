using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TBHStats.Capture;
using TBHStats.Capture.Wgc;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Windows.Graphics.Imaging;

namespace TBHStats.App.ViewModels;

/// <summary>
/// ViewModel экрана калибровки ROI (T030).
/// Загружает существующие калибровки из <see cref="ISettingsRepository"/>,
/// предоставляет редактируемый список <see cref="RoiCalibrationItem"/>,
/// и сохраняет изменения с клампингом координат к [0..1].
/// </summary>
public sealed partial class CalibrationViewModel : ObservableObject
{
    private readonly ISettingsRepository _settings;
    private readonly IGameMechanics _gameMechanics;
    private readonly ICaptureSession _captureSession;

    // ──────────────────────────────────────────────────────────────
    // Список ROI
    // ──────────────────────────────────────────────────────────────

    /// <summary>Редактируемый список калибровок ROI.</summary>
    public ObservableCollection<RoiCalibrationItem> Calibrations { get; } = [];

    /// <summary>Выбранный элемент в списке (null — ничего не выбрано).</summary>
    [ObservableProperty]
    private RoiCalibrationItem? _selectedItem;

    // ──────────────────────────────────────────────────────────────
    // Варианты для выпадающих списков
    // ──────────────────────────────────────────────────────────────

    /// <summary>Список известных FieldKey из FieldSourceBindings конфига.</summary>
    public IReadOnlyList<string> AvailableFieldKeys { get; }

    /// <summary>Значения перечисления <see cref="FieldSource"/>.</summary>
    public IReadOnlyList<FieldSource> AvailableSources { get; } =
        [FieldSource.MainZone, FieldSource.Tab];

    /// <summary>Значения перечисления <see cref="OcrEngine"/>.</summary>
    public IReadOnlyList<OcrEngine> AvailableOcrEngines { get; } =
        [OcrEngine.WindowsMediaOcr, OcrEngine.Tesseract];

    /// <summary>Справочник вкладок интерфейса (для выбора TabId).</summary>
    public IReadOnlyList<Tab> AvailableTabs { get; }

    // ──────────────────────────────────────────────────────────────
    // Кадр предпросмотра
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Изображение захваченного кадра игры для предпросмотра.
    /// Заполняется командой <see cref="CaptureFrameCommand"/> (снимок с <see cref="ICaptureSession"/>);
    /// null означает, что кадр ещё не захвачен (отображается подсказка).
    /// </summary>
    [ObservableProperty]
    private ImageSource? _frameImage;

    /// <summary>Ширина последнего захваченного кадра в пикселях (0 — кадра нет).</summary>
    [ObservableProperty]
    private int _frameWidthPx;

    /// <summary>Высота последнего захваченного кадра в пикселях (0 — кадра нет).</summary>
    [ObservableProperty]
    private int _frameHeightPx;

    // ──────────────────────────────────────────────────────────────
    // Статус операции
    // ──────────────────────────────────────────────────────────────

    /// <summary>Сообщение о статусе последней операции (загрузка / сохранение).</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>true, если выполняется асинхронная операция.</summary>
    [ObservableProperty]
    private bool _isBusy;

    // ──────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализирует ViewModel калибровки.
    /// </summary>
    /// <param name="settings">Репозиторий настроек (загрузка / сохранение ROI).</param>
    /// <param name="gameMechanics">Конфиг механик (вкладки, FieldKey).</param>
    public CalibrationViewModel(
        ISettingsRepository settings,
        IGameMechanics gameMechanics,
        ICaptureSession captureSession)
    {
        _settings = settings;
        _gameMechanics = gameMechanics;
        _captureSession = captureSession;

        GameMechanicsConfig cfg = gameMechanics.Current;

        // Список известных FieldKey из FieldSourceBindings (дополняется пользователем вручную)
        AvailableFieldKeys = cfg.FieldSourceBindings
            .Select(b => b.FieldKey)
            .Distinct()
            .OrderBy(k => k)
            .ToList()
            .AsReadOnly();

        AvailableTabs = cfg.Tabs;
    }

    // ──────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Загрузить существующие калибровки из репозитория.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            IReadOnlyList<RoiCalibration> saved = await _settings.GetRoiCalibrationsAsync();

            Calibrations.Clear();
            foreach (RoiCalibration roi in saved)
            {
                Calibrations.Add(RoiCalibrationItem.FromDomain(roi));
            }

            StatusMessage = $"Загружено {Calibrations.Count} ROI.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Захватить текущий кадр игры и показать его в предпросмотре.
    /// Вызывает <see cref="ICaptureSession.TryGetFrameAsync"/>; если окно игры не найдено
    /// или свёрнуто — кадр будет null, выводится подсказка в <see cref="StatusMessage"/>.
    /// Конвертация и установка <see cref="FrameImage"/> выполняются на UI-потоке (без ConfigureAwait(false)).
    /// </summary>
    [RelayCommand]
    private async Task CaptureFrameAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            using CapturedFrame? frame = await _captureSession.TryGetFrameAsync(ct);
            if (frame is null)
            {
                FrameImage = null;
                FrameWidthPx = 0;
                FrameHeightPx = 0;
                StatusMessage = "Кадр недоступен: окно игры не найдено, свёрнуто или ещё не готово. " +
                                "Откройте игру на нужной вкладке и повторите.";
                return;
            }

            // SoftwareBitmapSource требует BGRA8 с premultiplied-альфой.
            SoftwareBitmap source = frame.Bitmap;
            SoftwareBitmap? converted = null;
            if (source.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                source.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                converted = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                source = converted;
            }

            try
            {
                SoftwareBitmapSource imageSource = new();
                await imageSource.SetBitmapAsync(source);  // копирует данные внутрь источника

                FrameImage = imageSource;
                FrameWidthPx = frame.Bitmap.PixelWidth;
                FrameHeightPx = frame.Bitmap.PixelHeight;
                StatusMessage = $"Кадр захвачен ({FrameWidthPx}×{FrameHeightPx}). " +
                                "Выберите ROI и обведите область мышью по кадру.";
            }
            finally
            {
                converted?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // отмена — нормальный выход
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка захвата кадра: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Добавить новую пустую ROI-запись в список.
    /// </summary>
    [RelayCommand]
    private void AddRoi()
    {
        RoiCalibrationItem item = new()
        {
            FieldKey  = AvailableFieldKeys.Count > 0 ? AvailableFieldKeys[0] : string.Empty,
            Source    = FieldSource.MainZone,
            OcrEngine = OcrEngine.WindowsMediaOcr,
        };

        Calibrations.Add(item);
        SelectedItem = item;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Удалить указанный ROI из списка.
    /// </summary>
    /// <param name="item">Элемент для удаления.</param>
    [RelayCommand]
    private void RemoveRoi(RoiCalibrationItem? item)
    {
        if (item is null) return;

        Calibrations.Remove(item);

        if (ReferenceEquals(SelectedItem, item))
        {
            SelectedItem = null;
        }

        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Сохранить все калибровки в репозиторий.
    /// Невалидные записи (пустой FieldKey) пропускаются с предупреждением.
    /// Координаты клампируются к [0..1] при конвертации в <see cref="RoiCalibration"/>.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            List<RoiCalibration> toSave = [];
            int skipped = 0;

            foreach (RoiCalibrationItem item in Calibrations)
            {
                if (string.IsNullOrWhiteSpace(item.FieldKey))
                {
                    skipped++;
                    continue;
                }

                toSave.Add(item.ToDomain());
            }

            await _settings.SaveRoiCalibrationsAsync(toSave.AsReadOnly());

            StatusMessage = skipped > 0
                ? $"Сохранено {toSave.Count} ROI. Пропущено {skipped} (пустой FieldKey)."
                : $"Сохранено {toSave.Count} ROI.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
