using Orderly.Core.Models;
using Orderly.App.ViewModels.Helpers;

namespace Orderly.App.ViewModels;

public sealed class WorkbenchTaskListItem
{
    public WorkbenchTaskListItem(WorkbenchTask task)
    {
        Task = task;
    }

    public WorkbenchTask Task { get; }
    public string Id => Task.Id;
    public WorkbenchTaskType Type => Task.Type;
    public WorkbenchTaskPriority Priority => Task.Priority;
    public int? CustomerId => Task.CustomerId;
    public int? OrderId => Task.OrderId;
    public string Title => Task.Title;
    public string Summary => Task.Summary;
    public string CustomerNameDisplay => string.IsNullOrWhiteSpace(Task.CustomerName) ? "未定位客户" : Task.CustomerName;
    public string OrderDisplay => string.IsNullOrWhiteSpace(Task.OrderDisplay) ? "未关联订单" : Task.OrderDisplay;
    public string TypeText => GetTypeText();
    public string PriorityText => GetPriorityText();
    public string OccurredAtText => Task.OccurredAt.ToString("MM-dd HH:mm");
    public string RelatedContextText => Task.OrderId is > 0 ? $"{CustomerNameDisplay} / {OrderDisplay}" : CustomerNameDisplay;
    public bool HasPipelineStage => Task.PipelineStage is not null;
    public string PipelineStageText => Task.PipelineStage is PipelineStage stage ? StatusLabelHelper.GetPipelineStageLabel(stage) : "未推导";

    private string GetTypeText()
    {
        return Task.Type switch
        {
            WorkbenchTaskType.ReplyNeeded => "待回复",
            WorkbenchTaskType.DraftNotSent => "草稿未发送",
            WorkbenchTaskType.AiSuggestionPending => "AI 待处理",
            WorkbenchTaskType.OcrNotConverted => "OCR 待转消息",
            WorkbenchTaskType.FollowUpToday => "今日跟进",
            WorkbenchTaskType.FollowUpOverdue => "逾期跟进",
            WorkbenchTaskType.RecentlyActiveCustomer => "最近活跃",
            _ => Task.Type.ToString()
        };
    }

    private string GetPriorityText()
    {
        return Task.Priority switch
        {
            WorkbenchTaskPriority.Critical => "高优先级",
            WorkbenchTaskPriority.High => "优先处理",
            WorkbenchTaskPriority.Medium => "待处理",
            WorkbenchTaskPriority.Low => "可关注",
            _ => Task.Priority.ToString()
        };
    }
}
