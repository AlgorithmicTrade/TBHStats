using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TBHStats.Capture;
using TBHStats.Capture.Chests;
using TBHStats.Capture.Ocr;
using TBHStats.Capture.Wgc;
using TBHStats.Core.Mechanics;
using TBHStats.Core.Models;
using TBHStats.Core.Parsing;
using TBHStats.Data.Repositories;
using Windows.ApplicationModel.DataTransfer;
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
    private readonly IOcrReader _ocrReader;
    private readonly IChestPanelAnalyzer _chestPanelAnalyzer;
    private readonly IChestZoneAnalyzer _chestZoneAnalyzer;

    // Последний захваченный кадр; удерживается для команды TestSelectedRoiOcrAsync.
    // Диспозится при каждом новом захвате (удерживается максимум один кадр).
    private CapturedFrame? _lastFrame;

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
    // OCR-предпросмотр выбранной ROI
    // ──────────────────────────────────────────────────────────────

    /// <summary>Распознанный текст из выбранной ROI (заполняется командой <see cref="TestSelectedRoiOcrCommand"/>).</summary>
    [ObservableProperty]
    private string _ocrPreviewText = string.Empty;

    /// <summary>Статус последней OCR-проверки (уверенность / подсказка об ошибке).</summary>
    [ObservableProperty]
    private string _ocrPreviewStatus = string.Empty;

    /// <summary>Уверенность последнего распознавания [0..1] (0, если не распознано). Для копирования.</summary>
    private double _lastOcrConfidence;

    // ──────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Инициализирует ViewModel калибровки.
    /// </summary>
    /// <param name="settings">Репозиторий настроек (загрузка / сохранение ROI).</param>
    /// <param name="gameMechanics">Конфиг механик (вкладки, FieldKey).</param>
    /// <param name="captureSession">Сессия захвата кадра игры.</param>
    /// <param name="ocrReader">Движок OCR для живого предпросмотра текстовых ROI.</param>
    /// <param name="chestPanelAnalyzer">Визуальный анализатор одиночной плашки сундука (тип по цвету + число точек).</param>
    /// <param name="chestZoneAnalyzer">Зонный анализатор: локализует все плашки в широкой ROI и считает точки по рядам (ADR-023).</param>
    public CalibrationViewModel(
        ISettingsRepository settings,
        IGameMechanics gameMechanics,
        ICaptureSession captureSession,
        IOcrReader ocrReader,
        IChestPanelAnalyzer chestPanelAnalyzer,
        IChestZoneAnalyzer chestZoneAnalyzer)
    {
        _settings = settings;
        _gameMechanics = gameMechanics;
        _captureSession = captureSession;
        _ocrReader = ocrReader;
        _chestPanelAnalyzer = chestPanelAnalyzer;
        _chestZoneAnalyzer = chestZoneAnalyzer;

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
            // Не используем using: кадр удерживается в _lastFrame для TestSelectedRoiOcrAsync.
            CapturedFrame? frame = await _captureSession.TryGetFrameAsync(ct);
            if (frame is null)
            {
                FrameImage = null;
                FrameWidthPx = 0;
                FrameHeightPx = 0;
                StatusMessage = "Кадр недоступен: окно игры не найдено, свёрнуто или ещё не готово. " +
                                "Откройте игру на нужной вкладке и повторите.";
                return;
            }

            // Диспозим предыдущий кадр перед сохранением нового.
            CapturedFrame? previous = _lastFrame;
            _lastFrame = frame;
            previous?.Dispose();

            // SoftwareBitmapSource требует BGRA8 с premultiplied-альфой.
            // converted — временный, диспозится здесь; frame.Bitmap удерживается вместе с кадром.
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

    /// <summary>
    /// Запустить OCR на последнем захваченном кадре по выбранной ROI и показать результат
    /// в свойствах <see cref="OcrPreviewText"/> / <see cref="OcrPreviewStatus"/>.
    /// Вызывается автоматически после рисования рамки и при смене выбранного элемента.
    /// </summary>
    [RelayCommand]
    private async Task TestSelectedRoiOcrAsync(CancellationToken ct)
    {
        if (_lastFrame is null)
        {
            OcrPreviewText = string.Empty;
            OcrPreviewStatus = "Сначала захватите кадр.";
            return;
        }

        if (SelectedItem is null)
        {
            OcrPreviewText = string.Empty;
            OcrPreviewStatus = "Выберите ROI.";
            return;
        }

        if (SelectedItem.W <= 0 || SelectedItem.H <= 0)
        {
            OcrPreviewStatus = "Обведите область ROI.";
            return;
        }

        try
        {
            RoiCalibration roi = SelectedItem.ToDomain();

            // Для зонной ROI chestZone вызываем зонный анализатор (ADR-023):
            // локализует все плашки по цвету и возвращает число точек для каждой.
            if (roi.FieldKey == "chestZone")
            {
                GameMechanicsConfig cfg = _gameMechanics.Current;
                // Не используем ConfigureAwait(false): возвращаемся на UI-поток для присваивания свойств.
                IReadOnlyDictionary<int, int> zone = await _chestZoneAnalyzer.AnalyzeZoneAsync(_lastFrame, roi, cfg, ct);

                if (zone.Count > 0)
                {
                    // Собираем строку по всем найденным типам, упорядоченную по SortOrder.
                    IEnumerable<string> parts = zone
                        .OrderBy(kv =>
                        {
                            ChestType? ct2 = cfg.ChestTypes.FirstOrDefault(c => c.Id == kv.Key);
                            return ct2?.SortOrder ?? kv.Key;
                        })
                        .Select(kv =>
                        {
                            ChestType? ct2 = cfg.ChestTypes.FirstOrDefault(c => c.Id == kv.Key);
                            string label = ct2?.DisplayName ?? kv.Key.ToString();
                            return $"{label}: {kv.Value}";
                        });

                    OcrPreviewText = string.Join(", ", parts);
                    OcrPreviewStatus = $"Зона: распознано плашек — {zone.Count}";
                }
                else
                {
                    OcrPreviewText = "(плашки не распознаны)";
                    OcrPreviewStatus = "В зоне не найдено плашек сундуков: ROI должна покрывать всю группу плашек (по горизонтали и с точками снизу)";
                }

                return;
            }

            // Для chest-ROI используем визуальный анализатор (тип по цвету плашки + число точек),
            // OCR здесь бесполезен — точки графические, текста нет.
            if (ChestFieldKey.TryParse(roi.FieldKey, out _, out _))
            {
                GameMechanicsConfig cfg = _gameMechanics.Current;
                // Не используем ConfigureAwait(false): возвращаемся на UI-поток для присваивания свойств.
                ChestPanelReading reading = await _chestPanelAnalyzer.AnalyzeChestPanelAsync(_lastFrame, roi, cfg, ct);

                if (reading.ChestTypeId is int typeId)
                {
                    ChestType? chestType = cfg.ChestTypes.FirstOrDefault(c => c.Id == typeId);
                    string label = chestType?.DisplayName ?? typeId.ToString();
                    OcrPreviewText = $"{label}: {reading.DotCount} точк(а/и)";
                    OcrPreviewStatus = $"Плашка распознана · совпадение {reading.PanelMatch:F2}";
                }
                else
                {
                    OcrPreviewText = "(плашка не распознана)";
                    OcrPreviewStatus = "Тип сундука не определён: ROI должна покрывать цветную плашку целиком";
                }

                return;
            }

            // Не используем ConfigureAwait(false): возвращаемся на UI-поток для присваивания свойств.
            OcrResult res = await _ocrReader.ReadAsync(_lastFrame, roi, ct);

            OcrPreviewText = string.IsNullOrEmpty(res.RawText) ? "(пусто)" : res.RawText;
            _lastOcrConfidence = res.Recognized ? res.Confidence : 0.0;
            OcrPreviewStatus = res.Recognized
                ? $"Распознано · уверенность {res.Confidence:F2}"
                : "Не распознано (текст не найден в области)";
        }
        catch (OperationCanceledException)
        {
            // отмена — нормальный выход
        }
        catch (Exception ex)
        {
            OcrPreviewStatus = $"Ошибка: {ex.Message}";
        }
    }

    /// <summary>
    /// Копирует в буфер обмена название ROI (FieldKey), распознанный текст и уверенность OCR
    /// в формате «&lt;FieldKey&gt;: &lt;текст&gt; (уверенность NN%)». Плейсхолдер «(пусто)» и пустые значения не копируются.
    /// </summary>
    [RelayCommand]
    private void CopyOcrPreview()
    {
        string text = OcrPreviewText;
        if (string.IsNullOrWhiteSpace(text) || text == "(пусто)")
        {
            OcrPreviewStatus = "Нечего копировать.";
            return;
        }

        string? fieldKey = SelectedItem?.FieldKey;
        string head = string.IsNullOrWhiteSpace(fieldKey) ? text : $"{fieldKey}: {text}";
        string payload = $"{head} (уверенность {_lastOcrConfidence:P0})";

        try
        {
            DataPackage package = new();
            package.SetText(payload);
            Clipboard.SetContent(package);
            OcrPreviewStatus = "Скопировано в буфер обмена.";
        }
        catch (Exception ex)
        {
            OcrPreviewStatus = $"Ошибка копирования: {ex.Message}";
        }
    }
}
