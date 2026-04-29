using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class PipelineStageRuleEngine
{
    public static PipelineStageSnapshot Resolve(PipelineStageResolutionContext context)
    {
        var resolvedAt = DateTime.Now;
        var scopedOrders = FilterByOrder(context.Orders, context.OrderId, order => order.Id).ToList();
        var scopedDeals = context.Order?.DealId is int selectedDealId
            ? context.Deals.Where(deal => deal.Id == selectedDealId || deal.CustomerId == context.Customer.Id).ToList()
            : context.Deals.ToList();
        var scopedMessages = FilterByOrder(context.Messages, context.OrderId, message => message.OrderId).ToList();
        var scopedSuggestions = FilterByOrder(context.Suggestions, context.OrderId, suggestion => suggestion.OrderId).ToList();
        var scopedFollowUps = FilterByOrder(context.FollowUps, context.OrderId, followUp => followUp.OrderId).ToList();
        var scopedActivities = FilterByOrder(context.Activities, context.OrderId, activity => activity.OrderId).ToList();
        var scopedAdjustments = FilterByOrder(context.PriceAdjustments, context.OrderId, adjustment => adjustment.OrderId).ToList();
        var order = context.Order ?? scopedOrders.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id).FirstOrDefault();
        var deal = context.Order?.DealId is int dealId
            ? scopedDeals.FirstOrDefault(item => item.Id == dealId)
            : scopedDeals.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id).FirstOrDefault();

        if (context.Customer.Id <= 0)
        {
            return CreateSnapshot(context, PipelineStage.New, "未找到客户，回退到 New。", true, resolvedAt);
        }

        if (order?.Status == OrderStatus.Closed)
        {
            var fulfilled = deal?.Stage == DealStage.Won || scopedFollowUps.Any(followUp => followUp.Status == FollowUpStatus.Completed);
            return fulfilled
                ? CreateSnapshot(context, PipelineStage.Fulfilled, "订单已关闭，且存在成交或履约完成信号。", false, resolvedAt, deal?.Id)
                : CreateSnapshot(context, PipelineStage.Lost, "订单已关闭，未发现成交完成信号。", false, resolvedAt, deal?.Id);
        }

        if (deal?.Stage == DealStage.Lost)
        {
            return CreateSnapshot(context, PipelineStage.Lost, "DealStage 为 Lost。", false, resolvedAt, deal.Id);
        }

        if (order?.Status == OrderStatus.Won || deal?.Stage == DealStage.Won)
        {
            return CreateSnapshot(context, PipelineStage.Paid, "订单或 Deal 已进入成交状态。", false, resolvedAt, deal?.Id);
        }

        if (HasSentDraft(scopedSuggestions, scopedActivities))
        {
            return CreateSnapshot(context, PipelineStage.WaitingPayment, "已存在本地草稿已发送信号，等待付款/成交确认。", false, resolvedAt, deal?.Id);
        }

        if (HasPreparedDraft(scopedSuggestions))
        {
            return CreateSnapshot(context, PipelineStage.DraftPrepared, "已存在 prepared/copied 本地草稿。", false, resolvedAt, deal?.Id);
        }

        if (HasQuoteSignal(order, deal, scopedAdjustments))
        {
            return CreateSnapshot(context, PipelineStage.Quoted, "存在报价、改价或 Deal 报价阶段信号。", false, resolvedAt, deal?.Id);
        }

        if (HasInterestSignal(context.Customer, scopedSuggestions, scopedActivities))
        {
            return CreateSnapshot(context, PipelineStage.Interested, "存在 AI 建议或近期活跃信号。", false, resolvedAt, deal?.Id);
        }

        if (scopedMessages.Count > 0 || scopedActivities.Any(activity => activity.Type == ActivityType.ConversationMessageAdded))
        {
            return CreateSnapshot(context, PipelineStage.Contacted, "已存在沟通记录。", false, resolvedAt, deal?.Id);
        }

        var hasOrderOrFollowUp = scopedOrders.Count > 0 || scopedFollowUps.Count > 0;
        return hasOrderOrFollowUp
            ? CreateSnapshot(context, PipelineStage.New, "已有订单/跟进，但缺少更明确的阶段信号，安全回退到 New。", true, resolvedAt, deal?.Id)
            : CreateSnapshot(context, PipelineStage.New, "无沟通、无订单，保持 New。", false, resolvedAt, deal?.Id);
    }

    private static bool HasQuoteSignal(MerchantOrder? order, Deal? deal, IEnumerable<PriceAdjustment> adjustments)
    {
        return order?.Status == OrderStatus.Quoted
            || deal?.Stage is DealStage.Quoting or DealStage.Negotiating
            || adjustments.Any(adjustment => adjustment.Status is PriceAdjustmentStatus.PendingApproval or PriceAdjustmentStatus.Approved or PriceAdjustmentStatus.Applied);
    }

    private static bool HasPreparedDraft(IEnumerable<AiSuggestion> suggestions)
    {
        return suggestions.Any(suggestion =>
        {
            var state = ProjectionMetadataHelper.ReadAutoReplyState(suggestion.MetadataJson);
            return suggestion.Status == AiSuggestionStatus.DraftPrepared
                || string.Equals(state, "prepared", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "copied", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool HasSentDraft(IEnumerable<AiSuggestion> suggestions, IEnumerable<ActivityLog> activities)
    {
        var hasSuggestionSent = suggestions.Any(suggestion =>
            suggestion.Status == AiSuggestionStatus.Sent ||
            string.Equals(ProjectionMetadataHelper.ReadAutoReplyState(suggestion.MetadataJson), "sent", StringComparison.OrdinalIgnoreCase));
        if (hasSuggestionSent)
        {
            return true;
        }

        return activities.Any(activity => activity.Type == ActivityType.AutoReplySent);
    }

    private static bool HasInterestSignal(Customer customer, IEnumerable<AiSuggestion> suggestions, IEnumerable<ActivityLog> activities)
    {
        if (suggestions.Any())
        {
            return true;
        }

        var threshold = DateTime.Today.AddDays(-7);
        return customer.LastContactAt >= threshold
            || activities.Any(activity =>
                activity.CreatedAt >= threshold &&
                activity.Type is not ActivityType.ConversationMessageAdded);
    }

    private static IEnumerable<T> FilterByOrder<T>(
        IEnumerable<T> items,
        int? orderId,
        Func<T, int?> orderSelector)
    {
        if (orderId is null)
        {
            return items;
        }

        return items.Where(item =>
        {
            var itemOrderId = orderSelector(item);
            return itemOrderId == orderId || itemOrderId is null;
        });
    }

    private static PipelineStageSnapshot CreateSnapshot(
        PipelineStageResolutionContext context,
        PipelineStage stage,
        string reason,
        bool usedFallback,
        DateTime resolvedAt,
        int? dealId = null)
    {
        return new PipelineStageSnapshot
        {
            CustomerId = context.Customer.Id,
            OrderId = context.OrderId,
            DealId = dealId,
            Stage = stage,
            Reason = reason,
            UsedFallback = usedFallback,
            ResolvedAt = resolvedAt
        };
    }
}

internal sealed class PipelineStageResolutionContext
{
    public required Customer Customer { get; init; }
    public int? OrderId { get; init; }
    public MerchantOrder? Order { get; init; }
    public required IReadOnlyList<MerchantOrder> Orders { get; init; }
    public required IReadOnlyList<Deal> Deals { get; init; }
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }
    public required IReadOnlyList<AiSuggestion> Suggestions { get; init; }
    public required IReadOnlyList<FollowUp> FollowUps { get; init; }
    public required IReadOnlyList<ActivityLog> Activities { get; init; }
    public required IReadOnlyList<PriceAdjustment> PriceAdjustments { get; init; }
}
