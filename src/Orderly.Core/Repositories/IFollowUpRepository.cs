using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface IFollowUpRepository
{
    Task<FollowUp> CreateAsync(FollowUp followUp, CancellationToken cancellationToken = default);
    Task<FollowUp?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FollowUp>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FollowUp>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FollowUp>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(FollowUp followUp, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
