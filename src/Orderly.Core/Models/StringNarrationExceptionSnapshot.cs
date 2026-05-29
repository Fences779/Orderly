using System.Text.Json;

namespace Orderly.Core.Models;

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
    public IReadOnlyList<StringNarrationExceptionAuditEntry> AuditLogs { get; set; } = [];
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
    public string SeverityCategory
    {
        get
        {
            return EffectiveCode switch
            {
                StringNarrationExceptionFieldCatalog.CodeShippingSyncFailure => "Money",
                StringNarrationExceptionFieldCatalog.CodeMissingAddress or
                StringNarrationExceptionFieldCatalog.CodeMissingReceiverPhone or
                StringNarrationExceptionFieldCatalog.CodeMissingTrackingNo => "Warning",
                _ => "General"
            };
        }
    }
    public int SeveritySortOrder
    {
        get
        {
            return SeverityCategory switch
            {
                "Money" => 0,
                "Warning" => 1,
                "General" => 2,
                _ => 3
            };
        }
    }
    public string EffectiveType => BuildValue(Type, EffectiveCode);
    public string EffectiveReason => BuildValue(Reason, StringNarrationExceptionFieldCatalog.GetExceptionReason(EffectiveCode));
    public string ExceptionCategoryLabel => StringNarrationExceptionFieldCatalog.GetExceptionLabel(EffectiveCode);
    public string LevelText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Level))
            {
                return Level.Trim();
            }

            if (!HasException)
            {
                return "无";
            }

            return SeverityCategory switch
            {
                "Money" => "资金阻断",
                "Warning" => "流程阻断",
                "General" => "一般异常",
                _ => "待判级"
            };
        }
    }
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
