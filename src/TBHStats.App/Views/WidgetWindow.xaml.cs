using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TBHStats.App.ViewModels;
using TBHStats.Capture.WindowTracking;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Компактный виджет живой статистики TBHStats (US1, T028).
/// DataContext корневого Grid задаётся из DI-контейнера (LiveStatsViewModel).
/// Title bar скрыт через OverlappedPresenter.SetBorderAndTitleBar(hasBorder:true, hasTitleBar:false).
/// Перемещение реализовано через Win32 WM_NCLBUTTONDOWN + HTCAPTION по PointerPressed на HeaderBorder.
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private readonly ILogger<WidgetWindow> _logger;

    // ── Трекинг дочерних окон ────────────────────────────────────────────────
    private CompareHostWindow?    _compareWindow;
    private CalibrationHostWindow? _calibrationWindow;

    // ── Win32 P/Invoke для drag без title bar ────────────────────────────────

    [LibraryImport("user32.dll")]
    private static partial void ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmNclbuttondown = 0x00A1;
    private const int  HtCaption       = 0x0002;

    public WidgetWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<WidgetWindow>>();

        // Задать DataContext корневого Grid (Window сам не имеет DataContext).
        LiveStatsViewModel vm = App.Services.GetRequiredService<LiveStatsViewModel>();
        RootGrid.DataContext = vm;

        // ── Скрыть стандартный Windows title bar (п.2 T068 Phase 4) ──────────
        // SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false):
        //   убирает заголовочную полосу и caption-кнопки системы,
        //   оставляет рамку (для ресайза); IsMaximizable/IsMinimizable=false.
        // Перемещение обеспечивается P/Invoke в OnHeaderPointerPressed.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Стартовый размер виджета (SC-006: компактный, ≤15% экрана; уменьшен T068 Правка 1).
        AppWindow.Resize(new SizeInt32(320, 270));
        AppWindow.Title = "TBHStats";

        // Иконка окна/таскбара (unpackaged): .ico рядом с exe (Content → Assets/AppIcon.ico).
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Применить сохранённые настройки (позиция, topmost) после загрузки.
        Activated += OnFirstActivated;

        // Сохранять позицию/размер при изменении.
        AppWindow.Changed += OnAppWindowChanged;

        // Остановить оркестратор при закрытии виджета.
        Closed += OnWidgetClosed;
    }

    // ─── Перемещение borderless-окна через Win32 ────────────────────────────

    /// <summary>
    /// PointerPressed на заголовочной плашке — запустить системное drag-перемещение.
    /// ReleaseCapture() + WM_NCLBUTTONDOWN(HTCAPTION) передаёт управление DWM:
    /// окно перемещается штатным образом, позиция фиксируется OnAppWindowChanged.
    /// </summary>
    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Пропускаем нажатие кнопки ✕ внутри заголовка. OriginalSource обычно вложенный
        // элемент кнопки (TextBlock/ContentPresenter), поэтому проверяем всю цепочку предков —
        // иначе клик по ✕ запустил бы drag-перемещение и проглотил бы Click кнопки.
        if (IsWithinButton(e.OriginalSource as DependencyObject)) return;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ReleaseCapture();
        SendMessageW(hwnd, WmNclbuttondown, new IntPtr(HtCaption), IntPtr.Zero);
    }

    /// <summary>
    /// true, если <paramref name="source"/> или любой его визуальный предок — <see cref="Button"/>.
    /// </summary>
    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    // ─── Кнопка ✕ (закрыть виджет) ──────────────────────────────────────────

    private void OnCloseWidgetClicked(object sender, RoutedEventArgs e) => Close();

    // ─── Первая активация: применить WidgetSettings ─────────────────────────

    private bool _settingsApplied;

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_settingsApplied) return;
        _settingsApplied = true;

        // Отписаться, чтобы не вызываться повторно.
        Activated -= OnFirstActivated;

        try
        {
            WidgetSettings ws = await LoadWidgetSettingsAsync().ConfigureAwait(true);
            ApplyWidgetSettings(ws);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить WidgetSettings при активации виджета.");
        }
    }

    // ─── Сохранение позиции/размера при изменении окна ──────────────────────

    private bool _savePending;

    private async void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Реагируем только на изменение положения или размера.
        if (!args.DidPositionChange && !args.DidSizeChange) return;

        // Дедупликация: не запускать несколько одновременных сохранений.
        if (_savePending) return;
        _savePending = true;

        try
        {
            // Небольшая задержка, чтобы не сохранять каждый пиксель при ресайзе.
            await Task.Delay(500).ConfigureAwait(true);

            WidgetSettings current = await LoadWidgetSettingsAsync().ConfigureAwait(true);

            // WidgetSettings использует init-сеттеры — пересоздаём объект с новыми координатами.
            WidgetSettings updated = new()
            {
                PosX           = AppWindow.Position.X,
                PosY           = AppWindow.Position.Y,
                Width          = AppWindow.Size.Width,
                Height         = AppWindow.Size.Height,
                AlwaysOnTop    = current.AlwaysOnTop,
                Theme          = current.Theme,
                PollIntervalMs = current.PollIntervalMs,
            };

            await SaveWidgetSettingsAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить позицию/размер виджета.");
        }
        finally
        {
            _savePending = false;
        }
    }

    // ─── Закрытие виджета: остановить оркестратор + вернуть окно игры ───────

    private async void OnWidgetClosed(object sender, WindowEventArgs args)
    {
        // ── Авто-закрытие дочерних окон (п.7 T068 Phase 4) ─────────────────
        if (_compareWindow is not null)
        {
            try { _compareWindow.Close(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при закрытии CompareHostWindow при закрытии виджета.");
            }
            _compareWindow = null;
        }

        if (_calibrationWindow is not null)
        {
            try { _calibrationWindow.Close(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при закрытии CalibrationHostWindow при закрытии виджета.");
            }
            _calibrationWindow = null;
        }

        // Вернуть окно игры, если оно было уведено за экран.
        try
        {
            IGameWindowController controller = App.Services.GetRequiredService<IGameWindowController>();
            if (controller.IsHidden)
            {
                IGameWindowTracker tracker = App.Services.GetRequiredService<IGameWindowTracker>();
                GameWindowHandle? window = tracker.FindGameWindow();
                if (window is not null)
                {
                    bool restored = controller.Restore(window);
                    _logger.LogInformation("Restore при закрытии виджета: {Result}", restored);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при возврате окна игры на закрытии виджета.");
        }

        // Остановить оркестратор.
        try
        {
            await App.Services
                .GetRequiredService<TBHStats.App.Services.IStatsOrchestrator>()
                .StopAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при остановке оркестратора.");
        }
    }

    // ─── Чекбокс «Поверх окон» (FR-015) ────────────────────────────────────

    private bool _applyingSettings;

    private async void OnAlwaysOnTopClicked(object sender, RoutedEventArgs e)
    {
        // Не реагировать во время программной инициализации чекбокса.
        if (_applyingSettings) return;

        bool isChecked = AlwaysOnTopCheckBox.IsChecked == true;

        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.IsAlwaysOnTop = isChecked;

            WidgetSettings current = await LoadWidgetSettingsAsync().ConfigureAwait(true);

            WidgetSettings updated = new()
            {
                PosX           = current.PosX,
                PosY           = current.PosY,
                Width          = current.Width,
                Height         = current.Height,
                AlwaysOnTop    = isChecked,
                Theme          = current.Theme,
                PollIntervalMs = current.PollIntervalMs,
            };

            await SaveWidgetSettingsAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при применении/сохранении AlwaysOnTop.");
        }
    }

    // ─── Кнопка увода окна игры за экран ────────────────────────────────────

    private async void OnHideGameClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            IGameWindowController controller = App.Services.GetRequiredService<IGameWindowController>();
            IGameWindowTracker    tracker    = App.Services.GetRequiredService<IGameWindowTracker>();

            GameWindowHandle? window = tracker.FindGameWindow();
            if (window is null)
            {
                _logger.LogWarning("HideGame: окно игры не найдено.");
                HideGameButton.Content = "Game not found";
                // Через секунду вернуть исходный текст
                await Task.Delay(1500).ConfigureAwait(true);
                HideGameButton.Content = controller.IsHidden ? "Restore game" : "Hide game";
                return;
            }

            if (controller.IsHidden)
            {
                bool result = controller.Restore(window);
                _logger.LogInformation("Restore окна игры: {Result}", result);
                HideGameButton.Content = "Hide game";
            }
            else
            {
                bool result = controller.HideOffScreen(window);
                _logger.LogInformation("HideOffScreen окна игры: {Result}", result);
                HideGameButton.Content = "Restore game";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при управлении позицией окна игры.");
        }
    }

    // ─── Кнопка «Сравнение» ─────────────────────────────────────────────────

    private void OnCompareClicked(object sender, RoutedEventArgs e) => OpenCompareWindow();

    // ─── Кнопка «Калибровка» ────────────────────────────────────────────────

    private void OnCalibrationClicked(object sender, RoutedEventArgs e) => OpenCalibrationWindow();

    // ─── KeyboardAccelerator-обработчики (Alt+C / Alt+K) ───────────────────

    private void OnCompareAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenCompareWindow();
        args.Handled = true;
    }

    private void OnCalibrationAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenCalibrationWindow();
        args.Handled = true;
    }

    // ─── Вспомогательные методы открытия окон ───────────────────────────────

    /// <summary>
    /// Открывает или активирует окно сравнения.
    /// При повторном вызове, если окно ещё живо — активирует его (не создаёт дубль).
    /// Подписывается на Closed чтобы обнулить поле при закрытии.
    /// </summary>
    private void OpenCompareWindow()
    {
        if (_compareWindow is not null)
        {
            try
            {
                _compareWindow.Activate();
                return;
            }
            catch
            {
                // Окно уже было уничтожено — создадим новое.
                _compareWindow = null;
            }
        }

        CompareHostWindow newWindow = new();
        _compareWindow = newWindow;
        _compareWindow.Closed += (_, _) => _compareWindow = null;
        _compareWindow.Activate();
    }

    /// <summary>
    /// Открывает или активирует окно калибровки.
    /// При повторном вызове, если окно ещё живо — активирует его (не создаёт дубль).
    /// Подписывается на Closed чтобы обнулить поле при закрытии.
    /// </summary>
    private void OpenCalibrationWindow()
    {
        if (_calibrationWindow is not null)
        {
            try
            {
                _calibrationWindow.Activate();
                return;
            }
            catch
            {
                // Окно уже было уничтожено — создадим новое.
                _calibrationWindow = null;
            }
        }

        CalibrationHostWindow newWindow = new();
        _calibrationWindow = newWindow;
        _calibrationWindow.Closed += (_, _) => _calibrationWindow = null;
        _calibrationWindow.Activate();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static async Task<WidgetSettings> LoadWidgetSettingsAsync()
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .GetWidgetSettingsAsync()
            .ConfigureAwait(false);
    }

    private static async Task SaveWidgetSettingsAsync(WidgetSettings settings)
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<ISettingsRepository>()
            .SaveWidgetSettingsAsync(settings)
            .ConfigureAwait(false);
    }

    private void ApplyWidgetSettings(WidgetSettings ws)
    {
        // Применить размер, если задан разумный.
        if (ws.Width > 0 && ws.Height > 0)
        {
            AppWindow.Resize(new SizeInt32((int)ws.Width, (int)ws.Height));
        }

        // Применить позицию, если задана (ненулевая).
        if (ws.PosX != 0 || ws.PosY != 0)
        {
            AppWindow.Move(new PointInt32((int)ws.PosX, (int)ws.PosY));
        }

        // Применить AlwaysOnTop из сохранённых настроек (FR-015).
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = ws.AlwaysOnTop;
        }

        // Синхронизировать чекбокс без срабатывания обработчика.
        _applyingSettings = true;
        try
        {
            AlwaysOnTopCheckBox.IsChecked = ws.AlwaysOnTop;
        }
        finally
        {
            _applyingSettings = false;
        }
    }
}
