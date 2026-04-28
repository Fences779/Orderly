namespace Orderly.Core.Models;

public sealed class ActivityLog : EntityBase
{
    public ActivityType Type { get; set; }
    public int? CustomerId { get; set; }
    public int? DealId { get; set; }
    public int? OrderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;

    public string TypeLabel => Type switch
    {
        ActivityType.CustomerCreated => "客户创建",
        ActivityType.CustomerUpdated => "客户更新",
        ActivityType.CustomerStatusChanged => "客户状态变更",
        ActivityType.DealCreated => "成交机会创建",
        ActivityType.DealStageChanged => "成交阶段变更",
        ActivityType.FollowUpCreated => "新增跟进",
        ActivityType.FollowUpCompleted => "跟进完成",
        ActivityType.OrderCreated => "订单创建",
        ActivityType.OrderStatusChanged => "订单状态变更",
        ActivityType.NoteCreated => "新增备注",
        ActivityType.PriceAdjustmentRequested => "发起改价",
        ActivityType.PriceAdjustmentApproved => "改价通过",
        ActivityType.PriceAdjustmentRejected => "改价驳回",
        ActivityType.SyncCompleted => "同步完成",
        ActivityType.FollowUpSnoozed => "跟进延期",
        ActivityType.FollowUpCancelled => "跟进取消",
        ActivityType.ConversationMessageAdded => "新增会话消息",
        ActivityType.AiSuggestionGenerated => "生成 AI 建议",
        ActivityType.AiSuggestionAccepted => "接受 AI 建议",
        ActivityType.AiSuggestionRejected => "拒绝 AI 建议",
        ActivityType.OcrTaskCreated => "创建 OCR 任务",
        ActivityType.OcrTaskCompleted => "OCR 完成",
        ActivityType.OcrTaskFailed => "OCR 失败",
        ActivityType.AutoReplyDraftPrepared => "生成回复草稿",
        ActivityType.SyncFailed => "同步失败",
        ActivityType.AutoReplySent => "标记回复已发送",
        ActivityType.AutoReplyDraftRejected => "拒绝回复草稿",
        _ => Type.ToString()
    };
}
