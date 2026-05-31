using Microsoft.UI.Xaml.Data;

namespace TBHStats_App.Views.Converters;

/// <summary>
/// Инвертирует булево значение: true → false, false → true.
/// Используется для IsEnabled="{Binding IsBusy, Converter=...}".
/// </summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b && !b;
}
