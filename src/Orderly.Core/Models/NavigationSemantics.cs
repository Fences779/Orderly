namespace Orderly.Core.Models;

public static class NavigationSemantics
{
    public static string GetTargetSectionValue(NavigationTargetSection section)
    {
        return section switch
        {
            NavigationTargetSection.Customer => "Customer",
            NavigationTargetSection.Order => "Order",
            NavigationTargetSection.Conversation => "Conversation",
            NavigationTargetSection.AiSuggestion => "AiSuggestion",
            NavigationTargetSection.Ocr => "Ocr",
            NavigationTargetSection.FollowUp => "FollowUp",
            NavigationTargetSection.ActivityLog => "ActivityLog",
            _ => string.Empty
        };
    }

    public static string GetActionHintValue(NavigationActionHint hint)
    {
        return hint switch
        {
            NavigationActionHint.OpenCustomer => "OpenCustomer",
            NavigationActionHint.OpenOrder => "OpenOrder",
            NavigationActionHint.ReplyToCustomer => "ReplyToCustomer",
            NavigationActionHint.ReviewSuggestion => "ReviewSuggestion",
            NavigationActionHint.ReviewDraft => "ReviewDraft",
            NavigationActionHint.CopyDraft => "CopyDraft",
            NavigationActionHint.MarkSent => "MarkSent",
            NavigationActionHint.ConvertOcrToMessage => "ConvertOcrToMessage",
            NavigationActionHint.CompleteFollowUp => "CompleteFollowUp",
            NavigationActionHint.SnoozeFollowUp => "SnoozeFollowUp",
            _ => string.Empty
        };
    }

    public static bool TryParseTargetSection(string? value, out NavigationTargetSection section)
    {
        section = NavigationTargetSection.Unknown;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        if (normalized.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.Customer;
            return true;
        }

        if (normalized.Equals("Order", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.Order;
            return true;
        }

        if (normalized.Equals("Conversation", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.Conversation;
            return true;
        }

        if (normalized.Equals("AiSuggestion", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.AiSuggestion;
            return true;
        }

        if (normalized.Equals("Ocr", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.Ocr;
            return true;
        }

        if (normalized.Equals("FollowUp", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.FollowUp;
            return true;
        }

        if (normalized.Equals("ActivityLog", StringComparison.OrdinalIgnoreCase))
        {
            section = NavigationTargetSection.ActivityLog;
            return true;
        }

        return false;
    }

    public static bool TryParseActionHint(string? value, out NavigationActionHint hint)
    {
        hint = NavigationActionHint.None;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        if (normalized.Equals("OpenCustomer", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.OpenCustomer;
            return true;
        }

        if (normalized.Equals("OpenOrder", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.OpenOrder;
            return true;
        }

        if (normalized.Equals("ReplyToCustomer", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.ReplyToCustomer;
            return true;
        }

        if (normalized.Equals("ReviewSuggestion", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.ReviewSuggestion;
            return true;
        }

        if (normalized.Equals("ReviewDraft", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.ReviewDraft;
            return true;
        }

        if (normalized.Equals("CopyDraft", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.CopyDraft;
            return true;
        }

        if (normalized.Equals("MarkSent", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.MarkSent;
            return true;
        }

        if (normalized.Equals("ConvertOcrToMessage", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ConvertOcr", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.ConvertOcrToMessage;
            return true;
        }

        if (normalized.Equals("CompleteFollowUp", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.CompleteFollowUp;
            return true;
        }

        if (normalized.Equals("SnoozeFollowUp", StringComparison.OrdinalIgnoreCase))
        {
            hint = NavigationActionHint.SnoozeFollowUp;
            return true;
        }

        return false;
    }

    public static NavigationTargetSection GetDefaultTargetSection(NavigationActionHint hint)
    {
        return hint switch
        {
            NavigationActionHint.OpenCustomer => NavigationTargetSection.Customer,
            NavigationActionHint.OpenOrder => NavigationTargetSection.Order,
            NavigationActionHint.ReplyToCustomer => NavigationTargetSection.Conversation,
            NavigationActionHint.ReviewSuggestion or NavigationActionHint.ReviewDraft or NavigationActionHint.CopyDraft or NavigationActionHint.MarkSent
                => NavigationTargetSection.AiSuggestion,
            NavigationActionHint.ConvertOcrToMessage => NavigationTargetSection.Ocr,
            NavigationActionHint.CompleteFollowUp or NavigationActionHint.SnoozeFollowUp => NavigationTargetSection.FollowUp,
            _ => NavigationTargetSection.Unknown
        };
    }

    public static NavigationActionHint GetDefaultActionHint(NavigationTargetSection section, int? customerId = null, int? orderId = null)
    {
        return section switch
        {
            NavigationTargetSection.Customer when customerId is > 0 => NavigationActionHint.OpenCustomer,
            NavigationTargetSection.Order when orderId is > 0 => NavigationActionHint.OpenOrder,
            NavigationTargetSection.Conversation => NavigationActionHint.ReplyToCustomer,
            NavigationTargetSection.AiSuggestion => NavigationActionHint.ReviewSuggestion,
            NavigationTargetSection.Ocr => NavigationActionHint.ConvertOcrToMessage,
            NavigationTargetSection.FollowUp => NavigationActionHint.CompleteFollowUp,
            NavigationTargetSection.ActivityLog when orderId is > 0 => NavigationActionHint.OpenOrder,
            NavigationTargetSection.ActivityLog when customerId is > 0 => NavigationActionHint.OpenCustomer,
            _ => NavigationActionHint.None
        };
    }

    public static NavigationActionHint GetActionHint(QuickActionType type)
    {
        return type switch
        {
            QuickActionType.OpenCustomer => NavigationActionHint.OpenCustomer,
            QuickActionType.OpenOrder => NavigationActionHint.OpenOrder,
            QuickActionType.ReplyToCustomer => NavigationActionHint.ReplyToCustomer,
            QuickActionType.ReviewSuggestion => NavigationActionHint.ReviewSuggestion,
            QuickActionType.ReviewDraft => NavigationActionHint.ReviewDraft,
            QuickActionType.CopyDraft => NavigationActionHint.CopyDraft,
            QuickActionType.MarkSent => NavigationActionHint.MarkSent,
            QuickActionType.ConvertOcrToMessage => NavigationActionHint.ConvertOcrToMessage,
            QuickActionType.CompleteFollowUp => NavigationActionHint.CompleteFollowUp,
            QuickActionType.SnoozeFollowUp => NavigationActionHint.SnoozeFollowUp,
            _ => NavigationActionHint.None
        };
    }

    public static NavigationTargetSection GetTargetSection(QuickActionType type)
    {
        return GetDefaultTargetSection(GetActionHint(type));
    }

    public static bool IsHighRiskAction(NavigationActionHint hint)
    {
        return hint is NavigationActionHint.CopyDraft
            or NavigationActionHint.MarkSent
            or NavigationActionHint.ConvertOcrToMessage
            or NavigationActionHint.CompleteFollowUp
            or NavigationActionHint.SnoozeFollowUp;
    }
}
