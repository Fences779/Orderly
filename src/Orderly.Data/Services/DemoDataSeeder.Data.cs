using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class DemoDataSeeder
{
    private sealed record DemoCustomer(
        string ExternalId,
        string RemoteId,
        string Name,
        CustomerPriority Priority,
        string SourcePlatform,
        string Channel,
        string ContactHandle,
        string Phone,
        string Remark,
        int CreatedOffsetDays,
        int LastContactOffsetHours);

    private sealed record DemoOrder(
        string ExternalId,
        string RemoteId,
        string CustomerExternalId,
        string Title,
        OrderStatus Status,
        decimal Amount,
        string Requirement,
        string SourcePlatform,
        string Channel,
        int CreatedOffsetDays,
        int NextFollowUpOffsetHours);

    private sealed record DemoFollowUp(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        string Title,
        string Content,
        FollowUpStatus Status,
        int CreatedOffsetDays,
        int ScheduledOffsetHours,
        int ReminderOffsetHours);

    private sealed record DemoNote(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        NoteType Type,
        string Content,
        bool IsPinned,
        int CreatedOffsetDays);

    private sealed record DemoPriceAdjustment(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        decimal OriginalAmount,
        decimal AdjustedAmount,
        string Reason,
        PriceAdjustmentStatus Status,
        int CreatedOffsetDays);

    private sealed record DemoActivityLog(
        string RemoteId,
        string CustomerExternalId,
        string OrderExternalId,
        ActivityType Type,
        string Title,
        string Description,
        int CreatedOffsetHours);

    private static readonly DemoCustomer[] DemoCustomers =
    [
        new("demo-customer-001", "demo-customer-001", $"{DemoMarker} 林小姐", CustomerPriority.High, "微信", "私域咨询", "demo_lin", "13800001001", $"{DemoMarker} 偏好自然风格，预算明确，适合演示客户详情。", -12, -3),
        new("demo-customer-002", "demo-customer-002", $"{DemoMarker} 陈先生", CustomerPriority.Normal, "闲鱼", "二手平台", "demo_chen", "13800001002", $"{DemoMarker} 关注交付周期和售后说明，适合演示跟进。", -9, -8),
        new("demo-customer-003", "demo-customer-003", $"{DemoMarker} 周老板", CustomerPriority.Critical, "微信", "老客复购", "demo_zhou", "13800001003", $"{DemoMarker} 企业礼品复购客户，适合演示大额订单和改价。", -6, -1)
    ];

    private static readonly DemoOrder[] DemoOrders =
    [
        new("demo-order-001", "demo-order-001", "demo-customer-001", $"{DemoMarker} 婚礼伴手礼定制", OrderStatus.PendingCommunication, 0m, $"{DemoMarker} 需要确认数量、包装和交付日期。", "微信", "私域咨询", -10, 4),
        new("demo-order-002", "demo-order-002", "demo-customer-001", $"{DemoMarker} 家庭纪念照相框", OrderStatus.PendingQuote, 0m, $"{DemoMarker} 客户已发尺寸，待整理基础版和升级版报价。", "微信", "私域咨询", -7, 22),
        new("demo-order-003", "demo-order-003", "demo-customer-002", $"{DemoMarker} 闲鱼摆件修复", OrderStatus.Quoted, 680m, $"{DemoMarker} 已发报价，等待客户确认是否加急。", "闲鱼", "二手平台", -5, 30),
        new("demo-order-004", "demo-order-004", "demo-customer-002", $"{DemoMarker} 旧物翻新加急单", OrderStatus.PendingFollowUp, 1280m, $"{DemoMarker} 客户担心周期，需要补充工期说明。", "闲鱼", "二手平台", -3, 2),
        new("demo-order-005", "demo-order-005", "demo-customer-003", $"{DemoMarker} 企业年会礼盒", OrderStatus.Won, 9600m, $"{DemoMarker} 已收定金，准备排产并确认发票信息。", "微信", "老客复购", -2, 48)
    ];

    private static readonly DemoFollowUp[] DemoFollowUps =
    [
        new("demo-followup-001", "demo-customer-001", "demo-order-001", $"{DemoMarker} 确认婚礼伴手礼数量", $"{DemoMarker} 询问最终数量、包装色系和期望交付日期。", FollowUpStatus.Pending, -2, 3, 2),
        new("demo-followup-002", "demo-customer-002", "demo-order-004", $"{DemoMarker} 补充加急工期说明", $"{DemoMarker} 发送加急排期和额外费用说明。", FollowUpStatus.InProgress, -2, -30, -31),
        new("demo-followup-003", "demo-customer-003", "demo-order-005", $"{DemoMarker} 企业礼盒排产同步", $"{DemoMarker} 同步打样进度，提醒客户确认发票抬头。", FollowUpStatus.Pending, -1, 24, 23)
    ];

    private static readonly DemoNote[] DemoNotes =
    [
        new("demo-note-001", "demo-customer-001", "demo-order-001", NoteType.Preference, $"{DemoMarker} 喜欢低饱和米白色包装，不接受过度花哨设计。", true, -8),
        new("demo-note-002", "demo-customer-002", "demo-order-004", NoteType.Risk, $"{DemoMarker} 对交付时间敏感，报价时必须写清楚加急风险。", false, -4),
        new("demo-note-003", "demo-customer-003", "demo-order-005", NoteType.Requirement, $"{DemoMarker} 企业礼盒需要统一 logo、发票和批量物流单号。", true, -2)
    ];

    private static readonly DemoPriceAdjustment[] DemoPriceAdjustments =
    [
        new("demo-price-001", "demo-customer-002", "demo-order-004", 1480m, 1280m, $"{DemoMarker} 老客户转介绍，申请减免部分加急费用。", PriceAdjustmentStatus.PendingApproval, -2),
        new("demo-price-002", "demo-customer-003", "demo-order-005", 10200m, 9600m, $"{DemoMarker} 企业批量采购，已按阶梯价审批。", PriceAdjustmentStatus.Approved, -1)
    ];

    private static readonly DemoActivityLog[] DemoActivityLogs =
    [
        new("demo-activity-001", "demo-customer-001", "demo-order-001", ActivityType.CustomerCreated, $"{DemoMarker} 新增客户", $"{DemoMarker} 从微信私域新增林小姐。", -36),
        new("demo-activity-002", "demo-customer-001", "demo-order-001", ActivityType.OrderCreated, $"{DemoMarker} 创建订单", $"{DemoMarker} 婚礼伴手礼定制需求已建单。", -30),
        new("demo-activity-003", "demo-customer-001", "demo-order-002", ActivityType.NoteCreated, $"{DemoMarker} 新增备注", $"{DemoMarker} 记录包装偏好和报价范围。", -28),
        new("demo-activity-004", "demo-customer-002", "demo-order-003", ActivityType.OrderStatusChanged, $"{DemoMarker} 订单状态变更", $"{DemoMarker} 闲鱼摆件修复已报价。", -24),
        new("demo-activity-005", "demo-customer-002", "demo-order-004", ActivityType.FollowUpCreated, $"{DemoMarker} 新增跟进", $"{DemoMarker} 安排加急工期说明。", -18),
        new("demo-activity-006", "demo-customer-002", "demo-order-004", ActivityType.PriceAdjustmentRequested, $"{DemoMarker} 发起改价", $"{DemoMarker} 申请加急费用减免。", -12),
        new("demo-activity-007", "demo-customer-003", "demo-order-005", ActivityType.PriceAdjustmentApproved, $"{DemoMarker} 改价通过", $"{DemoMarker} 企业批量价审批通过。", -8),
        new("demo-activity-008", "demo-customer-003", "demo-order-005", ActivityType.FollowUpCreated, $"{DemoMarker} 新增跟进", $"{DemoMarker} 安排企业礼盒排产同步。", -3)
    ];
}
