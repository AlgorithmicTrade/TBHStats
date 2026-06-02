using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// Инвертирует bool → Visibility: false → Visible, true → Collapsed.
/// Используется для переключения видимости блока «Забег» (виден когда IsSessionView == false).
/// </summary>
public sealed class BoolToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Collapsed;
}
