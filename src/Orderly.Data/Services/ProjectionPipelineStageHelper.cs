using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class ProjectionPipelineStageHelper
{
    public static void ApplyToWorkbenchTasks(
        IEnumerable<WorkbenchTask> tasks,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyList<MerchantOrder> orders,
        IReadOnlyList<Deal> deals,
        IReadOnlyList<FollowUp> followUps,
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<AiSuggestion> suggestions,
        IReadOnlyList<ActivityLog> activities,
        IReadOnlyList<PriceAdjustment> priceAdjustments)
    {
        var context = CreateContext(customerMap, orders, deals, followUps, messages, suggestions, activities, priceAdjustments);
        foreach (var task in tasks)
        {
            task.PipelineStage = ResolvePipelineStage(context, task.CustomerId, task.OrderId);
        }
    }

    public static void ApplyToSearchResults(
        IEnumerable<SearchResultItem> items,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyList<MerchantOrder> orders,
        IReadOnlyList<Deal> deals,
        IReadOnlyList<FollowUp> followUps,
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<AiSuggestion> suggestions,
        IReadOnlyList<ActivityLog> activities,
        IReadOnlyList<PriceAdjustment> priceAdjustments)
    {
        var context = CreateContext(customerMap, orders, deals, followUps, messages, suggestions, activities, priceAdjustments);
        foreach (var item in items)
        {
            item.PipelineStage = ResolvePipelineStage(context, item.CustomerId, item.OrderId);
        }
    }

    private static PipelineStage? ResolvePipelineStage(ProjectionPipelineContext context, int? customerId, int? orderId)
    {
        if (customerId is not int resolvedCustomerId || !context.CustomerMap.TryGetValue(resolvedCustomerId, out var customer))
        {
            return null;
        }

        context.CustomerOrders.TryGetValue(resolvedCustomerId, out var scopedOrders);
        context.CustomerDeals.TryGetValue(resolvedCustomerId, out var scopedDeals);
        context.CustomerFollowUps.TryGetValue(resolvedCustomerId, out var scopedFollowUps);
        context.CustomerMessages.TryGetValue(resolvedCustomerId, out var scopedMessages);
        context.CustomerSuggestions.TryGetValue(resolvedCustomerId, out var scopedSuggestions);
        context.CustomerActivities.TryGetValue(resolvedCustomerId, out var scopedActivities);
        context.CustomerAdjustments.TryGetValue(resolvedCustomerId, out var scopedAdjustments);

        var resolutionContext = new PipelineStageResolutionContext
        {
            Customer = customer,
            OrderId = orderId,
            Order = orderId is int selectedOrderId ? scopedOrders?.FirstOrDefault(order => order.Id == selectedOrderId) : null,
            Orders = scopedOrders ?? Array.Empty<MerchantOrder>(),
            Deals = scopedDeals ?? Array.Empty<Deal>(),
            Messages = scopedMessages ?? Array.Empty<ConversationMessage>(),
            Suggestions = scopedSuggestions ?? Array.Empty<AiSuggestion>(),
            FollowUps = scopedFollowUps ?? Array.Empty<FollowUp>(),
            Activities = scopedActivities ?? Array.Empty<ActivityLog>(),
            PriceAdjustments = scopedAdjustments ?? Array.Empty<PriceAdjustment>()
        };

        return PipelineStageRuleEngine.Resolve(resolutionContext).Stage;
    }

    private static ProjectionPipelineContext CreateContext(
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyList<MerchantOrder> orders,
        IReadOnlyList<Deal> deals,
        IReadOnlyList<FollowUp> followUps,
        IReadOnlyList<ConversationMessage> messages,
        IReadOnlyList<AiSuggestion> suggestions,
        IReadOnlyList<ActivityLog> activities,
        IReadOnlyList<PriceAdjustment> priceAdjustments)
    {
        return new ProjectionPipelineContext
        {
            CustomerMap = customerMap,
            CustomerOrders = orders.GroupBy(order => order.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<MerchantOrder>)group.ToList()),
            CustomerDeals = deals.GroupBy(deal => deal.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<Deal>)group.ToList()),
            CustomerFollowUps = followUps.GroupBy(followUp => followUp.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<FollowUp>)group.ToList()),
            CustomerMessages = messages.GroupBy(message => message.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<ConversationMessage>)group.ToList()),
            CustomerSuggestions = suggestions.GroupBy(suggestion => suggestion.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<AiSuggestion>)group.ToList()),
            CustomerActivities = activities.Where(activity => activity.CustomerId is int)
                .GroupBy(activity => activity.CustomerId!.Value)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ActivityLog>)group.ToList()),
            CustomerAdjustments = priceAdjustments.GroupBy(adjustment => adjustment.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<PriceAdjustment>)group.ToList())
        };
    }

    private sealed class ProjectionPipelineContext
    {
        public required IReadOnlyDictionary<int, Customer> CustomerMap { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<MerchantOrder>> CustomerOrders { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<Deal>> CustomerDeals { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<FollowUp>> CustomerFollowUps { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<ConversationMessage>> CustomerMessages { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<AiSuggestion>> CustomerSuggestions { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<ActivityLog>> CustomerActivities { get; init; }
        public required IReadOnlyDictionary<int, IReadOnlyList<PriceAdjustment>> CustomerAdjustments { get; init; }
    }
}
