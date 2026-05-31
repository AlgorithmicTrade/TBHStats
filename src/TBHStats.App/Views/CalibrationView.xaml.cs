using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TBHStats.App.ViewModels;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TBHStats_App.Views;

/// <summary>
/// Страница калибровки ROI (T030).
/// DataContext устанавливается из DI-контейнера (<see cref="CalibrationViewModel"/>).
/// Кадр игры показывается в <see cref="ScrollViewer"/> с зумом/панорамированием; контент
/// (<c>PreviewContent</c>) имеет размер кадра в пикселях, поэтому координаты ROI — это доли
/// от размера контента (без letterbox). Область ROI задаётся протяжкой мыши по кадру.
/// </summary>
public sealed partial class CalibrationView : Page
{
    // Цвет прямоугольников ROI на оверлее
    private static readonly Windows.UI.Color RoiStrokeColor = Colors.Lime;
    private static readonly Windows.UI.Color RoiSelectedStrokeColor = Colors.Yellow;
    private static readonly Windows.UI.Color DrawingStrokeColor = Colors.DeepSkyBlue;

    private const float ZoomStep = 1.4f;

    private CalibrationViewModel? _viewModel;

    // Состояние рисования рамки мышью (в координатах контента/кадра)
    private bool _isDragging;
    private Point _dragStart;
    private Rectangle? _dragRect;

    // Текущий выбранный элемент, на чьи PropertyChanged подписаны (для живой перерисовки)
    private RoiCalibrationItem? _trackedItem;

    // Подавляет перерисовку оверлея во время пакетного изменения координат (см. FinalizeDrag):
    // иначе каждое присваивание X/Y/W/H синхронно перестраивает визуальное дерево внутри
    // pointer-события → реентрантный крах Microsoft.UI.Xaml (0xC000027B).
    private bool _suppressOverlayRedraw;

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
        TrackSelectedItem(_viewModel.SelectedItem);

        // Автозагрузка калибровок при открытии страницы
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }

        // Попытка автоматически захватить кадр игры при открытии (если игра доступна).
        if (_viewModel.CaptureFrameCommand.CanExecute(null))
        {
            _ = _viewModel.CaptureFrameCommand.ExecuteAsync(null);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        _viewModel.Calibrations.CollectionChanged -= OnCalibrationsChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        TrackSelectedItem(null);
    }

    // ──────────────────────────────────────────────────────────────
    // ROI overlay redraw triggers
    // ──────────────────────────────────────────────────────────────

    private void PreviewContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawRoiOverlay();
    }

    private void OnCalibrationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawRoiOverlay();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CalibrationViewModel.SelectedItem):
                TrackSelectedItem(_viewModel?.SelectedItem);
                RedrawRoiOverlay();
                break;

            // Новый кадр захвачен → размер контента изменился: вписать в окно и перерисовать.
            case nameof(CalibrationViewModel.FrameWidthPx):
            case nameof(CalibrationViewModel.FrameHeightPx):
            case nameof(CalibrationViewModel.FrameImage):
                // Откладываем до завершения layout (Width/Height контента уже применятся).
                DispatcherQueue.TryEnqueue(() =>
                {
                    FitToView();
                    RedrawRoiOverlay();
                });
                break;
        }
    }

    /// <summary>
    /// Подписывается на изменения координат выбранного ROI, чтобы оверлей обновлялся
    /// при ручной правке X/Y/W/H в числовых полях.
    /// </summary>
    private void TrackSelectedItem(RoiCalibrationItem? item)
    {
        if (ReferenceEquals(_trackedItem, item)) return;

        if (_trackedItem is not null)
            _trackedItem.PropertyChanged -= OnSelectedItemPropertyChanged;

        _trackedItem = item;

        if (_trackedItem is not null)
            _trackedItem.PropertyChanged += OnSelectedItemPropertyChanged;
    }

    private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressOverlayRedraw) return;

        if (e.PropertyName is nameof(RoiCalibrationItem.X) or nameof(RoiCalibrationItem.Y)
            or nameof(RoiCalibrationItem.W) or nameof(RoiCalibrationItem.H)
            or nameof(RoiCalibrationItem.FieldKey))
        {
            RedrawRoiOverlay();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Масштаб (зум / вписать)
    // ──────────────────────────────────────────────────────────────

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(PreviewScrollViewer.ZoomFactor * ZoomStep);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(PreviewScrollViewer.ZoomFactor / ZoomStep);

    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitToView();

    private void ApplyZoom(double targetZoom)
    {
        float z = (float)Math.Clamp(targetZoom, PreviewScrollViewer.MinZoomFactor, PreviewScrollViewer.MaxZoomFactor);
        PreviewScrollViewer.ChangeView(null, null, z);
    }

    /// <summary>
    /// Вписывает кадр целиком в видимую область (zoom = min(viewport/frame)).
    /// </summary>
    private void FitToView()
    {
        double fw = _viewModel?.FrameWidthPx ?? 0;
        double fh = _viewModel?.FrameHeightPx ?? 0;
        if (fw <= 0 || fh <= 0) return;

        double vpW = PreviewScrollViewer.ViewportWidth;
        double vpH = PreviewScrollViewer.ViewportHeight;
        if (vpW <= 0 || vpH <= 0) return;

        float fit = (float)Math.Min(vpW / fw, vpH / fh);
        fit = (float)Math.Clamp(fit, PreviewScrollViewer.MinZoomFactor, PreviewScrollViewer.MaxZoomFactor);
        PreviewScrollViewer.ChangeView(0, 0, fit, disableAnimation: true);
    }

    // ──────────────────────────────────────────────────────────────
    // Рисование рамки ROI мышью (координаты = пиксели кадра)
    // ──────────────────────────────────────────────────────────────

    private void PreviewContent_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (_viewModel.FrameWidthPx <= 0 || _viewModel.FrameHeightPx <= 0)
        {
            _viewModel.StatusMessage = "Сначала нажмите «Захватить кадр».";
            return;
        }

        if (_viewModel.SelectedItem is null)
        {
            _viewModel.StatusMessage = "Выберите ROI в списке (или «Добавить ROI»), затем обведите область.";
            return;
        }

        // Рисуем только основной кнопкой (ЛКМ / перо / касание).
        var props = e.GetCurrentPoint(RoiOverlayCanvas).Properties;
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !props.IsLeftButtonPressed)
            return;

        double w = RoiOverlayCanvas.ActualWidth;
        double h = RoiOverlayCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        Point p = ClampToContent(e.GetCurrentPoint(RoiOverlayCanvas).Position, w, h);

        _isDragging = true;
        _dragStart = p;

        _dragRect = new Rectangle
        {
            Stroke = new SolidColorBrush(DrawingStrokeColor),
            StrokeThickness = 2.0,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 191, 255)),
            Width = 0,
            Height = 0,
        };
        Canvas.SetLeft(_dragRect, p.X);
        Canvas.SetTop(_dragRect, p.Y);
        RoiOverlayCanvas.Children.Add(_dragRect);

        PreviewContent.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PreviewContent_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _dragRect is null) return;

        double w = RoiOverlayCanvas.ActualWidth;
        double h = RoiOverlayCanvas.ActualHeight;
        Point p = ClampToContent(e.GetCurrentPoint(RoiOverlayCanvas).Position, w, h);

        double left = Math.Min(_dragStart.X, p.X);
        double top = Math.Min(_dragStart.Y, p.Y);

        Canvas.SetLeft(_dragRect, left);
        Canvas.SetTop(_dragRect, top);
        _dragRect.Width = Math.Abs(p.X - _dragStart.X);
        _dragRect.Height = Math.Abs(p.Y - _dragStart.Y);
        e.Handled = true;
    }

    private void PreviewContent_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        PreviewContent.ReleasePointerCapture(e.Pointer);
        double w = RoiOverlayCanvas.ActualWidth;
        double h = RoiOverlayCanvas.ActualHeight;
        FinalizeDrag(ClampToContent(e.GetCurrentPoint(RoiOverlayCanvas).Position, w, h));
        e.Handled = true;
    }

    private void PreviewContent_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        RemoveDragRect();
    }

    private static Point ClampToContent(Point p, double w, double h) =>
        new(Math.Clamp(p.X, 0, w), Math.Clamp(p.Y, 0, h));

    /// <summary>
    /// Завершает рисование: переводит пиксельный прямоугольник кадра в нормализованные доли
    /// и записывает их в выбранный ROI. Слишком маленькие рамки (случайный клик) игнорируются.
    /// </summary>
    private void FinalizeDrag(Point endPoint)
    {
        _isDragging = false;
        RemoveDragRect();

        if (_viewModel?.SelectedItem is not { } item) return;

        double w = RoiOverlayCanvas.ActualWidth;
        double h = RoiOverlayCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double left = Math.Min(_dragStart.X, endPoint.X);
        double top = Math.Min(_dragStart.Y, endPoint.Y);
        double right = Math.Max(_dragStart.X, endPoint.X);
        double bottom = Math.Max(_dragStart.Y, endPoint.Y);

        // Игнорируем «клик без перетаскивания» (< 3 px кадра в любом измерении).
        if (right - left < 3.0 || bottom - top < 3.0)
        {
            DispatcherQueue.TryEnqueue(RedrawRoiOverlay);
            return;
        }

        // Пиксели кадра → нормализованные доли. Перерисовку подавляем на время пакетного
        // присваивания, чтобы НЕ перестраивать визуальное дерево синхронно внутри pointer-события
        // (источник реентрантного краха 0xC000027B при повторной разметке).
        _suppressOverlayRedraw = true;
        try
        {
            item.X = Math.Clamp(left / w, 0.0, 1.0);
            item.Y = Math.Clamp(top / h, 0.0, 1.0);
            item.W = Math.Clamp((right - left) / w, 0.0, 1.0);
            item.H = Math.Clamp((bottom - top) / h, 0.0, 1.0);
        }
        finally
        {
            _suppressOverlayRedraw = false;
        }

        _viewModel.StatusMessage =
            $"Область задана: X={item.X:F3} Y={item.Y:F3} W={item.W:F3} H={item.H:F3}. " +
            "Не забудьте «Сохранить».";

        // Единственная перерисовка — отложенно, уже ПОСЛЕ завершения pointer-события.
        DispatcherQueue.TryEnqueue(RedrawRoiOverlay);
    }

    private void RemoveDragRect()
    {
        if (_dragRect is not null)
        {
            RoiOverlayCanvas.Children.Remove(_dragRect);
            _dragRect = null;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Canvas overlay rendering
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Перерисовывает оверлей ROI-прямоугольников на <see cref="RoiOverlayCanvas"/>.
    /// Координаты — нормализованные доли × размер контента (= пиксели кадра); оверлей лежит
    /// внутри зумируемого контента, поэтому рамки масштабируются вместе с кадром.
    /// </summary>
    private void RedrawRoiOverlay()
    {
        RoiOverlayCanvas.Children.Clear();
        _dragRect = null; // очищен вместе с детьми

        if (_viewModel is null) return;

        double w = RoiOverlayCanvas.ActualWidth;
        double h = RoiOverlayCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        RoiCalibrationItem? selected = _viewModel.SelectedItem;

        foreach (RoiCalibrationItem item in _viewModel.Calibrations)
        {
            bool isSelected = ReferenceEquals(item, selected);

            double left   = item.X * w;
            double top    = item.Y * h;
            double width  = item.W * w;
            double height = item.H * h;

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
