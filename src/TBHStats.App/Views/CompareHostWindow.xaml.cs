using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Хост-окно для страницы сравнения этапов (T041).
/// Навигирует Frame на <see cref="CompareView"/> при активации.
/// </summary>
public sealed partial class CompareHostWindow : Window
{
    public CompareHostWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 600));
        AppWindow.Title = "TBHStats — Сравнение этапов";

        // Navigate после InitializeComponent, когда Frame готов.
        CompareFrame.Navigate(typeof(CompareView));
    }
}
