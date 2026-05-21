using System.Text.Json;

namespace Orderly.Core.Models;

public sealed class StringNarrationOrderQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Keyword { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FulfillmentStatus { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
}

public sealed class StringNarrationPageInfo
{
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
    public string NextCursor { get; set; } = string.Empty;
    public int Total { get; set; }
}

public sealed class StringNarrationOrderListResult
{
    public IReadOnlyList<StringNarrationOrderSummary> Orders { get; set; } = [];
    public StringNarrationPageInfo PageInfo { get; set; } = new();
    public StringNarrationFulfillmentStats Stats { get; set; } = new();
}

public sealed class StringNarrationGatewayResponse<T>
{
    public bool Ok { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

public sealed class StringNarrationWhoamiResult
{
    public bool Authorized { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorOpenid { get; set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; set; } = [];
}

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

    public static IReadOnlyList<StringNarrationFulfillmentStatusDefinition> GetDefinitions()
    {
        return Definitions;
    }

    public static string Normalize(string? fulfillmentStatus)
    {
        return string.IsNullOrWhiteSpace(fulfillmentStatus) ? string.Empty : fulfillmentStatus.Trim();
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

public sealed class StringNarrationExceptionSnapshot
{
    public string Type { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string AdminResolutionRemark { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = string.Empty;
    public string ResolutionAction { get; set; } = string.Empty;
    public string ResolvedBy { get; set; } = string.Empty;
    public long SlaDueAt { get; set; }
    public long LastCheckedAt { get; set; }
    public long DetectedAt { get; set; }
    public long ResolvedAt { get; set; }
    public bool HasException { get; set; }
    public bool RequiresManualReview { get; set; }
    public bool IsResolved { get; set; }
    public bool HasMissingAddress { get; set; }
    public bool HasMissingReceiverPhone { get; set; }
    public bool HasMissingTrackingNo { get; set; }
    public bool HasShippingSyncFailure { get; set; }
    public bool HasProductionOrderMissing { get; set; }
    public bool HasWorkOrderMissing { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public JsonElement? Raw { get; set; }

    public string SummaryText
    {
        get
        {
            if (!HasException)
            {
                return "无异常";
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LevelText))
            {
                parts.Add(LevelText);
            }

            if (!string.IsNullOrWhiteSpace(StatusText))
            {
                parts.Add(StatusText);
            }

            if (!string.IsNullOrWhiteSpace(Priority))
            {
                parts.Add($"优先级:{PriorityLabel}");
            }

            if (!string.IsNullOrWhiteSpace(ResolutionStatus))
            {
                parts.Add($"处理:{ResolutionStatusLabel}");
            }

            if (!string.IsNullOrWhiteSpace(EffectiveReason))
            {
                parts.Add(TrimForSummary(EffectiveReason, 36));
            }

            return parts.Count == 0 ? "异常待人工确认" : string.Join(" / ", parts);
        }
    }

    public string NormalizedPriority => StringNarrationExceptionFieldCatalog.NormalizePriority(Priority);
    public int PrioritySortOrder => StringNarrationExceptionFieldCatalog.GetPrioritySortOrder(Priority);
    public string PriorityLabel => StringNarrationExceptionFieldCatalog.GetPriorityLabel(Priority);
    public string NormalizedResolutionStatus => StringNarrationExceptionFieldCatalog.NormalizeResolutionStatus(ResolutionStatus);
    public string ResolutionStatusLabel => StringNarrationExceptionFieldCatalog.GetResolutionStatusLabel(ResolutionStatus);
    public int ResolutionStatusSortOrder => StringNarrationExceptionFieldCatalog.GetResolutionStatusSortOrder(ResolutionStatus);
    public bool IsResolvedByNormalizedStatus => StringNarrationExceptionFieldCatalog.IsResolvedState(ResolutionStatus);
    public int ResolvedSortOrder => IsResolved ? 1 : 0;
    public long SlaDueSortTimestamp => NormalizeGatewayTimestamp(SlaDueAt);
    public long DetectedSortTimestamp => NormalizeGatewayTimestamp(DetectedAt);
    public string EffectiveCode => BuildValue(
        Code,
        StringNarrationExceptionFieldCatalog.InferExceptionCode(
            HasException,
            HasMissingAddress,
            HasMissingReceiverPhone,
            HasMissingTrackingNo,
            HasShippingSyncFailure,
            HasProductionOrderMissing,
            HasWorkOrderMissing,
            fulfillmentStatus: string.Empty));
    public string EffectiveType => BuildValue(Type, EffectiveCode);
    public string EffectiveReason => BuildValue(Reason, StringNarrationExceptionFieldCatalog.GetExceptionReason(EffectiveCode));
    public string ExceptionCategoryLabel => StringNarrationExceptionFieldCatalog.GetExceptionLabel(EffectiveCode);
    public string LevelText => BuildValue(Level, HasException ? "待判级" : "无");
    public string StatusText => BuildValue(Status, IsResolved ? "已解决" : (HasException ? "待处理" : "无"));
    public string OwnerText => BuildValue(Owner, BuildValue(Assignee, "未分配"));
    public string AssigneeText => BuildValue(Assignee, "未分配");
    public string PriorityText => BuildValue(Priority, "normal");
    public string ResolutionStatusText => BuildValue(ResolutionStatus, IsResolved ? "resolved" : (HasException ? "open" : "none"));
    public string ResolutionActionText => BuildValue(ResolutionAction, "无");
    public string ResolvedByText => BuildValue(ResolvedBy, IsResolved ? "系统/未知" : "未处理");
    public string SlaDueAtText => FormatGatewayTime(SlaDueAt);
    public string LastCheckedAtText => FormatGatewayTime(LastCheckedAt);
    public string DetectedAtText => FormatGatewayTime(DetectedAt);
    public string ResolvedAtText => IsResolved ? FormatGatewayTime(ResolvedAt) : "未解决";
    public string TagsText => Tags.Count == 0 ? "无" : string.Join(", ", Tags);

    private static string BuildValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TrimForSummary(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength]}...";
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

    private static long NormalizeGatewayTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
    }
}

public class StringNarrationOrderSummary
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true
    };

    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string WxOutTradeNo { get; set; } = string.Empty;
    public string WxTransactionId { get; set; } = string.Empty;
    public string OwnerOpenid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FulfillmentStatus { get; set; } = string.Empty;
    public string WxShippingSyncStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int ItemCount { get; set; }
    public long CreatedAt { get; set; }
    public long PaidAt { get; set; }
    public string TitleSnapshot { get; set; } = string.Empty;
    public string CoverSnapshot { get; set; } = string.Empty;
    public StringNarrationAddressSnapshot Address { get; set; } = new();
    public JsonElement? GiftConfig { get; set; }
    public string TrackingNo { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string ExpressCompanyCode { get; set; } = string.Empty;
    public string ShippingRemark { get; set; } = string.Empty;
    public string AdminRemark { get; set; } = string.Empty;
    public long ShippedAt { get; set; }
    public long CompletedAt { get; set; }
    public long FulfillmentUpdatedAt { get; set; }
    public StringNarrationExceptionSnapshot Exception { get; set; } = new();
    public StringNarrationExceptionSnapshot ExceptionSnapshot
    {
        get => Exception;
        set => Exception = value ?? new StringNarrationExceptionSnapshot();
    }
    public bool HasException { get; set; }

    public string OrderNoText => BuildValue(OrderNo, "无 orderNo");
    public string WxOutTradeNoText => BuildValue(WxOutTradeNo, "无 wxOutTradeNo");
    public string WxTransactionIdText => BuildValue(WxTransactionId, "无 wxTransactionId");
    public string StatusText => BuildValue(Status, "未知 status");
    public string FulfillmentStatusText => BuildValue(FulfillmentStatus, "未知 fulfillmentStatus");
    public string FulfillmentStatusLabel => StringNarrationFulfillmentStatusCatalog.Resolve(FulfillmentStatus).Label;
    public int FulfillmentStatusSortOrder => StringNarrationFulfillmentStatusCatalog.Resolve(FulfillmentStatus).SortOrder;
    public bool FulfillmentIsTerminal => StringNarrationFulfillmentStatusCatalog.Resolve(FulfillmentStatus).IsTerminal;
    public bool FulfillmentIsException => StringNarrationFulfillmentStatusCatalog.Resolve(FulfillmentStatus).IsException;
    public string WxShippingSyncStatusText => BuildValue(WxShippingSyncStatus, "未知 wxShippingSyncStatus");
    public string AmountText => $"¥{Amount:N0}";
    public string ItemCountText => $"{ItemCount} 件";
    public string CreatedAtText => FormatGatewayTime(CreatedAt);
    public string PaidAtText => FormatGatewayTime(PaidAt);
    public string TitleSnapshotText => BuildValue(TitleSnapshot, "无 titleSnapshot");
    public string CoverSnapshotText => BuildValue(CoverSnapshot, "无 coverSnapshot");
    public string ReceiverSummaryText => Address.ReceiverSummaryText;
    public string ReceiverPhoneText => BuildValue(Address.ReceiverPhone, "无手机号");
    public string FullAddressText => Address.FullAddressText;
    public string TrackingSummaryText => string.IsNullOrWhiteSpace(TrackingNo) && string.IsNullOrWhiteSpace(Carrier)
        ? "暂无物流"
        : $"{BuildValue(Carrier, "未填快递公司")} / {BuildValue(TrackingNo, "未填单号")}";
    public string AdminRemarkSummaryText => TrimForSummary(AdminRemark, "无后台备注");
    public string GiftConfigText => FormatJson(GiftConfig, "无/旧订单未记录");
    public string ShippedAtText => FormatGatewayTime(ShippedAt);
    public string CompletedAtText => FormatGatewayTime(CompletedAt);
    public string FulfillmentUpdatedAtText => FormatGatewayTime(FulfillmentUpdatedAt);
    public string ExceptionSummaryText => Exception.SummaryText;
    public string ExceptionLevelText => Exception.LevelText;
    public string ExceptionStatusText => Exception.StatusText;
    public int ExceptionResolvedSortOrder => Exception.ResolvedSortOrder;
    public int ExceptionPrioritySortOrder => Exception.PrioritySortOrder;
    public int ExceptionResolutionStatusSortOrder => Exception.ResolutionStatusSortOrder;
    public long ExceptionSlaDueSortTimestamp => Exception.SlaDueSortTimestamp;
    public long ExceptionDetectedSortTimestamp =>
        Exception.DetectedSortTimestamp > 0
            ? Exception.DetectedSortTimestamp
            : NormalizeGatewayTimestamp(FulfillmentUpdatedAt) > 0
                ? NormalizeGatewayTimestamp(FulfillmentUpdatedAt)
                : NormalizeGatewayTimestamp(PaidAt);

    protected static string FormatJson(JsonElement? value, string fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Serialize(value.Value, SnapshotJsonOptions);
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    protected static string BuildValue(string? value, string fallback = "无")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    protected static string TrimForSummary(string? value, string fallback)
    {
        var normalized = BuildValue(value, fallback);
        return normalized.Length <= 48 ? normalized : $"{normalized[..48]}...";
    }

    protected static string FormatGatewayTime(long timestamp)
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

    private static long NormalizeGatewayTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
    }
}

public sealed class StringNarrationOrderDetail : StringNarrationOrderSummary
{
    public IReadOnlyList<StringNarrationOrderItemSnapshot> ItemsSnapshot { get; set; } = [];
    public JsonElement? PricingSnapshot { get; set; }
    public JsonElement? DesignSnapshot { get; set; }
    public JsonElement? StorySnapshot { get; set; }
    public string Remark { get; set; } = string.Empty;
    public StringNarrationProductionOrderSnapshot ProductionOrder { get; set; } = new();
    public IReadOnlyList<StringNarrationWorkOrderSnapshot> WorkOrders { get; set; } = [];
    public IReadOnlyList<StringNarrationStatusLog> StatusLogs { get; set; } = [];

    public string IdText => BuildValue(Id, "无 _id");
    public string OwnerOpenidMaskedText => MaskOpenid(OwnerOpenid);
    public string RemarkText => BuildValue(Remark, "无用户备注");
    public string PricingSnapshotText => FormatJson(PricingSnapshot, "无 pricingSnapshot");
    public string DesignSnapshotText => FormatJson(DesignSnapshot, "无 designSnapshot");
    public string StorySnapshotText => FormatJson(StorySnapshot, "无 storySnapshot");
    public string ItemsSnapshotStateText => ItemsSnapshot.Count == 0 ? "无 itemsSnapshot / 旧订单未记录" : $"{ItemsSnapshot.Count} 条商品快照";
    public string ProductionOrderSummaryText => ProductionOrder.SummaryText;
    public string WorkOrdersSummaryText => WorkOrders.Count == 0 ? "暂无制作单" : $"{WorkOrders.Count} 条制作单";

    private static string MaskOpenid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "无 ownerOpenid";
        }

        var normalized = value.Trim();
        return normalized.Length <= 10
            ? $"{normalized[..Math.Min(3, normalized.Length)]}***"
            : $"{normalized[..6]}***{normalized[^4..]}";
    }
}

public sealed class StringNarrationOrderItemSnapshot
{
    private static readonly string[] SpecificationFieldNames =
    [
        "size",
        "craft",
        "spec",
        "specs",
        "specification",
        "options",
        "attributes",
        "skuText"
    ];

    private static readonly string[] QuantityFieldNames =
    [
        "quantity",
        "count"
    ];

    public string DesignId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Cover { get; set; } = string.Empty;
    public int Count { get; set; }
    public JsonElement? Raw { get; set; }

    public string TitleText => string.IsNullOrWhiteSpace(Title) ? "无商品标题" : Title.Trim();
    public string CoverText => string.IsNullOrWhiteSpace(Cover) ? "无封面" : Cover.Trim();
    public string DesignIdText => string.IsNullOrWhiteSpace(DesignId) ? "无 designId" : DesignId.Trim();
    public string CountText => $"{Count} 件";
    public string SpecificationText => BuildSpecificationText();
    public string SpecSummaryText => SpecificationText;
    public string RawText => Raw is null ? "无原始商品快照" : JsonSerializer.Serialize(Raw.Value, new JsonSerializerOptions { WriteIndented = true });

    private string BuildSpecificationText()
    {
        var parts = new List<string>();
        if (Raw is JsonElement raw && raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var fieldName in SpecificationFieldNames)
            {
                if (!TryGetPropertyIgnoreCase(raw, fieldName, out var value))
                {
                    continue;
                }

                AddTokensFromValue(parts, fieldName, value);
            }
        }

        var stableFallback = $"{DesignIdText} / {ResolveCountText()}";
        if (parts.Count == 0)
        {
            return stableFallback;
        }

        var distinctParts = DistinctPreserveOrder(parts);
        return distinctParts.Count == 0
            ? stableFallback
            : $"{string.Join(" / ", distinctParts)} / {ResolveCountText()}";
    }

    private string ResolveCountText()
    {
        if (Raw is JsonElement raw && raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in QuantityFieldNames)
            {
                if (!TryGetPropertyIgnoreCase(raw, name, out var value))
                {
                    continue;
                }

                var parsed = ParsePositiveInt(value);
                if (parsed > 0)
                {
                    return $"{parsed} 件";
                }
            }
        }

        return CountText;
    }

    private static void AddTokensFromValue(List<string> parts, string fieldName, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                AddToken(parts, NormalizeScalar(value));
                return;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AddTokensFromValue(parts, fieldName, item);
                }

                return;
            case JsonValueKind.Object:
                AddTokensFromObject(parts, fieldName, value);
                return;
            default:
                return;
        }
    }

    private static void AddTokensFromObject(List<string> parts, string fieldName, JsonElement value)
    {
        if (TryBuildNamedOption(value, out var namedOption))
        {
            AddToken(parts, namedOption);
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            var scalar = NormalizeScalar(property.Value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                AddToken(parts, $"{property.Name}:{scalar}");
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            {
                AddTokensFromValue(parts, fieldName, property.Value);
            }
        }
    }

    private static bool TryBuildNamedOption(JsonElement value, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var name = ReadScalarProperty(value, "name", "label", "key", "title");
        var optionValue = ReadScalarProperty(value, "value", "text", "val", "option");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(optionValue))
        {
            return false;
        }

        text = $"{name}:{optionValue}";
        return true;
    }

    private static string ReadScalarProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                continue;
            }

            var scalar = NormalizeScalar(value);
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                return scalar;
            }
        }

        return string.Empty;
    }

    private static int ParsePositiveInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedByNumber))
        {
            return parsedByNumber > 0 ? parsedByNumber : 0;
        }

        var scalar = NormalizeScalar(value);
        if (int.TryParse(scalar, out var parsedByString) && parsedByString > 0)
        {
            return parsedByString;
        }

        return 0;
    }

    private static string NormalizeScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void AddToken(List<string> parts, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        parts.Add(token.Trim());
    }

    private static IReadOnlyList<string> DistinctPreserveOrder(IEnumerable<string> values)
    {
        var result = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (!set.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }
}

public sealed class StringNarrationAddressSnapshot
{
    public string ReceiverName { get; set; } = string.Empty;
    public string ReceiverPhone { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string FullAddress { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;

    public string ReceiverSummaryText
    {
        get
        {
            var parts = new[] { ReceiverName, ReceiverPhone }
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim());
            var value = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(value) ? "无收件人" : value;
        }
    }

    public string FullAddressText
    {
        get
        {
            var explicitAddress = FirstNonEmpty(FullAddress, AddressSummary);
            if (!string.IsNullOrWhiteSpace(explicitAddress))
            {
                return explicitAddress;
            }

            var composed = string.Join(" ", new[] { Region, Detail }
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));
            return string.IsNullOrWhiteSpace(composed) ? "无地址" : composed;
        }
    }

    public string ReceiverCopyText => string.Join(Environment.NewLine, new[] { ReceiverSummaryText, FullAddressText }
        .Where(item => !string.IsNullOrWhiteSpace(item) && item != "无收件人" && item != "无地址"));

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))?.Trim() ?? string.Empty;
    }
}

public sealed class StringNarrationProductionOrderSnapshot
{
    public string ProductionOrderId { get; set; } = string.Empty;
    public string ProductionOrderNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public IReadOnlyList<StringNarrationWorkOrderSnapshot> WorkOrders { get; set; } = [];
    public JsonElement? Raw { get; set; }

    public bool HasData =>
        !string.IsNullOrWhiteSpace(ProductionOrderId)
        || !string.IsNullOrWhiteSpace(ProductionOrderNo)
        || !string.IsNullOrWhiteSpace(Status)
        || WorkOrders.Count > 0
        || Raw is not null;

    public string ProductionOrderNoText => BuildValue(ProductionOrderNo, "无制作单号");
    public string StatusText => BuildValue(Status, "未知制作单状态");
    public string SourceText => BuildValue(Source, "未知来源");
    public string RemarkText => BuildValue(Remark, "无制作备注");
    public string CreatedAtText => FormatGatewayTime(CreatedAt);
    public string UpdatedAtText => FormatGatewayTime(UpdatedAt);
    public string SummaryText => HasData
        ? $"{ProductionOrderNoText} / {StatusText} / {WorkOrders.Count} 条工单"
        : "暂无制作单";

    private static string BuildValue(string? value, string fallback)
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
}

public sealed class StringNarrationWorkOrderSnapshot
{
    public string WorkOrderId { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProductionOrderNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public JsonElement? Raw { get; set; }

    public string WorkOrderNoText => BuildValue(WorkOrderNo, "无工单号");
    public string StatusText => BuildValue(Status, "未知工单状态");
    public string AssigneeText => BuildValue(Assignee, "未分配");
    public string RemarkText => BuildValue(Remark, "无备注");
    public string CreatedAtText => FormatGatewayTime(CreatedAt);
    public string UpdatedAtText => FormatGatewayTime(UpdatedAt);

    private static string BuildValue(string? value, string fallback)
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
}

public sealed class StringNarrationFulfillmentUpdateRequest
{
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string TradeNo { get; set; } = string.Empty;
    public string FulfillmentStatus { get; set; } = string.Empty;
    public string TrackingNo { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string ExpressCompanyCode { get; set; } = string.Empty;
    public string ShippingRemark { get; set; } = string.Empty;
    public string AdminRemark { get; set; } = string.Empty;
}

public sealed class StringNarrationGenerateProductionOrderRequest
{
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string TradeNo { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public bool ForceRegenerate { get; set; }
}

public sealed class StringNarrationStatusLog
{
    public string Type { get; set; } = string.Empty;
    public long At { get; set; }
    public string Source { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorOpenid { get; set; } = string.Empty;
    public JsonElement? Changes { get; set; }

    public string AtText => FormatGatewayTime(At);
    public string TypeText => string.IsNullOrWhiteSpace(Type) ? "未知日志类型" : Type.Trim();
    public string SourceText => string.IsNullOrWhiteSpace(Source) ? "无 source" : Source.Trim();
    public string OperatorIdText => string.IsNullOrWhiteSpace(OperatorId) ? "无 operatorId" : OperatorId.Trim();
    public string ChangesText => Changes is null
        ? "无 changes"
        : JsonSerializer.Serialize(Changes.Value, new JsonSerializerOptions { WriteIndented = true });

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
}
