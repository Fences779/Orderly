using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>
/// One row of the Inventory page: an item joined with its computed <see cref="InventoryMetrics"/>.
/// </summary>
public sealed record InventoryRow(
    Guid InventoryItemId,
    string Name,
    string? Sku,
    decimal QuantityAvailable,
    decimal ReorderThreshold,
    bool IsLowStock,
    decimal? CoverageDays,
    bool ShouldReorder,
    decimal SuggestedReorderQuantity);

/// <summary>
/// Dedicated ViewModel for the Inventory (库存) page. Inventory metrics (low-stock, coverage,
/// reorder suggestions) are sourced from <see cref="IInventoryService"/> and item descriptors from
/// the Commerce <see cref="IInventoryItemRepository"/> (Req 6.5, 7.3); no legacy remote inventory
/// gateway is invoked.
/// </summary>
public sealed partial class InventoryPageViewModel : CommercePageViewModel
{
    private readonly IInventoryService _inventoryService;
    private readonly IInventoryItemRepository _inventoryItemRepository;

    /// <summary>Creates the Inventory page ViewModel over the inventory service and item repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public InventoryPageViewModel(
        IInventoryService inventoryService,
        IInventoryItemRepository inventoryItemRepository)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _inventoryItemRepository = inventoryItemRepository ?? throw new ArgumentNullException(nameof(inventoryItemRepository));
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionInventory;

    /// <summary>The active inventory items with their computed metrics.</summary>
    public ObservableCollection<InventoryRow> Items { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Items.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        DateTime asOfUtc = DateTime.UtcNow;

        // The DB reads run off the UI thread: Microsoft.Data.Sqlite's *Async APIs complete
        // synchronously, so awaiting them inline on the navigation path would block the message pump
        // and freeze the shell. Both reads are issued inside a single Task.Run (fewer thread hops);
        // the continuation resumes on the UI thread (ConfigureAwait(true)) where the
        // ObservableCollection update is safe.
        (IReadOnlyList<InventoryItem> items, IReadOnlyList<InventoryMetrics> metrics) = await Task
            .Run(
                async () =>
                {
                    IReadOnlyList<InventoryItem> loadedItems = await _inventoryItemRepository
                        .GetAllAsync(cancellationToken)
                        .ConfigureAwait(false);
                    IReadOnlyList<InventoryMetrics> loadedMetrics = await _inventoryService
                        .GetAllMetricsAsync(asOfUtc, cancellationToken)
                        .ConfigureAwait(false);
                    return (loadedItems, loadedMetrics);
                },
                cancellationToken)
            .ConfigureAwait(true);

        Dictionary<Guid, InventoryMetrics> metricsById = metrics.ToDictionary(m => m.InventoryItemId);

        Items.Clear();
        foreach (InventoryItem item in items)
        {
            metricsById.TryGetValue(item.Id, out InventoryMetrics? metric);
            Items.Add(new InventoryRow(
                item.Id,
                item.Name,
                item.Sku,
                item.QuantityAvailable,
                item.ReorderThreshold,
                metric?.IsLowStock ?? false,
                metric?.CoverageDays,
                metric?.ReorderSuggestion.ShouldReorder ?? false,
                metric?.ReorderSuggestion.SuggestedQuantity ?? 0m));
        }

        NotifyEmptyStateChanged();
    }
}
