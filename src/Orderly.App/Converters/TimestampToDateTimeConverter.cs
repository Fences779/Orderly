using System;
using System.Globalization;
using System.Windows.Data;

namespace Orderly.App.Converters;

public sealed class TimestampToDateTimeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long timestamp = 0;
        if (value is long valLong)
        {
            timestamp = valLong;
        }
        else if (value is int valInt)
        {
            timestamp = valInt;
        }

        if (timestamp > 0)
        {
            try
            {
                var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
        {
            var offset = new DateTimeOffset(dt);
            var ms = offset.ToUnixTimeMilliseconds();
            // Match the resolution: if target type expects seconds or if timestamp < 10_000_000_000 was used
            return ms / 1000;
        }
        return 0L;
    }
}
