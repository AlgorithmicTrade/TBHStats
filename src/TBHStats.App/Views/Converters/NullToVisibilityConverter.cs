using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// null → Visible (показывает заглушку «нет данных»), non-null → Collapsed.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
