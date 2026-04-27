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
        _ => Type.ToString()
    };
}
