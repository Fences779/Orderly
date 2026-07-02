using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteInventoryService : IInventoryService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteInventoryService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
    }

    public async Task<IReadOnlyList<InventoryMetrics>> GetAllMetricsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        var paged = await _client.GetAsync<PagedList<CloudInventoryItemDto>>($"api/workspaces/{_session.WorkspaceId:N}/inventory/items?pageSize=200", cancellationToken);
        if (paged == null) return Array.Empty<InventoryMetrics>();
        return paged.Items.Select(ToMetrics).ToList();
    }

    public async Task<InventoryMetrics> GetMetricsAsync(Guid inventoryItemId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        // In this stage the server does not yet expose a dedicated metrics endpoint; derive from item.
        var paged = await _client.GetAsync<PagedList<CloudInventoryItemDto>>($"api/workspaces/{_session.WorkspaceId:N}/inventory/items?pageSize=200", cancellationToken);
        var dto = paged?.Items.FirstOrDefault(i => i.Id == inventoryItemId);
        if (dto == null) throw new InvalidOperationException("Inventory item not found.");
        return ToMetrics(dto);
    }

    public Task<IReadOnlyList<BusinessInsight>> GenerateInventoryInsightsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BusinessInsight>>(Array.Empty<BusinessInsight>());

    public Task<InventoryItem> RecordMovementAsync(InventoryMovement movement, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Remote inventory movement is not implemented in this stage.");

    private static InventoryMetrics ToMetrics(CloudInventoryItemDto dto)
    {
        var isLowStock = dto.QuantityAvailable <= dto.ReorderThreshold;
        return new InventoryMetrics
        {
            InventoryItemId = dto.Id,
            QuantityAvailable = dto.QuantityAvailable,
            ReorderThreshold = dto.ReorderThreshold,
            IsLowStock = isLowStock,
            AvgDailyUsage7d = 0m,
            AvgDailyUsage30d = 0m,
            CoverageDays = null,
            ReorderSuggestion = new ReorderSuggestion
            {
                InventoryItemId = dto.Id,
                ShouldReorder = isLowStock,
                SuggestedQuantity = isLowStock ? dto.ReorderThreshold * 2 - dto.QuantityAvailable : 0m
            }
        };
    }
}
