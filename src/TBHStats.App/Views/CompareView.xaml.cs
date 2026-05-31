using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TBHStats.App.ViewModels;
using TBHStats.Core.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Страница сравнения этапов (T041, US2, FR-008/FR-009/FR-017/FR-019).
/// DataContext устанавливается из DI-контейнера (<see cref="CompareViewModel"/>).
/// Управление метрикой и scope через code-behind: вызывают команды VM с нужным параметром.
/// </summary>
public sealed partial class CompareView : Page
{
    private CompareViewModel? _viewModel;

    public CompareView()
    {
        InitializeComponent();

        // Получаем ViewModel из DI-контейнера (Transient — новый экземпляр каждый раз).
        _viewModel = TBHStats_App.App.Services.GetRequiredService<CompareViewModel>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        // Синхронизируем RadioButton-состояния с текущим профилем VM.
        MetricGoldButton.IsChecked   = _viewModel.SelectedMetric == OptimizationMetric.GoldPerHour;
        MetricXpButton.IsChecked     = _viewModel.SelectedMetric == OptimizationMetric.XpPerHour;
        ScopeRecentButton.IsChecked  = _viewModel.Scope == AggregationScope.Recent;
        ScopeAllTimeButton.IsChecked = _viewModel.Scope == AggregationScope.AllTime;

        // Автозагрузка данных при открытии страницы.
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    // ── Обработчики RadioButton: метрика ─────────────────────────────────────

    private void OnMetricGoldClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.SetMetricCommand.CanExecute(OptimizationMetric.GoldPerHour))
        {
            _ = _viewModel.SetMetricCommand.ExecuteAsync(OptimizationMetric.GoldPerHour);
        }
    }

    private void OnMetricXpClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.SetMetricCommand.CanExecute(OptimizationMetric.XpPerHour))
        {
            _ = _viewModel.SetMetricCommand.ExecuteAsync(OptimizationMetric.XpPerHour);
        }
    }

    // ── Обработчики RadioButton: scope ───────────────────────────────────────

    private void OnScopeRecentClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.SetScopeCommand.CanExecute(AggregationScope.Recent))
        {
            _ = _viewModel.SetScopeCommand.ExecuteAsync(AggregationScope.Recent);
        }
    }

    private void OnScopeAllTimeClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.SetScopeCommand.CanExecute(AggregationScope.AllTime))
        {
            _ = _viewModel.SetScopeCommand.ExecuteAsync(AggregationScope.AllTime);
        }
    }
}
