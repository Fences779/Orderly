using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IActivityLogRepository
{
    Task<ActivityLog> CreateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default);
    Task<ActivityLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> ListRecentAsync(int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
