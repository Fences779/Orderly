using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class LocalWorkbenchTaskService
{
    private static IEnumerable<WorkbenchTask> BuildFollowUpTasks(
        IEnumerable<FollowUp> followUps,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        DateTime today)
    {
        foreach (var followUp in followUps)
        {
            if (!FollowUpStatusHelper.IsOpen(followUp))
            {
                continue;
            }

            var type = FollowUpStatusHelper.IsOverdue(followUp, today)
                ? WorkbenchTaskType.FollowUpOverdue
                : FollowUpStatusHelper.IsScheduledOn(followUp, today)
                    ? WorkbenchTaskType.FollowUpToday
                    : (WorkbenchTaskType?)null;

            if (type is null)
            {
                continue;
            }

            customerMap.TryGetValue(followUp.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (followUp.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            var title = type == WorkbenchTaskType.FollowUpOverdue
                ? $"逾期跟进：{GetTitleOrDefault(followUp.Title, customer?.Name)}"
                : $"今日跟进：{GetTitleOrDefault(followUp.Title, customer?.Name)}";

            yield return CreateTask(
                id: $"followup-{followUp.Id}",
                type: type.Value,
                priority: type == WorkbenchTaskType.FollowUpOverdue ? WorkbenchTaskPriority.Critical : WorkbenchTaskPriority.Medium,
                title: title,
                summary: string.IsNullOrWhiteSpace(followUp.Content)
                    ? $"计划时间 {followUp.ScheduledAt:yyyy-MM-dd HH:mm}"
                    : $"{TrimPreview(followUp.Content)} · {followUp.ScheduledAt:yyyy-MM-dd HH:mm}",
                customer: customer,
                order: order,
                relatedEntityType: nameof(FollowUp),
                relatedEntityId: followUp.Id,
                occurredAt: followUp.ScheduledAt,
                dealId: followUp.DealId,
                followUpId: followUp.Id,
                targetSection: ProjectionTargetSections.FollowUp,
                actionHint: ProjectionActionHints.CompleteFollowUp,
                dedupeKey: $"followup-{type}-{followUp.Id}");
        }
    }

    private static IEnumerable<WorkbenchTask> BuildDraftTasks(
        IEnumerable<AiSuggestion> suggestions,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap)
    {
        foreach (var suggestion in suggestions)
        {
            var autoReplyState = ProjectionMetadataHelper.ReadAutoReplyState(suggestion.MetadataJson);
            var isCopiedDraft = AutoReplyState.IsCopied(autoReplyState);
            var isPreparedDraft = suggestion.Status == AiSuggestionStatus.DraftPrepared
                || AutoReplyState.IsPreparedDraft(autoReplyState);
            var isSent = suggestion.Status == AiSuggestionStatus.Sent
                || AutoReplyState.IsSent(autoReplyState);

            if (!isPreparedDraft || isSent)
            {
                continue;
            }

            customerMap.TryGetValue(suggestion.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (suggestion.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            var title = isCopiedDraft ? "草稿已复制，待手动发送" : "草稿已准备，待发送";
            var summary = isCopiedDraft
                ? $"已复制到剪贴板：{TrimPreview(suggestion.SuggestionText)}"
                : $"本地草稿未发送：{TrimPreview(suggestion.SuggestionText)}";

            yield return CreateTask(
                id: $"draft-{suggestion.Id}",
                type: WorkbenchTaskType.DraftNotSent,
                priority: isCopiedDraft ? WorkbenchTaskPriority.Critical : WorkbenchTaskPriority.High,
                title: title,
                summary: summary,
                customer: customer,
                order: order,
                relatedEntityType: nameof(AiSuggestion),
                relatedEntityId: suggestion.Id,
                occurredAt: suggestion.UpdatedAt,
                dealId: order?.DealId,
                messageId: suggestion.MessageId,
                aiSuggestionId: suggestion.Id,
                targetSection: ProjectionTargetSections.AiSuggestion,
                actionHint: ProjectionActionHints.ReviewDraft,
                dedupeKey: $"draft-{suggestion.Id}");
        }
    }

    private static IEnumerable<WorkbenchTask> BuildReplyNeededTasks(
        IEnumerable<ConversationMessage> messages,
        IEnumerable<AiSuggestion> suggestions,
        IEnumerable<ActivityLog> activities,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap)
    {
        var suggestionList = suggestions.ToList();
        var activityList = activities.ToList();

        var groups = messages
            .GroupBy(message => new ConversationScopeKey(message.CustomerId, message.OrderId))
            .ToList();

        foreach (var group in groups)
        {
            var latestIncoming = group
                .Where(message => message.Direction == MessageDirection.Incoming)
                .OrderByDescending(message => message.MessageTime)
                .ThenByDescending(message => message.Id)
                .FirstOrDefault();

            if (latestIncoming is null)
            {
                continue;
            }

            var hasOutgoingAfterIncoming = group.Any(message =>
                message.Direction == MessageDirection.Outgoing &&
                IsAfter(message.MessageTime, message.Id, latestIncoming.MessageTime, latestIncoming.Id));
            if (hasOutgoingAfterIncoming)
            {
                continue;
            }

            var hasSentSuggestionAfterIncoming = suggestionList.Any(suggestion =>
                suggestion.CustomerId == group.Key.CustomerId &&
                suggestion.OrderId == group.Key.OrderId &&
                suggestion.Status == AiSuggestionStatus.Sent &&
                IsAfter(suggestion.UpdatedAt, suggestion.Id, latestIncoming.MessageTime, latestIncoming.Id));
            if (hasSentSuggestionAfterIncoming)
            {
                continue;
            }

            var hasSentActivityAfterIncoming = activityList.Any(activity =>
                activity.Type == ActivityType.AutoReplySent &&
                activity.CustomerId == group.Key.CustomerId &&
                activity.OrderId == group.Key.OrderId &&
                IsAfter(activity.CreatedAt, activity.Id, latestIncoming.MessageTime, latestIncoming.Id));
            if (hasSentActivityAfterIncoming)
            {
                continue;
            }

            customerMap.TryGetValue(group.Key.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (group.Key.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return CreateTask(
                id: $"reply-{latestIncoming.Id}",
                type: WorkbenchTaskType.ReplyNeeded,
                priority: WorkbenchTaskPriority.High,
                title: "收到新消息，待回复",
                summary: TrimPreview(latestIncoming.Content),
                customer: customer,
                order: order,
                relatedEntityType: nameof(ConversationMessage),
                relatedEntityId: latestIncoming.Id,
                occurredAt: latestIncoming.MessageTime,
                dealId: latestIncoming.DealId ?? order?.DealId,
                messageId: latestIncoming.Id,
                targetSection: ProjectionTargetSections.Conversation,
                actionHint: ProjectionActionHints.ReplyToCustomer,
                dedupeKey: $"reply-{group.Key.CustomerId}-{group.Key.OrderId?.ToString() ?? "none"}");
        }
    }

    private static IEnumerable<WorkbenchTask> BuildAiSuggestionPendingTasks(
        IEnumerable<AiSuggestion> suggestions,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap)
    {
        foreach (var suggestion in suggestions.Where(item => item.Status == AiSuggestionStatus.Draft))
        {
            customerMap.TryGetValue(suggestion.CustomerId, out var customer);
            MerchantOrder? order = null;
            if (suggestion.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return CreateTask(
                id: $"ai-pending-{suggestion.Id}",
                type: WorkbenchTaskType.AiSuggestionPending,
                priority: WorkbenchTaskPriority.Medium,
                title: "AI 建议待处理",
                summary: TrimPreview(suggestion.SuggestionText),
                customer: customer,
                order: order,
                relatedEntityType: nameof(AiSuggestion),
                relatedEntityId: suggestion.Id,
                occurredAt: suggestion.CreatedAt,
                dealId: order?.DealId,
                messageId: suggestion.MessageId,
                aiSuggestionId: suggestion.Id,
                targetSection: ProjectionTargetSections.AiSuggestion,
                actionHint: ProjectionActionHints.ReviewSuggestion,
                dedupeKey: $"ai-pending-{suggestion.Id}");
        }
    }

    private static IEnumerable<WorkbenchTask> BuildOcrTasks(
        IEnumerable<OcrResult> ocrResults,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap)
    {
        foreach (var ocrResult in ocrResults)
        {
            if (ocrResult.Status != OcrStatus.Completed || ProjectionMetadataHelper.ReadConvertedToMessageId(ocrResult.MetadataJson) is > 0)
            {
                continue;
            }

            Customer? customer = null;
            if (ocrResult.CustomerId is int customerId)
            {
                customerMap.TryGetValue(customerId, out customer);
            }

            MerchantOrder? order = null;
            if (ocrResult.OrderId is int orderId)
            {
                orderMap.TryGetValue(orderId, out order);
            }

            yield return CreateTask(
                id: $"ocr-{ocrResult.Id}",
                type: WorkbenchTaskType.OcrNotConverted,
                priority: WorkbenchTaskPriority.Medium,
                title: "OCR 已完成，待转消息",
                summary: string.IsNullOrWhiteSpace(ocrResult.ExtractedText)
                    ? GetTitleOrDefault(ocrResult.SourceName, "OCR 结果")
                    : $"{GetTitleOrDefault(ocrResult.SourceName, "OCR 结果")} · {TrimPreview(ocrResult.ExtractedText)}",
                customer: customer,
                order: order,
                relatedEntityType: nameof(OcrResult),
                relatedEntityId: ocrResult.Id,
                occurredAt: ocrResult.UpdatedAt,
                dealId: order?.DealId,
                ocrResultId: ocrResult.Id,
                targetSection: ProjectionTargetSections.Ocr,
                actionHint: ProjectionActionHints.ConvertOcrToMessage,
                dedupeKey: $"ocr-{ocrResult.Id}");
        }
    }

    private static IEnumerable<WorkbenchTask> BuildRecentlyActiveTasks(
        IEnumerable<Customer> customers,
        IEnumerable<ConversationMessage> messages,
        IEnumerable<ActivityLog> activities,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        ISet<int> customersWithBlockingTasks,
        DateTime today)
    {
        var threshold = today.AddDays(-RecentlyActiveWindowDays);

        foreach (var customer in customers)
        {
            if (customersWithBlockingTasks.Contains(customer.Id))
            {
                continue;
            }

            var recentMessage = messages
                .Where(message => message.CustomerId == customer.Id && message.MessageTime >= threshold)
                .OrderByDescending(message => message.MessageTime)
                .ThenByDescending(message => message.Id)
                .FirstOrDefault();

            var recentActivity = activities
                .Where(activity => activity.CustomerId == customer.Id && activity.CreatedAt >= threshold)
                .OrderByDescending(activity => activity.CreatedAt)
                .ThenByDescending(activity => activity.Id)
                .FirstOrDefault();

            var candidates = new List<RecentSignalCandidate>();

            if (recentActivity is not null)
            {
                candidates.Add(new RecentSignalCandidate(
                    OccurredAt: recentActivity.CreatedAt,
                    Summary: $"最近动态：{TrimPreview(GetTitleOrDefault(recentActivity.Title, recentActivity.Description))}",
                    Order: ResolveOrder(orderMap, recentActivity.OrderId),
                    RelatedEntityType: nameof(ActivityLog),
                    RelatedEntityId: recentActivity.Id,
                    MessageId: null,
                    TargetSection: recentActivity.Type == ActivityType.ConversationMessageAdded ? ProjectionTargetSections.Conversation : ProjectionTargetSections.Customer,
                    ActionHint: recentActivity.Type == ActivityType.ConversationMessageAdded ? ProjectionActionHints.ReplyToCustomer : ProjectionActionHints.OpenCustomer,
                    SignalRank: 1));
            }

            if (recentMessage is not null)
            {
                candidates.Add(new RecentSignalCandidate(
                    OccurredAt: recentMessage.MessageTime,
                    Summary: $"最近消息：{TrimPreview(recentMessage.Content)}",
                    Order: ResolveOrder(orderMap, recentMessage.OrderId),
                    RelatedEntityType: nameof(ConversationMessage),
                    RelatedEntityId: recentMessage.Id,
                    MessageId: recentMessage.Id,
                    TargetSection: ProjectionTargetSections.Conversation,
                    ActionHint: ProjectionActionHints.ReplyToCustomer,
                    SignalRank: 2));
            }

            if (customer.UpdatedAt >= threshold)
            {
                candidates.Add(new RecentSignalCandidate(
                    OccurredAt: customer.UpdatedAt,
                    Summary: "客户资料最近有更新",
                    Order: null,
                    RelatedEntityType: nameof(Customer),
                    RelatedEntityId: customer.Id,
                    MessageId: null,
                    TargetSection: ProjectionTargetSections.Customer,
                    ActionHint: ProjectionActionHints.OpenCustomer,
                    SignalRank: 3));
            }

            var latest = candidates
                .OrderByDescending(candidate => candidate.OccurredAt)
                .ThenBy(candidate => candidate.SignalRank)
                .FirstOrDefault();

            if (latest == default)
            {
                continue;
            }

            yield return CreateTask(
                id: $"recent-{customer.Id}",
                type: WorkbenchTaskType.RecentlyActiveCustomer,
                priority: WorkbenchTaskPriority.Low,
                title: "最近活跃客户",
                summary: latest.Summary,
                customer: customer,
                order: latest.Order,
                relatedEntityType: latest.RelatedEntityType,
                relatedEntityId: latest.RelatedEntityId,
                occurredAt: latest.OccurredAt,
                dealId: latest.Order?.DealId,
                messageId: latest.MessageId,
                targetSection: latest.TargetSection,
                actionHint: latest.ActionHint,
                dedupeKey: $"recent-{customer.Id}");
        }
    }

    private static WorkbenchTask CreateTask(
        string id,
        WorkbenchTaskType type,
        WorkbenchTaskPriority priority,
        string title,
        string summary,
        Customer? customer,
        MerchantOrder? order,
        string relatedEntityType,
        int? relatedEntityId,
        DateTime occurredAt,
        int? dealId = null,
        int? messageId = null,
        int? aiSuggestionId = null,
        int? ocrResultId = null,
        int? followUpId = null,
        string targetSection = "",
        string actionHint = "",
        string? dedupeKey = null)
    {
        var task = new WorkbenchTask
        {
            Id = id,
            Type = type,
            Priority = priority,
            Title = title,
            Summary = summary,
            CustomerId = customer?.Id ?? order?.CustomerId,
            CustomerName = customer?.Name ?? order?.Customer?.Name ?? string.Empty,
            OrderId = order?.Id,
            OrderDisplay = order?.Title ?? string.Empty,
            DealId = dealId ?? order?.DealId,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            MessageId = messageId,
            AiSuggestionId = aiSuggestionId,
            OcrResultId = ocrResultId,
            FollowUpId = followUpId,
            TargetSection = targetSection,
            ActionHint = actionHint,
            OccurredAt = occurredAt
        };

        task.DedupeKey = string.IsNullOrWhiteSpace(dedupeKey)
            ? BuildDefaultDedupeKey(task)
            : dedupeKey;
        task.SortKey = BuildSortKey(task);
        return task;
    }

    private static MerchantOrder? ResolveOrder(IReadOnlyDictionary<int, MerchantOrder> orderMap, int? orderId)
    {
        if (orderId is not int resolvedOrderId)
        {
            return null;
        }

        return orderMap.TryGetValue(resolvedOrderId, out var order)
            ? order
            : null;
    }

    private readonly record struct ConversationScopeKey(int CustomerId, int? OrderId);

    private readonly record struct RecentSignalCandidate(
        DateTime OccurredAt,
        string Summary,
        MerchantOrder? Order,
        string RelatedEntityType,
        int? RelatedEntityId,
        int? MessageId,
        string TargetSection,
        string ActionHint,
        int SignalRank);
}
