using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class FollowUpService : IFollowUpService
{
    private const int MaxFollowUpTitleCharacters = 120;
    private const int MaxFollowUpContentCharacters = 2000;
    private const int ActivityDescriptionCharacters = 120;

    private static readonly DateTime MinFollowUpDate = new(2000, 1, 1);
    private static readonly DateTime MaxFollowUpDate = new(2100, 1, 1);

    private readonly IFollowUpRepository _followUpRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public FollowUpService(IFollowUpRepository followUpRepository, IActivityLogRepository activityLogRepository)
    {
        _followUpRepository = followUpRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<FollowUp>> GetFollowUpsAsync(CancellationToken cancellationToken = default)
    {
        return _followUpRepository.ListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<FollowUp>> GetPendingFollowUpsAsync(CancellationToken cancellationToken = default)
    {
        return _followUpRepository.ListPendingAsync(cancellationToken);
    }

    public Task<IReadOnlyList<FollowUp>> GetCustomerFollowUpsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _followUpRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<FollowUp> SaveFollowUpAsync(FollowUp followUp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUp);

        NormalizeFollowUp(followUp);
        if (followUp.Id <= 0)
        {
            var created = await _followUpRepository.CreateAsync(followUp, cancellationToken);
            await AddActivityAsync(ActivityType.FollowUpCreated, created.CustomerId, created.DealId, created.OrderId, "新增跟进", created.Title, cancellationToken);
            return created;
        }

        await _followUpRepository.UpdateAsync(followUp, cancellationToken);
        return followUp;
    }

    public async Task CompleteFollowUpAsync(int id, DateTime completedAt, CancellationToken cancellationToken = default)
    {
        EnsureDateInRange(completedAt, "跟进完成时间");
        var followUp = await _followUpRepository.GetByIdAsync(id, cancellationToken);
        if (followUp is null || !FollowUpStatusHelper.CanTransition(followUp.Status))
        {
            return;
        }

        followUp.Status = FollowUpStatus.Completed;
        followUp.CompletedAt = completedAt;
        await _followUpRepository.UpdateAsync(followUp, cancellationToken);
        await AddActivityAsync(ActivityType.FollowUpCompleted, followUp.CustomerId, followUp.DealId, followUp.OrderId, "完成跟进", followUp.Title, cancellationToken);
    }

    public async Task SnoozeFollowUpAsync(int id, DateTime scheduledAt, CancellationToken cancellationToken = default)
    {
        EnsureDateInRange(scheduledAt, "跟进计划时间");
        var followUp = await _followUpRepository.GetByIdAsync(id, cancellationToken);
        if (followUp is null || !FollowUpStatusHelper.CanTransition(followUp.Status) || followUp.ScheduledAt == scheduledAt)
        {
            return;
        }

        var oldScheduledAt = followUp.ScheduledAt;
        followUp.Status = FollowUpStatus.Pending;
        followUp.ScheduledAt = scheduledAt;
        followUp.CompletedAt = null;
        await _followUpRepository.UpdateAsync(followUp, cancellationToken);
        await AddActivityAsync(
            ActivityType.FollowUpSnoozed,
            followUp.CustomerId,
            followUp.DealId,
            followUp.OrderId,
            "延期跟进",
            $"{followUp.Title}：{oldScheduledAt:yyyy-MM-dd HH:mm} -> {scheduledAt:yyyy-MM-dd HH:mm}",
            cancellationToken);
    }

    public async Task CancelFollowUpAsync(int id, CancellationToken cancellationToken = default)
    {
        var followUp = await _followUpRepository.GetByIdAsync(id, cancellationToken);
        if (followUp is null || !FollowUpStatusHelper.CanTransition(followUp.Status))
        {
            return;
        }

        followUp.Status = FollowUpStatus.Cancelled;
        await _followUpRepository.UpdateAsync(followUp, cancellationToken);
        await AddActivityAsync(ActivityType.FollowUpCancelled, followUp.CustomerId, followUp.DealId, followUp.OrderId, "取消跟进", followUp.Title, cancellationToken);
    }

    private Task AddActivityAsync(ActivityType type, int? customerId, int? dealId, int? orderId, string title, string description, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = customerId,
            DealId = dealId,
            OrderId = orderId,
            Title = title,
            Description = BuildActivityDescription(description),
            Operator = "local"
        }, cancellationToken);
    }

    private static void NormalizeFollowUp(FollowUp followUp)
    {
        if (followUp.CustomerId <= 0)
        {
            throw new InvalidOperationException("跟进缺少有效客户。");
        }

        if (!Enum.IsDefined(followUp.Status))
        {
            throw new InvalidOperationException("跟进状态无效。");
        }

        EnsureDateInRange(followUp.ScheduledAt, "跟进计划时间");
        if (followUp.CompletedAt is DateTime completedAt)
        {
            EnsureDateInRange(completedAt, "跟进完成时间");
        }

        if (followUp.ReminderAt is DateTime reminderAt)
        {
            EnsureDateInRange(reminderAt, "跟进提醒时间");
        }

        followUp.Title = NormalizeRequiredText(followUp.Title, MaxFollowUpTitleCharacters, "跟进标题", allowLineBreaks: false);
        followUp.Content = NormalizeOptionalText(followUp.Content, MaxFollowUpContentCharacters, "跟进内容", allowLineBreaks: true);
    }

    private static void EnsureDateInRange(DateTime value, string fieldName)
    {
        if (value < MinFollowUpDate || value > MaxFollowUpDate)
        {
            throw new InvalidOperationException($"{fieldName}超出允许范围。");
        }
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName, allowLineBreaks);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => char.IsControl(ch) && !(allowLineBreaks && ch is '\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static string BuildActivityDescription(string description)
    {
        var singleLine = new string((description ?? string.Empty)
            .Select(static ch => ch is '\r' or '\n' or '\t' ? ' ' : ch)
            .ToArray())
            .Trim();

        return singleLine.Length <= ActivityDescriptionCharacters
            ? singleLine
            : $"{singleLine[..ActivityDescriptionCharacters]}...";
    }
}
