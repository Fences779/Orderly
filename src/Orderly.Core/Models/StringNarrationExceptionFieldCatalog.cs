namespace Orderly.Core.Models;

public static class StringNarrationExceptionFieldCatalog
{
    public const string PriorityLow = "low";
    public const string PriorityNormal = "normal";
    public const string PriorityHigh = "high";
    public const string PriorityUrgent = "urgent";

    public const string ResolutionOpen = "open";
    public const string ResolutionProcessing = "processing";
    public const string ResolutionResolved = "resolved";
    public const string ResolutionIgnored = "ignored";

    public const string ActionAssign = "assign";
    public const string ActionStartProcessing = "start_processing";
    public const string ActionResolve = "resolve";
    public const string ActionIgnore = "ignore";
    public const string ActionReopen = "reopen";
    public const string ActionComment = "comment";

    public const string CodeMissingAddress = "missing_address";
    public const string CodeMissingReceiverPhone = "missing_receiver_phone";
    public const string CodeMissingTrackingNo = "missing_tracking_no";
    public const string CodeShippingSyncFailure = "shipping_sync_failure";
    public const string CodeProductionOrderMissing = "production_order_missing";
    public const string CodeWorkOrderMissing = "work_order_missing";
    public const string CodeFulfillmentException = "fulfillment_exception";
    public const string CodeUnknownException = "unknown_exception";

    private static readonly Dictionary<string, string> PriorityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [PriorityLow] = PriorityLow,
        ["minor"] = PriorityLow,
        ["低"] = PriorityLow,
        ["低优先级"] = PriorityLow,
        [PriorityNormal] = PriorityNormal,
        ["medium"] = PriorityNormal,
        ["中"] = PriorityNormal,
        ["普通"] = PriorityNormal,
        [PriorityHigh] = PriorityHigh,
        ["major"] = PriorityHigh,
        ["高"] = PriorityHigh,
        ["高优先级"] = PriorityHigh,
        [PriorityUrgent] = PriorityUrgent,
        ["critical"] = PriorityUrgent,
        ["blocker"] = PriorityUrgent,
        ["紧急"] = PriorityUrgent,
        ["严重"] = PriorityUrgent
    };

    private static readonly Dictionary<string, string> ResolutionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [ResolutionOpen] = ResolutionOpen,
        ["new"] = ResolutionOpen,
        ["pending"] = ResolutionOpen,
        ["todo"] = ResolutionOpen,
        ["待处理"] = ResolutionOpen,
        [ResolutionProcessing] = ResolutionProcessing,
        ["in_progress"] = ResolutionProcessing,
        ["handling"] = ResolutionProcessing,
        ["处理中"] = ResolutionProcessing,
        [ResolutionResolved] = ResolutionResolved,
        ["closed"] = ResolutionResolved,
        ["fixed"] = ResolutionResolved,
        ["done"] = ResolutionResolved,
        ["cleared"] = ResolutionResolved,
        ["已解决"] = ResolutionResolved,
        [ResolutionIgnored] = ResolutionIgnored,
        ["dismissed"] = ResolutionIgnored,
        ["false_positive"] = ResolutionIgnored,
        ["无需处理"] = ResolutionIgnored
    };

    private static readonly Dictionary<string, string> ExceptionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        [CodeMissingAddress] = "缺地址",
        [CodeMissingReceiverPhone] = "缺手机号",
        [CodeMissingTrackingNo] = "缺物流单号",
        [CodeShippingSyncFailure] = "微信发货同步失败",
        [CodeProductionOrderMissing] = "缺制作单",
        [CodeWorkOrderMissing] = "缺工单",
        [CodeFulfillmentException] = "履约异常",
        [CodeUnknownException] = "异常待确认"
    };

    private static readonly Dictionary<string, string> ExceptionReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        [CodeMissingAddress] = "收件地址缺失或不完整",
        [CodeMissingReceiverPhone] = "收件人手机号缺失",
        [CodeMissingTrackingNo] = "物流单号缺失",
        [CodeShippingSyncFailure] = "微信发货同步失败",
        [CodeProductionOrderMissing] = "制作单缺失",
        [CodeWorkOrderMissing] = "工单缺失",
        [CodeFulfillmentException] = "履约链路存在异常",
        [CodeUnknownException] = "异常原因待补充"
    };

    public static string NormalizePriority(string? priority)
    {
        var token = NormalizeAliasToken(priority);
        return PriorityAliases.TryGetValue(token, out var normalized) ? normalized : string.Empty;
    }

    public static int GetPrioritySortOrder(string? priority)
    {
        return NormalizePriority(priority) switch
        {
            PriorityUrgent => 0,
            PriorityHigh => 1,
            PriorityNormal => 2,
            PriorityLow => 3,
            _ => 4
        };
    }

    public static string GetPriorityLabel(string? priority)
    {
        return NormalizePriority(priority) switch
        {
            PriorityUrgent => "紧急",
            PriorityHigh => "高",
            PriorityNormal => "普通",
            PriorityLow => "低",
            _ => "未分级"
        };
    }

    public static string NormalizeResolutionStatus(string? resolutionStatus)
    {
        var token = NormalizeAliasToken(resolutionStatus);
        return ResolutionAliases.TryGetValue(token, out var normalized) ? normalized : string.Empty;
    }

    public static int GetResolutionStatusSortOrder(string? resolutionStatus)
    {
        return NormalizeResolutionStatus(resolutionStatus) switch
        {
            ResolutionOpen => 0,
            ResolutionProcessing => 1,
            ResolutionIgnored => 2,
            ResolutionResolved => 3,
            _ => 4
        };
    }

    public static string GetResolutionStatusLabel(string? resolutionStatus)
    {
        return NormalizeResolutionStatus(resolutionStatus) switch
        {
            ResolutionOpen => "待处理",
            ResolutionProcessing => "处理中",
            ResolutionResolved => "已解决",
            ResolutionIgnored => "已忽略",
            _ => "未标注"
        };
    }

    public static bool IsResolvedState(string? resolutionStatus)
    {
        return string.Equals(
            NormalizeResolutionStatus(resolutionStatus),
            ResolutionResolved,
            StringComparison.Ordinal);
    }

    public static string NormalizeAction(string? action)
    {
        var token = NormalizeAliasToken(action);
        return token switch
        {
            "assign" or "claim" or "认领" or "分配" => ActionAssign,
            "start_processing" or "processing" or "handle" or "handling" or "处理中" => ActionStartProcessing,
            "resolve" or "resolved" or "close" or "fixed" or "解决" or "已解决" => ActionResolve,
            "ignore" or "ignored" or "dismiss" or "dismissed" or "无需处理" => ActionIgnore,
            "reopen" or "open" or "重新打开" => ActionReopen,
            "comment" or "remark" or "note" or "备注" => ActionComment,
            _ => token
        };
    }

    public static string GetActionTargetStatus(string? action, string? fallbackStatus)
    {
        return NormalizeAction(action) switch
        {
            ActionStartProcessing => ResolutionProcessing,
            ActionResolve => ResolutionResolved,
            ActionIgnore => ResolutionIgnored,
            ActionReopen => ResolutionOpen,
            _ => NormalizeResolutionStatus(fallbackStatus)
        };
    }

    public static string InferExceptionCode(
        bool hasException,
        bool hasMissingAddress,
        bool hasMissingReceiverPhone,
        bool hasMissingTrackingNo,
        bool hasShippingSyncFailure,
        bool hasProductionOrderMissing,
        bool hasWorkOrderMissing,
        string? fulfillmentStatus)
    {
        if (hasMissingAddress)
        {
            return CodeMissingAddress;
        }

        if (hasMissingReceiverPhone)
        {
            return CodeMissingReceiverPhone;
        }

        if (hasMissingTrackingNo)
        {
            return CodeMissingTrackingNo;
        }

        if (hasShippingSyncFailure)
        {
            return CodeShippingSyncFailure;
        }

        if (hasProductionOrderMissing)
        {
            return CodeProductionOrderMissing;
        }

        if (hasWorkOrderMissing)
        {
            return CodeWorkOrderMissing;
        }

        if (string.Equals(
            StringNarrationFulfillmentStatusCatalog.Normalize(fulfillmentStatus),
            StringNarrationFulfillmentStatusCatalog.Exception,
            StringComparison.OrdinalIgnoreCase))
        {
            return CodeFulfillmentException;
        }

        return hasException ? CodeUnknownException : string.Empty;
    }

    public static string GetExceptionLabel(string? codeOrType)
    {
        var normalized = NormalizeAliasToken(codeOrType);
        return ExceptionLabels.TryGetValue(normalized, out var label)
            ? label
            : ExceptionLabels[CodeUnknownException];
    }

    public static string GetExceptionReason(string? codeOrType)
    {
        var normalized = NormalizeAliasToken(codeOrType);
        return ExceptionReasons.TryGetValue(normalized, out var reason)
            ? reason
            : ExceptionReasons[CodeUnknownException];
    }

    private static string NormalizeAliasToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }
}
