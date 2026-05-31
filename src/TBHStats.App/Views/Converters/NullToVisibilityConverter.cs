using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// null → Visible (показывает заглушку «нет данных»), non-null → Collapsed.
/// ConverterParameter="Invert" инвертирует: non-null → Visible, null → Collapsed.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isNull = value is null;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? !isNull : isNull;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
