using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed partial class LocalWorkbenchTaskService : IWorkbenchTaskService
{
    private const int RecentlyActiveWindowDays = 7;
    private const int RecentlyActiveLimit = 5;

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
        return await GetTasksAsync(new WorkbenchTaskQuery(), cancellationToken);
    }

    public async Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskFilter filter, CancellationToken cancellationToken = default)
    {
        return await GetTasksAsync(new WorkbenchTaskQuery
        {
            Filter = filter ?? new WorkbenchTaskFilter()
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskQuery query, CancellationToken cancellationToken = default)
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

        ProjectionPipelineStageHelper.ApplyToWorkbenchTasks(tasks, customerMap, orders, deals, followUps, messages, suggestions, activities, priceAdjustments);
        var finalized = FinalizeTasks(tasks)
            .Select(AttachQuickActions)
            .ToList();
        return ApplyFilter(finalized, query);
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

    private static string BuildDefaultDedupeKey(WorkbenchTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.RelatedEntityType) && task.RelatedEntityId is int relatedEntityId)
        {
            return $"{task.Type}:{task.RelatedEntityType}:{relatedEntityId}";
        }

        return $"{task.Type}:{task.CustomerId?.ToString() ?? "none"}:{task.OrderId?.ToString() ?? "none"}";
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
            WorkbenchTaskType.DraftNotSent when task.Priority == WorkbenchTaskPriority.Critical => 2,
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

    private static WorkbenchTask AttachQuickActions(WorkbenchTask task)
    {
        task.QuickActions = QuickActionProjectionBuilder.BuildForWorkbenchTask(task);
        return task;
    }

    private static IReadOnlyList<WorkbenchTask> ApplyFilter(IReadOnlyList<WorkbenchTask> tasks, WorkbenchTaskQuery? query)
    {
        if (query is null)
        {
            return tasks;
        }

        IEnumerable<WorkbenchTask> filtered = tasks;
        var filter = query.Filter ?? new WorkbenchTaskFilter();

        if (filter.TaskType is WorkbenchTaskType taskType)
        {
            filtered = filtered.Where(task => task.Type == taskType);
        }

        if (filter.Priority is WorkbenchTaskPriority priority)
        {
            filtered = filtered.Where(task => task.Priority == priority);
        }

        if (filter.PipelineStage is PipelineStage stage)
        {
            filtered = filtered.Where(task => task.PipelineStage == stage);
        }

        if (filter.CustomerId is int customerId)
        {
            filtered = filtered.Where(task => task.CustomerId == customerId);
        }

        if (filter.OrderId is int orderId)
        {
            filtered = filtered.Where(task => task.OrderId == orderId);
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetSection))
        {
            filtered = filtered.Where(task => string.Equals(task.TargetSection, filter.TargetSection, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.OnlyActionable == true)
        {
            filtered = filtered.Where(QuickActionProjectionBuilder.HasActionableOperation);
        }

        if (filter.IncludeRecentlyActive == false)
        {
            filtered = filtered.Where(task => task.Type != WorkbenchTaskType.RecentlyActiveCustomer);
        }

        if (filter.OccurredFrom is DateTime occurredFrom)
        {
            filtered = filtered.Where(task => task.OccurredAt >= occurredFrom);
        }

        if (filter.OccurredTo is DateTime occurredTo)
        {
            filtered = filtered.Where(task => task.OccurredAt <= occurredTo);
        }

        if (query.Limit is > 0)
        {
            filtered = filtered.Take(query.Limit.Value);
        }

        return filtered.ToList();
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

    private static string GetTitleOrDefault(string? value, string? fallback)
    {
        return ProjectionTextHelper.GetTitleOrDefault(value, fallback);
    }

    private static string TrimPreview(string? value)
    {
        return ProjectionTextHelper.TrimPreview(value);
    }
}
