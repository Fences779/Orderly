using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Orderly.App.Converters;

/// <summary>
/// 计数 → 可见性 / 布尔转换器（任务 17.2）：当集合计数大于 0 时返回可见 / <c>true</c>，
/// 否则返回折叠 / <c>false</c>。用于「搜索结果列表」仅在有命中时显示（Req 2.7：空结果不显示列表）。
/// 目标类型为 <see cref="bool"/> 时返回布尔（如绑定 <c>Popup.IsOpen</c>），否则返回 <see cref="Visibility"/>。
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0,
        };

        var hasItems = count > 0;
        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return hasItems;
        }

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
