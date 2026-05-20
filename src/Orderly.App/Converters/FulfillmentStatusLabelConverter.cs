using System;
using System.Globalization;
using System.Windows.Data;
using Orderly.Core.Models;

namespace Orderly.App.Converters;

public sealed class FulfillmentStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string statusStr)
        {
            if (string.Equals(statusStr, "全部", StringComparison.Ordinal) || string.IsNullOrEmpty(statusStr))
            {
                return "全部状态";
            }
            return StringNarrationFulfillmentStatusCatalog.Resolve(statusStr).Label;
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
