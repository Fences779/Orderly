using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class FollowUpService : IFollowUpService
{
    private readonly IFollowUpRepository _followUpRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public FollowUpService(IFollowUpRepository followUpRepository, IActivityLogRepository activityLogRepository)
    {
        _followUpRepository = followUpRepository;
        _activityLogRepository = activityLogRepository;
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
        var followUp = await _followUpRepository.GetByIdAsync(id, cancellationToken);
        if (followUp is null)
        {
            return;
        }

        followUp.Status = FollowUpStatus.Completed;
        followUp.CompletedAt = completedAt;
        await _followUpRepository.UpdateAsync(followUp, cancellationToken);
        await AddActivityAsync(ActivityType.FollowUpCompleted, followUp.CustomerId, followUp.DealId, followUp.OrderId, "完成跟进", followUp.Title, cancellationToken);
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
            Description = description,
            Operator = "local"
        }, cancellationToken);
    }
}
