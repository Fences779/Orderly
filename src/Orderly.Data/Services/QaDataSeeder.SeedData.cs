using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
    private static readonly QaCustomer[] QaCustomers =
    [
        new("p13qa-customer-a", "p13qa-customer-a", $"{QaMarker} 客户-A", CustomerStatus.Active, CustomerPriority.High, "微信", "私域咨询", "p13qa_customer_a", "13800130001", $"{QaMarker} 用于验证 FollowUp 完成/延期/取消、状态切换和备注模板。", -7, 10),
        new("p13qa-customer-b", "p13qa-customer-b", $"{QaMarker} 客户-B", CustomerStatus.Dormant, CustomerPriority.Normal, "闲鱼", "店铺咨询", "p13qa_customer_b", "13800130002", $"{QaMarker} 用于验证已成交订单、成交阶段推进和改价终态。", -5, 15)
    ];

    private static readonly QaDeal[] QaDeals =
    [
        new("p13qa-deal-001", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", DealStage.Negotiating, 1880m, $"{QaMarker} 用于验证 ChangeDealStageCommand 的可推进成交机会。", "微信", "私域咨询", -4, 2),
        new("p13qa-deal-002", $"{QaMarker} 客户-B", $"{QaMarker} Deal-已成交", DealStage.Won, 5200m, $"{QaMarker} 用于验证高阶段/已成交数据展示。", "闲鱼", "店铺咨询", -3, -1)
    ];

    private static readonly QaOrder[] QaOrders =
    [
        new("p13qa-order-001", "p13qa-order-001", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", $"{QaMarker} 订单-待处理", OrderStatus.PendingFollowUp, 1680m, $"{QaMarker} 用于验证订单状态切换、跟进按钮和 AddOrder 保存回读。", "微信", "私域咨询", -4, 0, 11),
        new("p13qa-order-002", "p13qa-order-002", $"{QaMarker} 客户-B", $"{QaMarker} Deal-已成交", $"{QaMarker} 订单-已成交", OrderStatus.Won, 5200m, $"{QaMarker} 用于验证已成交订单、成交阶段和终态跟进。", "闲鱼", "店铺咨询", -3, 1, 15),
        new("p13qa-order-003", "p13qa-order-003", $"{QaMarker} 客户-A", $"{QaMarker} Deal-当前推进", $"{QaMarker} 订单-需跟进", OrderStatus.PendingQuote, 980m, $"{QaMarker} 用于验证待处理搜索、逾期跟进和改价待审批。", "微信", "私域咨询", -2, -1, 16)
    ];

    private static readonly QaFollowUp[] QaFollowUps =
    [
        new("p13qa-followup-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", $"{QaMarker} Deal-当前推进", $"{QaMarker} 今日跟进", $"{QaMarker} 今日 Pending 跟进，用于验证完成/延期/取消按钮可见。", FollowUpStatus.Pending, -1, 0, 10),
        new("p13qa-followup-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", $"{QaMarker} 逾期跟进", $"{QaMarker} 逾期 Pending 跟进，用于验证 overdue quick filter。", FollowUpStatus.Pending, -2, -1, 14),
        new("p13qa-followup-003", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", $"{QaMarker} 明日跟进", $"{QaMarker} 明日 Pending 跟进，用于验证 tomorrow quick filter。", FollowUpStatus.Pending, -1, 1, 9),
        new("p13qa-followup-004", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", $"{QaMarker} 已完成跟进", $"{QaMarker} 已完成终态跟进，用于验证终态隐藏操作按钮。", FollowUpStatus.Completed, -3, -2, 16)
    ];

    private static readonly QaNote[] QaNotes =
    [
        new("p13qa-note-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", NoteType.Internal, $"{QaMarker} 模板备注验证：已插入标准报价模板。p13qa-note-keyword", true, -2),
        new("p13qa-note-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", NoteType.Requirement, $"{QaMarker} 需确认材质、数量和最终交付日期。", false, -1),
        new("p13qa-note-003", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", NoteType.Preference, $"{QaMarker} 成交后保持每周一次回访节奏。", false, -1)
    ];

    private static readonly QaPriceAdjustment[] QaPriceAdjustments =
    [
        new("p13qa-price-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", 1180m, 980m, $"{QaMarker} UIA 改价待审批验证", PriceAdjustmentStatus.PendingApproval, -1),
        new("p13qa-price-002", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", 5600m, 5200m, $"{QaMarker} UIA 改价已通过验证", PriceAdjustmentStatus.Approved, -1)
    ];

    private static readonly QaActivityLog[] QaActivityLogs =
    [
        new("p13qa-activity-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.CustomerCreated, $"{QaMarker} 新增客户", $"{QaMarker} 新增客户-A，用于 QA 演示。", -30),
        new("p13qa-activity-002", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.OrderCreated, $"{QaMarker} 创建订单", $"{QaMarker} 创建订单-待处理。", -28),
        new("p13qa-activity-003", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.NoteCreated, $"{QaMarker} 新增备注", $"{QaMarker} 新增模板备注验证记录。", -26),
        new("p13qa-activity-004", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", null, ActivityType.FollowUpCreated, $"{QaMarker} 新增跟进", $"{QaMarker} 新增今日 Pending 跟进。", -24),
        new("p13qa-activity-005", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", ActivityType.PriceAdjustmentRequested, $"{QaMarker} 新增改价", $"{QaMarker} 发起待审批改价申请。", -22),
        new("p13qa-activity-006", $"{QaMarker} 客户-B", null, null, ActivityType.CustomerStatusChanged, $"{QaMarker} 客户状态变化", $"{QaMarker} 客户-B 状态切换到 Dormant。", -20),
        new("p13qa-activity-007", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", null, ActivityType.OrderStatusChanged, $"{QaMarker} 订单状态变化", $"{QaMarker} 订单-需跟进状态切换到 PendingQuote。", -18),
        new("p13qa-activity-008", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", $"{QaMarker} Deal-当前推进", ActivityType.DealStageChanged, $"{QaMarker} Deal 阶段变化", $"{QaMarker} Deal-当前推进已切换到 Negotiating。", -16),
        new("p13qa-activity-009", $"{QaMarker} 客户-A", $"{QaMarker} 订单-需跟进", $"{QaMarker} Deal-当前推进", ActivityType.FollowUpSnoozed, $"{QaMarker} 跟进延期", $"{QaMarker} 逾期跟进已从昨天延后到今日。", -14),
        new("p13qa-activity-010", $"{QaMarker} 客户-B", $"{QaMarker} 订单-已成交", $"{QaMarker} Deal-已成交", ActivityType.FollowUpCompleted, $"{QaMarker} 跟进完成", $"{QaMarker} 已完成跟进进入终态。", -12)
    ];

    private static readonly QaConversationMessage[] QaConversationMessages =
    [
        new("p2qa-message-001", "p2qa-source-message-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", $"{QaMarker} Deal-当前推进", MessageDirection.Incoming, MessageChannel.Manual, $"{QaDataScope.P2DisplayMarker} 客户-A", $"{QaDataScope.P2DisplayMarker} 你好，我想先确认尺寸和交付时间。", -8, -8)
    ];

    private static readonly QaAiSuggestion[] QaAiSuggestions =
    [
        new("p2qa-suggestion-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", "p2qa-message-001", $"{QaDataScope.P2DisplayMarker} 【Local Stub】这是本地模拟回复建议，后续将接入真实 AI。", $"{QaDataScope.P2DisplayMarker} 本地 Stub 建议，仅用于 QA 验证。", AiSuggestionStatus.Draft, -7)
    ];

    private static readonly QaOcrResult[] QaOcrResults =
    [
        new("p2qa-ocr-001", $"{QaMarker} 客户-A", $"{QaMarker} 订单-待处理", "qa/p2qa/sample-ocr.png", $"{QaDataScope.P2DisplayMarker} OCR 样本", string.Empty, OcrStatus.Pending, string.Empty, -6)
    ];

    private static readonly QaSyncRecord[] QaSyncRecords =
    [
        new("p2qa-sync-001", "ConversationMessage", "p2qa-message-001", SyncStatus.Pending, string.Empty, -5)
    ];
}
