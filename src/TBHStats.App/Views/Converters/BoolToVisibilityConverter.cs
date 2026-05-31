using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// Конвертирует bool в Visibility: true → Visible, false → Collapsed.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
