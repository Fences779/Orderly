using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class QuickActionProjectionBuilder
{
    public static IReadOnlyList<QuickAction> BuildForWorkbenchTask(WorkbenchTask task)
    {
        var actions = new List<QuickAction>();
        actions.Add(CreateNavigationAction(
            QuickActionType.OpenCustomer,
            "打开客户",
            task.CustomerId is > 0,
            "当前任务未关联客户"));
        actions.Add(CreateNavigationAction(
            QuickActionType.OpenOrder,
            "打开订单",
            task.OrderId is > 0,
            "当前任务未关联订单"));

        switch (task.Type)
        {
            case WorkbenchTaskType.DraftNotSent:
                actions.Add(CreateTaskAction(QuickActionType.ReviewDraft, "查看草稿", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                actions.Add(CreateTaskAction(QuickActionType.CopyDraft, "复制草稿", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                actions.Add(CreateTaskAction(QuickActionType.MarkSent, "标记已发送", task.AiSuggestionId is > 0, "当前任务未关联草稿"));
                break;
            case WorkbenchTaskType.AiSuggestionPending:
                actions.Add(CreateTaskAction(QuickActionType.ReviewSuggestion, "查看建议", task.AiSuggestionId is > 0, "当前任务未关联 AI 建议"));
                break;
            case WorkbenchTaskType.OcrNotConverted:
                actions.Add(CreateTaskAction(QuickActionType.ConvertOcrToMessage, "转为消息", task.OcrResultId is > 0, "当前任务未关联 OCR 结果"));
                break;
            case WorkbenchTaskType.FollowUpToday:
            case WorkbenchTaskType.FollowUpOverdue:
                actions.Add(CreateTaskAction(QuickActionType.CompleteFollowUp, "完成跟进", task.FollowUpId is > 0, "当前任务未关联跟进"));
                actions.Add(CreateTaskAction(QuickActionType.SnoozeFollowUp, "延期跟进", task.FollowUpId is > 0, "当前任务未关联跟进"));
                break;
            case WorkbenchTaskType.ReplyNeeded:
                actions.Add(CreateTaskAction(QuickActionType.ReplyToCustomer, "回复客户", task.CustomerId is > 0, "当前任务未关联客户"));
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

    private static QuickAction CreateNavigationAction(QuickActionType type, string label, bool isEnabled, string disabledReason)
    {
        return new QuickAction
        {
            Type = type,
            Label = label,
            IsEnabled = isEnabled,
            DisabledReason = isEnabled ? string.Empty : disabledReason
        };
    }

    private static QuickAction CreateTaskAction(QuickActionType type, string label, bool isEnabled, string disabledReason)
    {
        return new QuickAction
        {
            Type = type,
            Label = label,
            IsEnabled = isEnabled,
            DisabledReason = isEnabled ? string.Empty : disabledReason
        };
    }
}
