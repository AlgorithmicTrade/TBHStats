using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// non-null → true (панель редактора активна), null → false (панель неактивна).
/// </summary>
public sealed class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
