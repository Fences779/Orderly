using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

// Pure, side-effect-free projection / mapping / formatting helpers extracted from
// MainViewModel.StringNarrationOrders.cs. None of these methods touch the gateway,
// the order service, or the frozen payment-success-to-fulfillment transaction loop.
public partial class MainViewModel
{
    private static bool HasStringNarrationStatsData(StringNarrationFulfillmentStats? stats, int fallbackOrderCount)
    {
        if (stats?.Metrics.Count > 0 && (stats.TotalCount > 0 || stats.Metrics.Any(item => item.Count > 0)))
        {
            return true;
        }

        return fallbackOrderCount == 0 && stats?.Metrics.Count > 0;
    }

    private static StringNarrationFulfillmentStats ResolveStringNarrationStatsFallback(
        StringNarrationFulfillmentStats? stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        if (HasStringNarrationStatsData(stats, orders.Count))
        {
            return stats!;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var status = StringNarrationFulfillmentStatusCatalog.Normalize(order.FulfillmentStatus);
            if (string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            counts.TryGetValue(status, out var current);
            counts[status] = current + 1;
        }

        var metrics = StringNarrationFulfillmentStatusCatalog.GetDefinitions()
            .OrderBy(item => item.SortOrder)
            .Select(item =>
            {
                counts.TryGetValue(item.FulfillmentStatus, out var count);
                return new StringNarrationFulfillmentStatusMetric
                {
                    FulfillmentStatus = item.FulfillmentStatus,
                    Label = item.Label,
                    SortOrder = item.SortOrder,
                    Count = count,
                    IsTerminal = item.IsTerminal,
                    IsException = item.IsException,
                    IsUnknown = item.IsUnknown
                };
            })
            .ToArray();

        return new StringNarrationFulfillmentStats
        {
            Metrics = metrics,
            TotalCount = counts.Values.Sum(),
            CalculatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static StringNarrationWorkbenchDashboardStats ResolveStringNarrationWorkbenchDashboard(
        StringNarrationWorkbenchDashboardStats? dashboard,
        StringNarrationFulfillmentStats stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        dashboard ??= new StringNarrationWorkbenchDashboardStats();
        var fallback = BuildStringNarrationWorkbenchDashboardFallback(stats, orders);
        var fallbackUsed = false;

        if (dashboard.TodayOrderCount == 0)
        {
            dashboard.TodayOrderCount = fallback.TodayOrderCount;
            fallbackUsed = true;
        }

        if (dashboard.TodayOrderCountDelta == 0)
        {
            dashboard.TodayOrderCountDelta = fallback.TodayOrderCountDelta;
            fallbackUsed = true;
        }

        if (dashboard.TodayRevenueAmount == 0)
        {
            dashboard.TodayRevenueAmount = fallback.TodayRevenueAmount;
            fallbackUsed = true;
        }

        if (dashboard.TodayRevenueAmountDelta == 0)
        {
            dashboard.TodayRevenueAmountDelta = fallback.TodayRevenueAmountDelta;
            fallbackUsed = true;
        }

        if (dashboard.PendingMakeCount != stats.PendingMakeCount)
        {
            dashboard.PendingMakeCount = stats.PendingMakeCount;
            fallbackUsed = true;
        }

        if (dashboard.MakingCount != stats.MakingCount)
        {
            dashboard.MakingCount = stats.MakingCount;
            fallbackUsed = true;
        }

        if (dashboard.ReadyToShipCount != stats.ReadyToShipCount)
        {
            dashboard.ReadyToShipCount = stats.ReadyToShipCount;
            fallbackUsed = true;
        }

        if (dashboard.ExceptionOrderCount != stats.ExceptionCount)
        {
            dashboard.ExceptionOrderCount = stats.ExceptionCount;
            fallbackUsed = true;
        }

        if (dashboard.UnfinishedOrderCount != fallback.UnfinishedOrderCount)
        {
            dashboard.UnfinishedOrderCount = fallback.UnfinishedOrderCount;
            fallbackUsed = true;
        }

        if (dashboard.LastSyncedAt <= 0)
        {
            dashboard.LastSyncedAt = fallback.LastSyncedAt;
            fallbackUsed = true;
        }

        if (dashboard.RecentBusinessTrendItems.Count == 0)
        {
            dashboard.RecentBusinessTrendItems = fallback.RecentBusinessTrendItems;
            fallbackUsed = true;
        }

        if (dashboard.FulfillmentPressureItems.Count == 0)
        {
            dashboard.FulfillmentPressureItems = BuildStringNarrationPressureItems(stats, dashboard.UnfinishedOrderCount);
            fallbackUsed = true;
        }

        dashboard.IsFallbackProjection = dashboard.IsFallbackProjection || fallbackUsed;
        return dashboard;
    }

    private static StringNarrationWorkbenchDashboardStats BuildStringNarrationWorkbenchDashboardFallback(
        StringNarrationFulfillmentStats stats,
        IReadOnlyList<StringNarrationOrderSummary> orders)
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var todayOrderCount = CountOrdersCreatedOn(orders, today);
        var yesterdayOrderCount = CountOrdersCreatedOn(orders, yesterday);
        var todayRevenue = SumRevenuePaidOn(orders, today);
        var yesterdayRevenue = SumRevenuePaidOn(orders, yesterday);
        var unfinishedOrderCount =
            stats.PaidPendingConfirmCount +
            stats.PendingMakeCount +
            stats.MakingCount +
            stats.ReadyToShipCount +
            stats.ExceptionCount;

        return new StringNarrationWorkbenchDashboardStats
        {
            TodayOrderCount = todayOrderCount,
            TodayOrderCountDelta = todayOrderCount - yesterdayOrderCount,
            TodayRevenueAmount = todayRevenue,
            TodayRevenueAmountDelta = todayRevenue - yesterdayRevenue,
            PendingMakeCount = stats.PendingMakeCount,
            MakingCount = stats.MakingCount,
            ReadyToShipCount = stats.ReadyToShipCount,
            ExceptionOrderCount = stats.ExceptionCount,
            UnfinishedOrderCount = unfinishedOrderCount,
            LastSyncedAt = stats.CalculatedAt > 0 ? stats.CalculatedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RecentBusinessTrendItems = BuildStringNarrationTrendItems(orders, today),
            FulfillmentPressureItems = BuildStringNarrationPressureItems(stats, unfinishedOrderCount),
            IsFallbackProjection = true
        };
    }

    private static IReadOnlyList<StringNarrationBusinessTrendPoint> BuildStringNarrationTrendItems(
        IReadOnlyList<StringNarrationOrderSummary> orders,
        DateTime today)
    {
        var items = new List<StringNarrationBusinessTrendPoint>();
        var start = today.AddDays(-6);
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            items.Add(new StringNarrationBusinessTrendPoint
            {
                DateKey = date.ToString("yyyy-MM-dd"),
                Label = date.ToString("MM-dd"),
                OrderCount = CountOrdersCreatedOn(orders, date),
                RevenueAmount = SumRevenuePaidOn(orders, date)
            });
        }

        return items;
    }

    private static IReadOnlyList<StringNarrationFulfillmentPressureMetric> BuildStringNarrationPressureItems(
        StringNarrationFulfillmentStats stats,
        int unfinishedOrderCount)
    {
        var targetCount = unfinishedOrderCount > 0 ? unfinishedOrderCount : stats.TotalCount;
        return new[]
        {
            BuildStringNarrationPressureItem(StringNarrationFulfillmentStatusCatalog.PendingMake, "待制作", stats.PendingMakeCount, targetCount),
            BuildStringNarrationPressureItem(StringNarrationFulfillmentStatusCatalog.ReadyToShip, "待发货", stats.ReadyToShipCount, targetCount)
        };
    }

    private static StringNarrationFulfillmentPressureMetric BuildStringNarrationPressureItem(
        string fulfillmentStatus,
        string label,
        int count,
        int targetCount)
    {
        return new StringNarrationFulfillmentPressureMetric
        {
            FulfillmentStatus = fulfillmentStatus,
            Label = label,
            Count = count,
            TargetCount = targetCount,
            Ratio = targetCount <= 0 ? 0 : (decimal)count / targetCount
        };
    }

    private static int CountOrdersCreatedOn(IReadOnlyList<StringNarrationOrderSummary> orders, DateTime date)
    {
        return orders.Count(order => TryGetLocalDate(order.CreatedAt, out var localDate) && localDate == date.Date);
    }

    private static decimal SumRevenuePaidOn(IReadOnlyList<StringNarrationOrderSummary> orders, DateTime date)
    {
        return orders
            .Where(order => TryGetLocalDate(order.PaidAt, out var localDate) && localDate == date.Date)
            .Sum(order => order.Amount);
    }

    private static bool TryGetLocalDate(long timestamp, out DateTime date)
    {
        date = default;
        if (timestamp <= 0)
        {
            return false;
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            date = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.Date;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static (string Label, string Text) BuildStringNarrationCopyText(StringNarrationOrderDetail detail, string field)
    {
        return field.Trim() switch
        {
            "orderNo" => ("订单号", detail.OrderNo),
            "wxOutTradeNo" => ("微信商户单号", detail.WxOutTradeNo),
            "phone" => ("手机号", detail.Address.ReceiverPhone),
            "address" => ("完整地址", detail.Address.FullAddressText == "无地址" ? string.Empty : detail.Address.FullAddressText),
            "trackingNo" => ("快递单号", detail.TrackingNo),
            "shippingInfo" => ("发货信息", JoinNonEmpty(" ", detail.Carrier, detail.TrackingNo)),
            "receiverInfo" => ("收件信息", JoinNonEmpty(Environment.NewLine, detail.Address.ReceiverName, detail.Address.ReceiverPhone, detail.Address.FullAddressText == "无地址" ? string.Empty : detail.Address.FullAddressText)),
            _ => ("内容", string.Empty)
        };
    }

    private static string JoinNonEmpty(string separator, params string[] values)
    {
        return string.Join(separator, values.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()));
    }

    private static string NormalizeStringNarrationFilter(string value)
    {
        return string.Equals(value, "全部", StringComparison.OrdinalIgnoreCase) ? string.Empty : value.Trim();
    }

    private static string BuildAddressText(StringNarrationAddressSnapshot address)
    {
        var summary = address.FullAddressText;
        var receiver = string.Join(" ", new[] { address.ReceiverName, address.ReceiverPhone }.Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(receiver) ? summary : $"{receiver}\n{summary}";
    }

    private static string BuildValue(string? value, string fallback = "暂无")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatGatewayTime(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "暂无";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "暂无";
        }
    }

    private static void AdjustStringNarrationMetricCount(
        ICollection<StringNarrationFulfillmentStatusMetric> metrics,
        string fulfillmentStatus,
        int delta)
    {
        if (string.IsNullOrWhiteSpace(fulfillmentStatus) || delta == 0)
        {
            return;
        }

        var metric = metrics.FirstOrDefault(item =>
            string.Equals(item.FulfillmentStatus, fulfillmentStatus, StringComparison.OrdinalIgnoreCase));
        if (metric is not null)
        {
            metric.Count = Math.Max(0, metric.Count + delta);
            return;
        }

        if (delta < 0)
        {
            return;
        }

        var definition = StringNarrationFulfillmentStatusCatalog.Resolve(fulfillmentStatus);
        metrics.Add(new StringNarrationFulfillmentStatusMetric
        {
            FulfillmentStatus = fulfillmentStatus,
            Label = definition.Label,
            SortOrder = definition.SortOrder,
            Count = delta,
            IsTerminal = definition.IsTerminal,
            IsException = definition.IsException,
            IsUnknown = definition.IsUnknown
        });
    }
}
