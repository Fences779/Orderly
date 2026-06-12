using Orderly.Core.Models;
using Orderly.Core.Repositories;

namespace Orderly.Tests.Fakes;

/// <summary>
/// 简单的内存账户仓储，用于在不触碰磁盘/SQLite 的前提下驱动
/// <see cref="Orderly.Data.Services.LocalAccountManagementService"/> 的目录读取路径。
/// </summary>
internal sealed class InMemoryLocalAccountRepository : ILocalAccountRepository
{
    private readonly List<LocalAccount> _accounts;

    public InMemoryLocalAccountRepository(IEnumerable<LocalAccount> accounts)
    {
        _accounts = accounts.ToList();
    }

    public int ListCallCount { get; private set; }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.Count);

    public Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        return Task.FromResult<IReadOnlyList<LocalAccount>>(_accounts.ToList());
    }

    public Task<LocalAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.FirstOrDefault(a =>
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)));

    public Task<LocalAccount?> GetByAccountIdAsync(string accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase)));

    public Task CreateAsync(LocalAccount account, CancellationToken cancellationToken = default)
    {
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(LocalAccount account, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        _accounts.RemoveAll(a => string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }
}
