using System.Globalization;
using System.Windows.Data;

namespace Orderly.App.Converters;

/// <summary>
/// Converts a boolean to one of two strings separated by a pipe in <see cref="ConverterParameter"/>.
/// The first string is returned when the value is true, the second when false.
/// Example parameter: "Admin view|Employee view".
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BooleanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter?.ToString() ?? string.Empty).Split('|');
        var trueText = parts.Length > 0 ? parts[0] : string.Empty;
        var falseText = parts.Length > 1 ? parts[1] : string.Empty;
        return value is true ? trueText : falseText;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
