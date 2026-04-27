using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IFollowUpService
{
    Task<IReadOnlyList<FollowUp>> GetFollowUpsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FollowUp>> GetPendingFollowUpsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FollowUp>> GetCustomerFollowUpsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<FollowUp> SaveFollowUpAsync(FollowUp followUp, CancellationToken cancellationToken = default);
    Task CompleteFollowUpAsync(int id, DateTime completedAt, CancellationToken cancellationToken = default);
    Task SnoozeFollowUpAsync(int id, DateTime scheduledAt, CancellationToken cancellationToken = default);
    Task CancelFollowUpAsync(int id, CancellationToken cancellationToken = default);
}
