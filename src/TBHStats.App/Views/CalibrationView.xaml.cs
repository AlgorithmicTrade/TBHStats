using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TBHStats.App.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Страница калибровки ROI (T030).
/// DataContext устанавливается из DI-контейнера (<see cref="CalibrationViewModel"/>).
/// Оверлей ROI-прямоугольников на Canvas перерисовывается при изменении размера
/// предпросмотра или коллекции калибровок.
/// </summary>
public sealed partial class CalibrationView : Page
{
    // Цвет прямоугольников ROI на оверлее
    private static readonly Windows.UI.Color RoiStrokeColor = Colors.Lime;
    private static readonly Windows.UI.Color RoiSelectedStrokeColor = Colors.Yellow;

    private CalibrationViewModel? _viewModel;

    public CalibrationView()
    {
        InitializeComponent();

        // Получаем ViewModel из DI-контейнера
        _viewModel = TBHStats_App.App.Services.GetService(typeof(CalibrationViewModel)) as CalibrationViewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ──────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.Calibrations.CollectionChanged += OnCalibrationsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Автозагрузка калибровок при открытии страницы
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.Calibrations.CollectionChanged -= OnCalibrationsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    // ──────────────────────────────────────────────────────────────
    // ROI overlay redraw triggers
    // ──────────────────────────────────────────────────────────────

    private void PreviewGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawRoiOverlay();
    }

    private void OnCalibrationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawRoiOverlay();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationViewModel.SelectedItem))
        {
            RedrawRoiOverlay();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Canvas overlay rendering
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Перерисовывает оверлей ROI-прямоугольников на <see cref="RoiOverlayCanvas"/>.
    /// Каждый ROI представлен прямоугольником, позиционированным через Canvas.Left/Top
    /// в пикселях, вычисленных из нормализованных долей.
    /// </summary>
    private void RedrawRoiOverlay()
    {
        RoiOverlayCanvas.Children.Clear();

        if (_viewModel is null) return;

        double canvasW = RoiOverlayCanvas.ActualWidth;
        double canvasH = RoiOverlayCanvas.ActualHeight;

        // Если canvas ещё не измерен — пропустить
        if (canvasW <= 0 || canvasH <= 0) return;

        CalibrationViewModel? vm = _viewModel;
        RoiCalibrationItem? selected = vm.SelectedItem;

        foreach (RoiCalibrationItem item in vm.Calibrations)
        {
            bool isSelected = ReferenceEquals(item, selected);

            double left   = item.X * canvasW;
            double top    = item.Y * canvasH;
            double width  = item.W * canvasW;
            double height = item.H * canvasH;

            // Пропускаем нулевые прямоугольники
            if (width < 1 || height < 1) continue;

            Rectangle rect = new()
            {
                Width           = width,
                Height          = height,
                Stroke          = new SolidColorBrush(isSelected ? RoiSelectedStrokeColor : RoiStrokeColor),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Fill            = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 0, 255, 0)),
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            RoiOverlayCanvas.Children.Add(rect);

            // Подпись FieldKey
            if (!string.IsNullOrEmpty(item.FieldKey))
            {
                TextBlock label = new()
                {
                    Text       = item.FieldKey,
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(isSelected ? RoiSelectedStrokeColor : RoiStrokeColor),
                };

                Canvas.SetLeft(label, left + 2);
                Canvas.SetTop(label, top + 2);
                RoiOverlayCanvas.Children.Add(label);
            }
        }
    }
}
