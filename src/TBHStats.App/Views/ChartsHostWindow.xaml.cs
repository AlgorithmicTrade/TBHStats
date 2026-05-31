using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Хост-окно для страницы трендов по этапу (T045).
/// Навигирует Frame на <see cref="ChartsView"/> при активации.
/// </summary>
public sealed partial class ChartsHostWindow : Window
{
    public ChartsHostWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(860, 700));
        AppWindow.Title = "TBHStats — Тренды";

        // Navigate после InitializeComponent, когда Frame готов.
        ChartsFrame.Navigate(typeof(ChartsView));
    }
}
