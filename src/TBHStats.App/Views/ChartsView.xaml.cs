using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TBHStats.App.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Страница трендов по этапу с LiveCharts2 (T045, US3, FR-018).
/// DataContext устанавливается из DI-контейнера (<see cref="ChartsViewModel"/>).
/// Выбор этапа обрабатывается во ViewModel (partial OnSelectedStageChanged) — code-behind не нужен.
/// </summary>
public sealed partial class ChartsView : Page
{
    private ChartsViewModel? _viewModel;

    public ChartsView()
    {
        InitializeComponent();

        // Получаем ViewModel из DI-контейнера.
        _viewModel = TBHStats_App.App.Services.GetRequiredService<ChartsViewModel>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        // Автозагрузка данных при открытии страницы.
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }
}
