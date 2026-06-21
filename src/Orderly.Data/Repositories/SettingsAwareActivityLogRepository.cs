using Orderly.Core.Models;
using Orderly.Core.Repositories;

namespace Orderly.Data.Repositories;

/// <summary>
/// Applies the user-facing operation-log switch at the repository boundary so existing
/// business services do not need parallel logging branches.
/// </summary>
public sealed class SettingsAwareActivityLogRepository : IActivityLogRepository
{
    private readonly IActivityLogRepository _inner;
    private readonly IAppSettingRepository _settings;

    public SettingsAwareActivityLogRepository(IActivityLogRepository inner, IAppSettingRepository settings)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<ActivityLog> CreateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityLog);

        var preferences = await _settings.GetPreferencesAsync(cancellationToken);
        return preferences.OperationLogEnabled
            ? await _inner.CreateAsync(activityLog, cancellationToken)
            : activityLog;
    }

    public Task<ActivityLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<ActivityLog>> ListAsync(CancellationToken cancellationToken = default)
        => _inner.ListAsync(cancellationToken);

    public Task<IReadOnlyList<ActivityLog>> ListRecentAsync(int count, CancellationToken cancellationToken = default)
        => _inner.ListRecentAsync(count, cancellationToken);

    public Task<IReadOnlyList<ActivityLog>> ListByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
        => _inner.ListByCustomerIdAsync(customerId, cancellationToken);

    public Task<int> SoftDeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        => _inner.SoftDeleteOlderThanAsync(cutoff, cancellationToken);

    public Task UpdateAsync(ActivityLog activityLog, CancellationToken cancellationToken = default)
        => _inner.UpdateAsync(activityLog, cancellationToken);

    public Task SoftDeleteAsync(int id, CancellationToken cancellationToken = default)
        => _inner.SoftDeleteAsync(id, cancellationToken);
}
