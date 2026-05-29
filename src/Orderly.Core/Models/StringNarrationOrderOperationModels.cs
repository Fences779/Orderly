using System.Text.Json;

namespace Orderly.Core.Models;

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

public sealed class StringNarrationExceptionActionRequest
{
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string TradeNo { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = string.Empty;
    public string ResolutionAction { get; set; } = string.Empty;
    public string AdminResolutionRemark { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string ResolvedBy { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorOpenid { get; set; } = string.Empty;
    public long SlaDueAt { get; set; }
    public long LastCheckedAt { get; set; }
    public long ActionAt { get; set; }

    public string NormalizedAction => StringNarrationExceptionFieldCatalog.NormalizeAction(Action);
    public string TargetResolutionStatus => StringNarrationExceptionFieldCatalog.GetActionTargetStatus(Action, ResolutionStatus);
}

public sealed class StringNarrationExceptionActionResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public StringNarrationOrderDetail Detail { get; set; } = new();
    public StringNarrationExceptionAuditEntry AuditEntry { get; set; } = new();
}

public sealed class StringNarrationExceptionSampleReplayRequest
{
    public IReadOnlyList<StringNarrationExceptionSample> Samples { get; set; } = [];
}

public sealed class StringNarrationExceptionSample
{
    public string Name { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class StringNarrationExceptionSampleReplayResult
{
    public IReadOnlyList<StringNarrationExceptionSampleReplayItem> Items { get; set; } = [];
    public int SuccessCount => Items.Count(item => item.Success);
    public int FailureCount => Items.Count(item => !item.Success);
    public string SummaryText => $"{SuccessCount} passed / {FailureCount} failed";
}

public sealed class StringNarrationExceptionSampleReplayItem
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int ParsedOrderCount { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public bool HasException { get; set; }
    public string EffectiveCode { get; set; } = string.Empty;
    public string EffectiveReason { get; set; } = string.Empty;
    public string NormalizedPriority { get; set; } = string.Empty;
    public string NormalizedResolutionStatus { get; set; } = string.Empty;
    public int PrioritySortOrder { get; set; }
    public int ResolutionStatusSortOrder { get; set; }
    public long SlaDueSortTimestamp { get; set; }
    public long DetectedSortTimestamp { get; set; }
    public string SummaryText { get; set; } = string.Empty;
}

public sealed class StringNarrationGenerateProductionOrderRequest
{
    public string Id { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string TradeNo { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public bool ForceRegenerate { get; set; }
}

public sealed class StringNarrationExceptionAuditEntry
{
    public string Action { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorOpenid { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string ResolutionAction { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public long At { get; set; }
    public JsonElement? Raw { get; set; }

    public string NormalizedAction => StringNarrationExceptionFieldCatalog.NormalizeAction(Action);
    public string FromStatusLabel => StringNarrationExceptionFieldCatalog.GetResolutionStatusLabel(FromStatus);
    public string ToStatusLabel => StringNarrationExceptionFieldCatalog.GetResolutionStatusLabel(ToStatus);
    public string PriorityLabel => StringNarrationExceptionFieldCatalog.GetPriorityLabel(Priority);
    public string OperatorText => string.IsNullOrWhiteSpace(OperatorId) ? "未知处理人" : OperatorId.Trim();
    public string AtText => FormatGatewayTime(At);

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
