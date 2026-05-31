using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TBHStats.App.ViewModels;
using TBHStats.Core.Models;
using TBHStats.Data.Repositories;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Компактный виджет живой статистики TBHStats (US1, T028).
/// DataContext корневого Grid задаётся из DI-контейнера (LiveStatsViewModel).
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private readonly ILogger<WidgetWindow> _logger;

    public WidgetWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<WidgetWindow>>();

        // Задать DataContext корневого Grid (Window сам не имеет DataContext).
        LiveStatsViewModel vm = App.Services.GetRequiredService<LiveStatsViewModel>();
        RootGrid.DataContext = vm;

        // Стартовый размер виджета (SC-006: компактный, ≤15% экрана).
        AppWindow.Resize(new SizeInt32(320, 220));
        AppWindow.Title = "TBHStats";

        // Применить сохранённые настройки (позиция, topmost) после загрузки.
        Activated += OnFirstActivated;

        // Сохранять позицию/размер при изменении.
        AppWindow.Changed += OnAppWindowChanged;

        // Остановить оркестратор при закрытии виджета.
        Closed += OnWidgetClosed;
    }

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
                PosX          = AppWindow.Position.X,
                PosY          = AppWindow.Position.Y,
                Width         = AppWindow.Size.Width,
                Height        = AppWindow.Size.Height,
                AlwaysOnTop   = current.AlwaysOnTop,
                Theme         = current.Theme,
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

    // ─── Закрытие виджета: остановить оркестратор ────────────────────────────

    private async void OnWidgetClosed(object sender, WindowEventArgs args)
    {
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

    // ─── Кнопка «Графики» ───────────────────────────────────────────────────

    private void OnChartsClicked(object sender, RoutedEventArgs e)
    {
        ChartsHostWindow chartsWindow = new();
        chartsWindow.Activate();
    }

    // ─── Кнопка «Сравнение» ─────────────────────────────────────────────────

    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        // Открываем CompareView в отдельном хост-окне.
        CompareHostWindow compareWindow = new();
        compareWindow.Activate();
    }

    // ─── Кнопка «Калибровка» ────────────────────────────────────────────────

    private void OnCalibrationClicked(object sender, RoutedEventArgs e)
    {
        // Открываем CalibrationView в отдельном хост-окне.
        CalibrationHostWindow calibWindow = new();
        calibWindow.Activate();
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

        // AlwaysOnTop через OverlappedPresenter.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = ws.AlwaysOnTop;
        }
    }
}
