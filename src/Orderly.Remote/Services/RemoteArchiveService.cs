using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteArchiveService : IArchiveService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteArchiveService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<IReadOnlyList<ArchivedEntitySummary>> ListAsync(
        string entityType,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync<object>(
            $"api/workspaces/{_session.WorkspaceId:N}/archive/{entityType}?pageSize=200",
            cancellationToken).ConfigureAwait(false);

        if (response is null)
            return Array.Empty<ArchivedEntitySummary>();

        var json = JsonSerializer.Serialize(response);
        var dto = JsonSerializer.Deserialize<ArchiveListResponse>(json);
        if (dto?.Items is null)
            return Array.Empty<ArchivedEntitySummary>();

        return dto.Items.Select(r => new ArchivedEntitySummary(
            r.Id,
            entityType,
            r.Name ?? string.Empty,
            r.ArchivedAtUtc,
            r.ArchivedByDisplayName,
            r.ArchiveReason,
            r.Revision)).ToList();
    }

    public async Task RecoverAsync(
        string entityType,
        Guid entityId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var command = new RecoverCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = expectedRevision
        };

        await _client.PostAsync<RecoverCommand>(
            $"api/workspaces/{_session.WorkspaceId:N}/archive/{entityType}/{entityId:N}/recover",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class ArchiveListResponse
    {
        public List<ArchivedEntityDto> Items { get; set; } = new();
    }
}
