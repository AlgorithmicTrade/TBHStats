using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace TBHStats_App.Views.Controls;

/// <summary>
/// Пиксельный TextBlock с тёмной окантовкой вокруг символов (WinUI 3, T068 Phase 4).
/// Реализован как ContentControl: Content = Grid из 9 слоёв:
///   8 копий со смещением ±OutlineThickness по 8 направлениям (Stroke-кисть),
///   поверх — центральный TextBlock (Fill-кисть).
/// DependencyProperties: Text, Fill, Stroke, OutlineThickness,
///   FontSize, FontFamily, FontWeight, TextAlignment.
/// Привязка {Binding} к Text работает в режиме OneWay.
/// AutomationProperties и ToolTipService задаются на обёртке (родительском элементе).
/// </summary>
public sealed class OutlinedTextBlock : ContentControl
{
    // ── DependencyProperties ─────────────────────────────────────────────────

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(new SolidColorBrush(Colors.White), OnLayersChanged));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(new SolidColorBrush(Colors.Black), OnLayersChanged));

    public static readonly DependencyProperty OutlineThicknessProperty =
        DependencyProperty.Register(
            nameof(OutlineThickness),
            typeof(double),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(1.5, OnLayersChanged));

    public static readonly DependencyProperty TextFontSizeProperty =
        DependencyProperty.Register(
            nameof(TextFontSize),
            typeof(double),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(14.0, OnLayersChanged));

    public static readonly DependencyProperty TextFontFamilyProperty =
        DependencyProperty.Register(
            nameof(TextFontFamily),
            typeof(FontFamily),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(null, OnLayersChanged));

    public static readonly DependencyProperty TextFontWeightProperty =
        DependencyProperty.Register(
            nameof(TextFontWeight),
            typeof(FontWeight),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(FontWeights.Normal, OnLayersChanged));

    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(
            nameof(TextAlignment),
            typeof(TextAlignment),
            typeof(OutlinedTextBlock),
            new PropertyMetadata(TextAlignment.Left, OnLayersChanged));

    // ── CLR-обёртки ──────────────────────────────────────────────────────────

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double OutlineThickness
    {
        get => (double)GetValue(OutlineThicknessProperty);
        set => SetValue(OutlineThicknessProperty, value);
    }

    /// <summary>Размер шрифта слоёв (не совпадает с base.FontSize — переименован во избежание конфликта).</summary>
    public double TextFontSize
    {
        get => (double)GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    /// <summary>Шрифт слоёв.</summary>
    public FontFamily? TextFontFamily
    {
        get => (FontFamily?)GetValue(TextFontFamilyProperty);
        set => SetValue(TextFontFamilyProperty, value!);
    }

    /// <summary>Насыщенность шрифта слоёв.</summary>
    public FontWeight TextFontWeight
    {
        get => (FontWeight)GetValue(TextFontWeightProperty);
        set => SetValue(TextFontWeightProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    // ── Внутренние слои ───────────────────────────────────────────────────────

    private Grid?        _rootGrid;
    private TextBlock?   _centerBlock;
    private TextBlock[]? _strokeBlocks;

    private static readonly (double Dx, double Dy)[] StrokeOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),           (1,  0),
        (-1,  1), (0,  1), (1,  1),
    ];

    // ── Конструктор ───────────────────────────────────────────────────────────

    public OutlinedTextBlock()
    {
        // ContentControl имеет DefaultStyleKey, переопределяем для нашего типа,
        // но сразу ставим Content чтобы не ждать ApplyTemplate.
        IsTabStop = false;
        Padding   = new Thickness(0);
        HorizontalAlignment        = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment          = VerticalAlignment.Center;
        VerticalContentAlignment   = VerticalAlignment.Center;

        EnsureVisualTree();
    }

    // ── Построение дерева ─────────────────────────────────────────────────────

    private void EnsureVisualTree()
    {
        if (_rootGrid is not null) return;

        _rootGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        _strokeBlocks = new TextBlock[StrokeOffsets.Length];
        double t = OutlineThickness;

        for (int i = 0; i < StrokeOffsets.Length; i++)
        {
            var (dx, dy) = StrokeOffsets[i];
            TextBlock tb = MakeTextBlock(Stroke, t, dx, dy);
            _strokeBlocks[i] = tb;
            _rootGrid.Children.Add(tb);
        }

        _centerBlock = MakeTextBlock(Fill, 0, 0, 0);
        _rootGrid.Children.Add(_centerBlock);

        Content = _rootGrid;
    }

    private TextBlock MakeTextBlock(Brush foreground, double t, double dx, double dy)
    {
        FontFamily ff = ResolvedFontFamily();
        return new TextBlock
        {
            Text              = Text,
            FontSize          = TextFontSize,
            FontFamily        = ff,
            FontWeight        = TextFontWeight,
            TextAlignment     = TextAlignment,
            TextWrapping      = TextWrapping.NoWrap,
            Foreground        = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = t > 0
                ? new Thickness(dx * t, dy * t, -dx * t, -dy * t)
                : new Thickness(0),
        };
    }

    private FontFamily ResolvedFontFamily()
        => TextFontFamily ?? new FontFamily("Segoe UI");

    // ── Callbacks ─────────────────────────────────────────────────────────────

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (OutlinedTextBlock)d;
        string text = (string)(e.NewValue ?? string.Empty);

        if (ctrl._strokeBlocks is not null)
            foreach (TextBlock tb in ctrl._strokeBlocks)
                tb.Text = text;

        if (ctrl._centerBlock is not null)
            ctrl._centerBlock.Text = text;
    }

    private static void OnLayersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OutlinedTextBlock)d).RebuildLayers();

    private void RebuildLayers()
    {
        EnsureVisualTree();
        if (_strokeBlocks is null || _centerBlock is null) return;

        double t  = OutlineThickness;
        FontFamily ff = ResolvedFontFamily();

        for (int i = 0; i < StrokeOffsets.Length; i++)
        {
            var (dx, dy) = StrokeOffsets[i];
            TextBlock tb     = _strokeBlocks[i];
            tb.Text          = Text;
            tb.Foreground    = Stroke;
            tb.FontSize      = TextFontSize;
            tb.FontFamily    = ff;
            tb.FontWeight    = TextFontWeight;
            tb.TextAlignment = TextAlignment;
            tb.Margin = t > 0
                ? new Thickness(dx * t, dy * t, -dx * t, -dy * t)
                : new Thickness(0);
        }

        _centerBlock.Text          = Text;
        _centerBlock.Foreground    = Fill;
        _centerBlock.FontSize      = TextFontSize;
        _centerBlock.FontFamily    = ff;
        _centerBlock.FontWeight    = TextFontWeight;
        _centerBlock.TextAlignment = TextAlignment;
    }
}
