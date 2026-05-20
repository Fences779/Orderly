using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface ILocalAccountRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken = default);
    Task<LocalAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<LocalAccount?> GetByAccountIdAsync(string accountId, CancellationToken cancellationToken = default);
    Task CreateAsync(LocalAccount account, CancellationToken cancellationToken = default);
    Task UpdateAsync(LocalAccount account, CancellationToken cancellationToken = default);
    Task DeleteAsync(string accountId, CancellationToken cancellationToken = default);
}
