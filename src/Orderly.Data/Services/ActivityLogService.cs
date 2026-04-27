using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class ActivityLogService : IActivityLogService
{
    private readonly IActivityLogRepository _activityLogRepository;

    public ActivityLogService(IActivityLogRepository activityLogRepository)
    {
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<ActivityLog>> GetRecentActivitiesAsync(int count, CancellationToken cancellationToken = default)
    {
        return _activityLogRepository.ListRecentAsync(count, cancellationToken);
    }

    public Task<IReadOnlyList<ActivityLog>> GetCustomerActivitiesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return _activityLogRepository.ListByCustomerIdAsync(customerId, cancellationToken);
    }

    public Task<ActivityLog> AddActivityAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        return _activityLogRepository.CreateAsync(activityLog, cancellationToken);
    }
}
