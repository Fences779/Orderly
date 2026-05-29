namespace Orderly.Core.Models;

public sealed class StringNarrationFulfillmentStatusMetric
{
    public string FulfillmentStatus { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int Count { get; set; }
    public bool IsTerminal { get; set; }
    public bool IsException { get; set; }
    public bool IsUnknown { get; set; }
}

public sealed class StringNarrationFulfillmentStats
{
    public IReadOnlyList<StringNarrationFulfillmentStatusMetric> Metrics { get; set; } = [];
    public int TotalCount { get; set; }
    public long CalculatedAt { get; set; }
    public StringNarrationWorkbenchDashboardStats WorkbenchDashboard { get; set; } = new();

    public int PaidPendingConfirmCount => GetCount(StringNarrationFulfillmentStatusCatalog.PaidPendingConfirm);
    public int PendingMakeCount => GetCount(StringNarrationFulfillmentStatusCatalog.PendingMake);
    public int MakingCount => GetCount(StringNarrationFulfillmentStatusCatalog.Making);
    public int ReadyToShipCount => GetCount(StringNarrationFulfillmentStatusCatalog.ReadyToShip);
    public int ShippedCount => GetCount(StringNarrationFulfillmentStatusCatalog.Shipped);
    public int ExceptionCount => GetCount(StringNarrationFulfillmentStatusCatalog.Exception);
    public int CompletedCount => GetCount(StringNarrationFulfillmentStatusCatalog.Completed);

    private int GetCount(string fulfillmentStatus)
    {
        return Metrics.FirstOrDefault(item =>
            string.Equals(item.FulfillmentStatus, fulfillmentStatus, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
    }
}

public sealed class StringNarrationFulfillmentStatusDefinition
{
    public string FulfillmentStatus { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsTerminal { get; init; }
    public bool IsException { get; init; }
    public bool IsUnknown { get; init; }
}

public static class StringNarrationFulfillmentStatusCatalog
{
    public const string PaidPendingConfirm = "paid_pending_confirm";
    public const string PendingMake = "pending_make";
    public const string Making = "making";
    public const string ReadyToShip = "ready_to_ship";
    public const string Shipped = "shipped";
    // 注意：exception 仅表示履约异常，不表示取消订单语义。
    public const string Exception = "exception";
    public const string Completed = "completed";

    private static readonly IReadOnlyList<StringNarrationFulfillmentStatusDefinition> Definitions =
    [
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = PaidPendingConfirm,
            Label = "已支付待确认",
            SortOrder = 10
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = PendingMake,
            Label = "待制作",
            SortOrder = 20
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = Making,
            Label = "制作中",
            SortOrder = 30
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = ReadyToShip,
            Label = "待发货",
            SortOrder = 40
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = Shipped,
            Label = "已发货",
            SortOrder = 50
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = Exception,
            Label = "异常",
            SortOrder = 60,
            IsException = true
        },
        new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = Completed,
            Label = "已完成",
            SortOrder = 70,
            IsTerminal = true
        }
    ];

    private static readonly IReadOnlyDictionary<string, StringNarrationFulfillmentStatusDefinition> Lookup =
        Definitions.ToDictionary(item => item.FulfillmentStatus, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["scheduled"] = PendingMake,
        ["in_production"] = Making,
        ["received"] = Completed,
        ["repurchase_due"] = Completed
    };

    public static IReadOnlyList<StringNarrationFulfillmentStatusDefinition> GetDefinitions()
    {
        return Definitions;
    }

    public static string Normalize(string? fulfillmentStatus)
    {
        if (string.IsNullOrWhiteSpace(fulfillmentStatus))
        {
            return string.Empty;
        }

        var normalized = fulfillmentStatus.Trim();
        return Aliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    public static StringNarrationFulfillmentStatusDefinition Resolve(string? fulfillmentStatus)
    {
        var normalized = Normalize(fulfillmentStatus);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new StringNarrationFulfillmentStatusDefinition
            {
                FulfillmentStatus = string.Empty,
                Label = "未知履约状态",
                SortOrder = 9_999,
                IsUnknown = true
            };
        }

        if (Lookup.TryGetValue(normalized, out var definition))
        {
            return definition;
        }

        return new StringNarrationFulfillmentStatusDefinition
        {
            FulfillmentStatus = normalized,
            Label = $"未知状态({normalized})",
            SortOrder = 9_999,
            IsUnknown = true
        };
    }
}
