using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Хост-окно для страницы калибровки ROI (T028).
/// Навигирует Frame на <see cref="CalibrationView"/> при активации.
/// </summary>
public sealed partial class CalibrationHostWindow : Window
{
    public CalibrationHostWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 650));
        AppWindow.Title = "TBHStats — Калибровка ROI";
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate после InitializeComponent, когда Frame готов.
        CalibrationFrame.Navigate(typeof(CalibrationView));
    }
}
