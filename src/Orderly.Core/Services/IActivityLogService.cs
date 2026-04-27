using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IActivityLogService
{
    Task<IReadOnlyList<ActivityLog>> GetRecentActivitiesAsync(int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> GetCustomerActivitiesAsync(int customerId, CancellationToken cancellationToken = default);
    Task<ActivityLog> AddActivityAsync(ActivityLog activityLog, CancellationToken cancellationToken = default);
}
