using Orderly.Contracts.Commerce;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public interface IRemoteImportService
{
    Task<LocalImportDryRunResponse> DryRunAsync(LocalImportDryRunRequest request, CancellationToken cancellationToken = default);
    Task<LocalImportCommitResponse> CommitAsync(LocalImportCommitRequest request, CancellationToken cancellationToken = default);
    Task<LocalImportBatchStatusDto?> GetBatchStatusAsync(Guid batchId, CancellationToken cancellationToken = default);
}

public sealed class RemoteImportService : IRemoteImportService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteImportService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public Task<LocalImportDryRunResponse> DryRunAsync(LocalImportDryRunRequest request, CancellationToken cancellationToken = default)
        => _client.PostAsync<LocalImportDryRunRequest, LocalImportDryRunResponse>(
            $"api/workspaces/{_session.WorkspaceId:N}/import/dry-run",
            request,
            cancellationToken);

    public Task<LocalImportCommitResponse> CommitAsync(LocalImportCommitRequest request, CancellationToken cancellationToken = default)
        => _client.PostAsync<LocalImportCommitRequest, LocalImportCommitResponse>(
            $"api/workspaces/{_session.WorkspaceId:N}/import/commit",
            request,
            cancellationToken);

    public Task<LocalImportBatchStatusDto?> GetBatchStatusAsync(Guid batchId, CancellationToken cancellationToken = default)
        => _client.GetAsync<LocalImportBatchStatusDto?>(
            $"api/workspaces/{_session.WorkspaceId:N}/import/batches/{batchId:N}",
            cancellationToken);
}
