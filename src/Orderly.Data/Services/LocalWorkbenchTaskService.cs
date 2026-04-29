using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalWorkbenchTaskService : IWorkbenchTaskService
{
    private const int RecentlyActiveWindowDays = 7;
    private const int RecentlyActiveLimit = 5;

    private const string TargetSectionCustomer = "Customer";
    private const string TargetSectionConversation = "Conversation";
    private const string TargetSectionAiSuggestion = "AiSuggestion";
    private const string TargetSectionOcr = "Ocr";
    private const string TargetSectionFollowUp = "FollowUp";

    private const string ActionHintReviewDraft = "ReviewDraft";
    private const string ActionHintReviewSuggestion = "ReviewSuggestion";
    private const string ActionHintConvertOcr = "ConvertOcr";
    private const string ActionHintCompleteFollowUp = "CompleteFollowUp";
    private const string ActionHintReplyToCustomer = "ReplyToCustomer";

    private static readonly HashSet<WorkbenchTaskType> RecentlyActiveBlockedTypes =
    [
        WorkbenchTaskType.FollowUpOverdue,
        WorkbenchTaskType.DraftNotSent,
        WorkbenchTaskType.ReplyNeeded,
        WorkbenchTaskType.AiSuggestionPending,
        WorkbenchTaskType.OcrNotConverted,
        WorkbenchTaskType.FollowUpToday
    ];

    private readonly ICustomerRepository _customerRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDealRepository _dealRepository;
    private readonly IFollowUpRepository _followUpRepository;
    private readonly IConversationMessageRepository _messageRepository;
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IOcrResultRepository _ocrResultRepository;
    private readonly IActivityLogRepository _activityLogRepository;
    private readonly IPriceAdjustmentRepository _priceAdjustmentRepository;

    public LocalWorkbenchTaskService(
        ICustomerRepository customerRepository,
        IOrderRepository orderRepository,
        IDealRepository dealRepository,
        IFollowUpRepository followUpRepository,
        IConversationMessageRepository messageRepository,
        IAiSuggestionRepository suggestionRepository,
        IOcrResultRepository ocrResultRepository,
        IActivityLogRepository activityLogRepository,
        IPriceAdjustmentRepository priceAdjustmentRepository)
    {
        _customerRepository = customerRepository;
        _orderRepository = orderRepository;
        _dealRepository = dealRepository;
        _followUpRepository = followUpRepository;
        _messageRepository = messageRepository;
        _suggestionRepository = suggestionRepository;
        _ocrResultRepository = ocrResultRepository;
        _activityLogRepository = activityLogRepository;
        _priceAdjustmentRepository = priceAdjustmentRepository;
    }

    public async Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        var customers = (await _customerRepository.GetAllAsync(cancellationToken)).ToList();
        var orders = (await _orderRepository.GetRecentAsync(cancellationToken)).ToList();
        var deals = (await _dealRepository.ListAsync(cancellationToken)).ToList();
        var followUps = (await _followUpRepository.ListAsync(cancellationToken)).ToList();
        var messages = (await _messageRepository.ListAsync(cancellationToken)).ToList();
        var suggestions = (await _suggestionRepository.ListAsync(cancellationToken)).ToList();
        var ocrResults = (await _ocrResultRepository.ListAsync(cancellationToken)).ToList();
        var activities = (await _activityLogRepository.ListAsync(cancellationToken)).ToList();
        var priceAdjustments = (await _priceAdjustmentRepository.ListAsync(cancellationToken)).ToList();

        var customerMap = customers.ToDictionary(customer => customer.Id);
        var orderMap = orders.ToDictionary(order => order.Id);
        var today = DateTime.Today;

        var tasks = new List<WorkbenchTask>();
        tasks.AddRange(BuildFollowUpTasks(followUps, customerMap, orderMap, today));
        tasks.AddRange(BuildDraftTasks(suggestions, customerMap, orderMap));
        tasks.AddRange(BuildReplyNeededTasks(messages, suggestions, activities, customerMap, orderMap));
        tasks.AddRange(BuildAiSuggestionPendingTasks(suggestions, customerMap, orderMap));
        tasks.AddRange(BuildOcrTasks(ocrResults, customerMap, orderMap));

        var customersWithBlockingTasks = tasks
            .Where(task => task.CustomerId is int && SuppressesRecentlyActive(task.Type))
            .Select(task => task.CustomerId!.Value)
            .ToHashSet();
        tasks.AddRange(BuildRecentlyActiveTasks(customers, messages, activities, orderMap, customersWithBlockingTasks, today));

        ApplyPipelineStages(tasks, customerMap, orders, deals, followUps, messages, suggestions, activities, priceAdjustments);
        return FinalizeTasks(tasks);
    }

    private static IReadOnlyList<WorkbenchTask> FinalizeTasks(IEnumerable<WorkbenchTask> tasks)
    {
        var deduped = tasks
            .GroupBy(task => string.IsNullOrWhiteSpace(task.DedupeKey) ? task.Id : task.DedupeKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(task => task, WorkbenchTaskComparer.Instance).First())
            .ToList();

        var customersWithBlockingTasks = deduped
            .Where(task => task.CustomerId is int && SuppressesRecentlyActive(task.Type))
            .Select(task => task.CustomerId!.Value)
            .ToHashSet();

        deduped.RemoveAll(task =>
            task.Type == WorkbenchTaskType.RecentlyActiveCustomer &&
            task.CustomerId is int customerId &&
            customersWithBlockingTasks.Contains(customerId));

        var keepRecentIds = deduped
            .Where(task => task.Type == WorkbenchTaskType.RecentlyActiveCustomer)
            .OrderBy(task => task, WorkbenchTaskComparer.Instance)
            .Take(RecentlyActiveLimit)
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);

        deduped.RemoveAll(task =>
            task.Type == WorkbenchTaskType.RecentlyActiveCustomer &&
            !keepRecentIds.Contains(task.Id));

        return deduped
            .OrderBy(task => task, WorkbenchTaskComparer.Instance)
            .ToList();
    }

    private static IEnumerable<WorkbenchTask> BuildFollowUpTasks(
        IEnumerable<FollowUp> followUps,
        IReadOnlyDictionary<int, Customer> customerMap,
        IReadOnlyDictionary<int, MerchantOrder> orderMap,
        DateTime today)
    {
        foreach (var followUp in followUps)
        {
            if (!IsOpenFollowUp(followUp))
            {
                continue;
            }

            var type = followUp.ScheduledAt.Date < today
                ? WorkbenchTaskType.FollowUpOverdue
                : followUp.ScheduledAt.Date == today
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
                targetSection: TargetSectionFollowUp,
                actionHint: ActionHintCompleteFollowUp,
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
            var isCopiedDraft = string.Equals(autoReplyState, "copied", StringComparison.OrdinalIgnoreCase);
            var isPreparedDraft = suggestion.Status == AiSuggestionStatus.DraftPrepared
                || string.Equals(autoReplyState, "prepared", StringComparison.OrdinalIgnoreCase)
                || isCopiedDraft;
            var isSent = suggestion.Status == AiSuggestionStatus.Sent
                || string.Equals(autoReplyState, "sent", StringComparison.OrdinalIgnoreCase);

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
                targetSection: TargetSectionAiSuggestion,
                actionHint: isCopiedDraft ? ActionHintReplyToCustomer : ActionHintReviewDraft,
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
                targetSection: TargetSectionConversation,
                actionHint: ActionHintReplyToCustomer,
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
                targetSection: TargetSectionAiSuggestion,
                actionHint: ActionHintReviewSuggestion,
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
                targetSection: TargetSectionOcr,
                actionHint: ActionHintConvertOcr,
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
                    TargetSection: recentActivity.Type == ActivityType.ConversationMessageAdded ? TargetSectionConversation : TargetSectionCustomer,
                    ActionHint: recentActivity.Type == ActivityType.ConversationMessageAdded ? ActionHintReplyToCustomer : string.Empty,
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
                    TargetSection: TargetSectionConversation,
                    ActionHint: ActionHintReplyToCustomer,
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
                    TargetSection: TargetSectionCustomer,
                    ActionHint: string.Empty,
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

    private static string BuildDefaultDedupeKey(WorkbenchTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.RelatedEntityType) && task.RelatedEntityId is int relatedEntityId)
        {
            return $"{task.Type}:{task.RelatedEntityType}:{relatedEntityId}";
        }

        return $"{task.Type}:{task.CustomerId?.ToString() ?? "none"}:{task.OrderId?.ToString() ?? "none"}";
    }

    private static bool IsOpenFollowUp(FollowUp followUp)
    {
        return followUp.Status is FollowUpStatus.Pending or FollowUpStatus.InProgress or FollowUpStatus.Overdue
            && followUp.CompletedAt is null;
    }

    private static bool IsAfter(DateTime leftTime, int leftId, DateTime rightTime, int rightId)
    {
        return leftTime > rightTime || leftTime == rightTime && leftId > rightId;
    }

    private static bool SuppressesRecentlyActive(WorkbenchTaskType type)
    {
        return RecentlyActiveBlockedTypes.Contains(type);
    }

    private static int GetSortRank(WorkbenchTask task)
    {
        return task.Type switch
        {
            WorkbenchTaskType.FollowUpOverdue => 1,
            WorkbenchTaskType.DraftNotSent when string.Equals(task.ActionHint, ActionHintReplyToCustomer, StringComparison.Ordinal) => 2,
            WorkbenchTaskType.ReplyNeeded => 3,
            WorkbenchTaskType.DraftNotSent => 4,
            WorkbenchTaskType.AiSuggestionPending => 5,
            WorkbenchTaskType.OcrNotConverted => 6,
            WorkbenchTaskType.FollowUpToday => 7,
            WorkbenchTaskType.RecentlyActiveCustomer => 8,
            _ => 99
        };
    }

    private static long BuildSortKey(WorkbenchTask task)
    {
        var rank = GetSortRank(task);
        var epochSeconds = new DateTimeOffset(task.OccurredAt).ToUnixTimeSeconds();
        var reverseSeconds = 9_999_999_999L - Math.Clamp(epochSeconds, 0L, 9_999_999_999L);
        return (rank * 10_000_000_000L) + reverseSeconds;
    }

    private static string GetTitleOrDefault(string? value, string? fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback ?? string.Empty
            : value.Trim();
    }

    private static string TrimPreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "暂无摘要";
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 48
            ? normalized
            : $"{normalized[..48]}...";
    }

    private static void ApplyPipelineStages(
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
        var customerOrders = orders.GroupBy(order => order.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<MerchantOrder>)group.ToList());
        var customerDeals = deals.GroupBy(deal => deal.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<Deal>)group.ToList());
        var customerFollowUps = followUps.GroupBy(followUp => followUp.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<FollowUp>)group.ToList());
        var customerMessages = messages.GroupBy(message => message.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<ConversationMessage>)group.ToList());
        var customerSuggestions = suggestions.GroupBy(suggestion => suggestion.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<AiSuggestion>)group.ToList());
        var customerActivities = activities.Where(activity => activity.CustomerId is int)
            .GroupBy(activity => activity.CustomerId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ActivityLog>)group.ToList());
        var customerAdjustments = priceAdjustments.GroupBy(adjustment => adjustment.CustomerId).ToDictionary(group => group.Key, group => (IReadOnlyList<PriceAdjustment>)group.ToList());

        foreach (var task in tasks)
        {
            if (task.CustomerId is not int customerId || !customerMap.TryGetValue(customerId, out var customer))
            {
                continue;
            }

            customerOrders.TryGetValue(customerId, out var scopedOrders);
            customerDeals.TryGetValue(customerId, out var scopedDeals);
            customerFollowUps.TryGetValue(customerId, out var scopedFollowUps);
            customerMessages.TryGetValue(customerId, out var scopedMessages);
            customerSuggestions.TryGetValue(customerId, out var scopedSuggestions);
            customerActivities.TryGetValue(customerId, out var scopedActivities);
            customerAdjustments.TryGetValue(customerId, out var scopedAdjustments);

            var context = new PipelineStageResolutionContext
            {
                Customer = customer,
                OrderId = task.OrderId,
                Order = task.OrderId is int orderId ? scopedOrders?.FirstOrDefault(order => order.Id == orderId) : null,
                Orders = scopedOrders ?? Array.Empty<MerchantOrder>(),
                Deals = scopedDeals ?? Array.Empty<Deal>(),
                Messages = scopedMessages ?? Array.Empty<ConversationMessage>(),
                Suggestions = scopedSuggestions ?? Array.Empty<AiSuggestion>(),
                FollowUps = scopedFollowUps ?? Array.Empty<FollowUp>(),
                Activities = scopedActivities ?? Array.Empty<ActivityLog>(),
                PriceAdjustments = scopedAdjustments ?? Array.Empty<PriceAdjustment>()
            };

            task.PipelineStage = PipelineStageRuleEngine.Resolve(context).Stage;
        }
    }

    private sealed class WorkbenchTaskComparer : IComparer<WorkbenchTask>
    {
        public static WorkbenchTaskComparer Instance { get; } = new();

        public int Compare(WorkbenchTask? left, WorkbenchTask? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var rankComparison = GetSortRank(left).CompareTo(GetSortRank(right));
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            var occurredAtComparison = right.OccurredAt.CompareTo(left.OccurredAt);
            if (occurredAtComparison != 0)
            {
                return occurredAtComparison;
            }

            var priorityComparison = right.Priority.CompareTo(left.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            var customerComparison = CompareNullableDescending(left.CustomerId, right.CustomerId);
            if (customerComparison != 0)
            {
                return customerComparison;
            }

            var orderComparison = CompareNullableDescending(left.OrderId, right.OrderId);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            var relatedEntityComparison = CompareNullableDescending(left.RelatedEntityId, right.RelatedEntityId);
            if (relatedEntityComparison != 0)
            {
                return relatedEntityComparison;
            }

            return StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static int CompareNullableDescending(int? left, int? right)
        {
            return Nullable.Compare(right, left);
        }
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
