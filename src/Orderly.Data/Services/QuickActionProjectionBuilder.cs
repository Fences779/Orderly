using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class QuickActionProjectionBuilder
{
    public static IReadOnlyList<QuickAction> BuildForWorkbenchTask(WorkbenchTask task)
    {
        var actions = new List<QuickAction>();
        actions.Add(CreateNavigationAction(
            task,
            QuickActionType.OpenCustomer,
            "打开客户",
            task.CustomerId is > 0,
            "当前任务未关联客户"));
        actions.Add(CreateNavigationAction(
            task,
            QuickActionType.OpenOrder,
            "打开订单",
            task.OrderId is > 0,
            "当前任务未关联订单"));

        switch (task.Type)
        {
            case WorkbenchTaskType.DraftNotSent:
                actions.Add(CreateTaskAction(task, QuickActionType.ReviewDraft, "查看草稿", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                actions.Add(CreateTaskAction(task, QuickActionType.CopyDraft, "复制草稿", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                actions.Add(CreateTaskAction(task, QuickActionType.MarkSent, "标记已发送", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                break;
            case WorkbenchTaskType.AiSuggestionPending:
                actions.Add(CreateTaskAction(task, QuickActionType.ReviewSuggestion, "查看建议", task.AiSuggestionId is > 0, "当前任务未关联 AI 建议"));
                break;
            case WorkbenchTaskType.OcrNotConverted:
                actions.Add(CreateTaskAction(task, QuickActionType.ConvertOcrToMessage, "转为消息", task.OcrResultId is > 0, "当前任务未关联 OCR 结果"));
                break;
            case WorkbenchTaskType.FollowUpToday:
            case WorkbenchTaskType.FollowUpOverdue:
                actions.Add(CreateTaskAction(task, QuickActionType.CompleteFollowUp, "完成跟进", task.FollowUpId is > 0, "当前任务未关联跟进"));
                actions.Add(CreateTaskAction(task, QuickActionType.SnoozeFollowUp, "延期跟进", task.FollowUpId is > 0, "当前任务未关联跟进"));
                break;
            case WorkbenchTaskType.ReplyNeeded:
                actions.Add(CreateTaskAction(task, QuickActionType.ReplyToCustomer, "回复客户", task.CustomerId is > 0, "当前任务未关联客户"));
                break;
        }

        return actions;
    }

    public static bool HasActionableOperation(WorkbenchTask task)
    {
        return task.QuickActions.Any(action =>
            action.IsEnabled &&
            action.Type is not QuickActionType.OpenCustomer &&
            action.Type is not QuickActionType.OpenOrder);
    }

    private static QuickAction CreateNavigationAction(WorkbenchTask task, QuickActionType type, string label, bool isEnabled, string disabledReason)
    {
        return new QuickAction
        {
            Type = type,
            Label = label,
            TargetSection = NavigationSemantics.GetTargetSectionValue(NavigationSemantics.GetTargetSection(type)),
            ActionHint = NavigationSemantics.GetActionHintValue(NavigationSemantics.GetActionHint(type)),
            IsEnabled = isEnabled,
            DisabledReason = isEnabled ? string.Empty : disabledReason,
            CustomerId = task.CustomerId,
            OrderId = task.OrderId,
            RelatedEntityType = type == QuickActionType.OpenOrder ? nameof(MerchantOrder) : nameof(Customer),
            RelatedEntityId = type == QuickActionType.OpenOrder ? task.OrderId : task.CustomerId,
            RequiresUserAction = false
        };
    }

    private static QuickAction CreateTaskAction(WorkbenchTask task, QuickActionType type, string label, bool isEnabled, string disabledReason)
    {
        var actionHint = NavigationSemantics.GetActionHint(type);
        var targetSection = NavigationSemantics.GetTargetSection(type);
        var relatedEntity = ResolveRelatedEntity(task, type);

        return new QuickAction
        {
            Type = type,
            Label = label,
            TargetSection = NavigationSemantics.GetTargetSectionValue(targetSection),
            ActionHint = NavigationSemantics.GetActionHintValue(actionHint),
            IsEnabled = isEnabled,
            DisabledReason = isEnabled ? string.Empty : disabledReason,
            CustomerId = task.CustomerId,
            OrderId = task.OrderId,
            RelatedEntityType = relatedEntity.RelatedEntityType,
            RelatedEntityId = relatedEntity.RelatedEntityId,
            RequiresUserAction = NavigationSemantics.IsHighRiskAction(actionHint)
        };
    }

    private static (string RelatedEntityType, int? RelatedEntityId) ResolveRelatedEntity(WorkbenchTask task, QuickActionType type)
    {
        return type switch
        {
            QuickActionType.ReviewSuggestion or QuickActionType.ReviewDraft or QuickActionType.CopyDraft or QuickActionType.MarkSent
                => (nameof(AiSuggestion), task.AiSuggestionId ?? task.RelatedEntityId),
            QuickActionType.ConvertOcrToMessage
                => (nameof(OcrResult), task.OcrResultId ?? task.RelatedEntityId),
            QuickActionType.CompleteFollowUp or QuickActionType.SnoozeFollowUp
                => (nameof(FollowUp), task.FollowUpId ?? task.RelatedEntityId),
            QuickActionType.ReplyToCustomer
                => (nameof(ConversationMessage), task.MessageId ?? task.RelatedEntityId),
            _ => (task.RelatedEntityType, task.RelatedEntityId)
        };
    }
}
