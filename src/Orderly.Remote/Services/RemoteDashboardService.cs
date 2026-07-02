using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteDashboardService : IDashboardService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteDashboardService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var dto = await _client.GetAsync<CloudDashboardDto>($"api/workspaces/{_session.WorkspaceId:N}/dashboard", cancellationToken);
        if (dto == null)
            return new DashboardSnapshot { AsOfUtc = asOfUtc, Metrics = new DashboardMetrics(), Trend = Array.Empty<DashboardTrendPoint>() };
        return dto.ToSnapshot();
    }

    public Task<IReadOnlyList<BusinessMetricSnapshot>> PersistMetricSnapshotsAsync(Guid workspaceId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        // Snapshots are persisted on the server during dashboard refresh; client does not need to persist.
        return Task.FromResult<IReadOnlyList<BusinessMetricSnapshot>>(Array.Empty<BusinessMetricSnapshot>());
    }
}
