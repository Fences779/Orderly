using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Universal inventory service implementation (Req 4.1, 4.8, 4.9, 4.10). Records inventory movements
/// and applies them to the owning item's available quantity per <see cref="InventoryMovementType"/>
/// (Req 4.8), and computes inventory metrics — low-stock status, fixed 7-day and 30-day average
/// daily usage, <c>CoverageDays</c>, a reorder suggestion, and inventory insights (Req 4.9, 4.10).
///
/// <para><b>Usage definition.</b> Average daily usage is computed from
/// <see cref="InventoryMovementType.Outbound"/> movements only — stock that has left inventory —
/// summed over the fixed window and divided by the window length in days. Inbound movements and
/// adjustment corrections are not counted as usage.</para>
///
/// <para><b>CoverageDays null rule.</b> <c>CoverageDays = QuantityAvailable / AvgDailyUsage30d</c>,
/// reported as <c>null</c> exactly when <c>AvgDailyUsage30d</c> is 0 and never as 0, because 0 would
/// incorrectly indicate no remaining coverage (Req 4.10).</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only through
/// the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceInventoryService : IInventoryService
{
    /// <summary>Category label applied to generated inventory insights.</summary>
    private const string InventoryInsightCategory = "库存";

    /// <summary>Scale (decimal places) the suggested reorder quantity is rounded to for neatness.</summary>
    private const int SuggestionScale = 2;

    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IInventoryMovementRepository _inventoryMovementRepository;

    /// <summary>
    /// Creates the service over the Commerce inventory item and movement repositories.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when either repository is null.</exception>
    public CommerceInventoryService(
        IInventoryItemRepository inventoryItemRepository,
        IInventoryMovementRepository inventoryMovementRepository)
    {
        _inventoryItemRepository = inventoryItemRepository ?? throw new ArgumentNullException(nameof(inventoryItemRepository));
        _inventoryMovementRepository = inventoryMovementRepository ?? throw new ArgumentNullException(nameof(inventoryMovementRepository));
    }

    /// <inheritdoc />
    public async Task<InventoryItem> RecordMovementAsync(
        InventoryMovement movement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        InventoryItem item = await _inventoryItemRepository
            .GetByIdAsync(movement.InventoryItemId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Inventory item '{movement.InventoryItemId}' was not found.");

        // Apply the movement to the item's available quantity according to its type (Req 4.8).
        item.QuantityAvailable += SignedDelta(movement.MovementType, movement.Quantity);

        await _inventoryItemRepository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        await _inventoryMovementRepository.CreateAsync(movement, cancellationToken).ConfigureAwait(false);

        return item;
    }

    /// <inheritdoc />
    public async Task<InventoryMetrics> GetMetricsAsync(
        Guid inventoryItemId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        InventoryItem item = await _inventoryItemRepository
            .GetByIdAsync(inventoryItemId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Inventory item '{inventoryItemId}' was not found.");

        IReadOnlyList<InventoryMovement> movements = await _inventoryMovementRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        List<InventoryMovement> itemMovements = movements
            .Where(movement => movement.InventoryItemId == inventoryItemId)
            .ToList();

        return BuildMetrics(item, itemMovements, asOfUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryMetrics>> GetAllMetricsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InventoryItem> items = await _inventoryItemRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<InventoryMovement> movements = await _inventoryMovementRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        // Group movements by their owning item once so each item's metrics are built from only its
        // own movements (items with no movements still produce metrics with zero usage).
        Dictionary<Guid, List<InventoryMovement>> movementsByItem = movements
            .GroupBy(movement => movement.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var metrics = new List<InventoryMetrics>(items.Count);
        foreach (InventoryItem item in items)
        {
            List<InventoryMovement> itemMovements = movementsByItem.TryGetValue(item.Id, out List<InventoryMovement>? value)
                ? value
                : new List<InventoryMovement>();
            metrics.Add(BuildMetrics(item, itemMovements, asOfUtc));
        }

        return metrics;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BusinessInsight>> GenerateInventoryInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InventoryItem> items = await _inventoryItemRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        var insights = new List<BusinessInsight>();
        foreach (InventoryItem item in items)
        {
            // Out of stock is the more severe condition and takes precedence over low-stock.
            if (item.QuantityAvailable <= 0m)
            {
                insights.Add(BuildInsight(
                    item,
                    asOfUtc,
                    InsightSeverity.Critical,
                    keySuffix: "out-of-stock",
                    title: "库存告罄",
                    message: $"“{item.Name}”当前可用数量为 {Format(item.QuantityAvailable)}，已无可用库存，请尽快补货。"));
            }
            else if (item.QuantityAvailable <= item.ReorderThreshold)
            {
                insights.Add(BuildInsight(
                    item,
                    asOfUtc,
                    InsightSeverity.Warning,
                    keySuffix: "low-stock",
                    title: "库存偏低",
                    message: $"“{item.Name}”当前可用数量为 {Format(item.QuantityAvailable)}，已达到或低于补货阈值 {Format(item.ReorderThreshold)}，建议补货。"));
            }
        }

        return insights;
    }

    /// <summary>
    /// Maps a movement type to the signed delta it applies to the available quantity (Req 4.8):
    /// inbound adds, outbound subtracts, and adjustment applies the (possibly signed) quantity as a
    /// correction.
    /// </summary>
    private static decimal SignedDelta(InventoryMovementType movementType, decimal quantity)
        => movementType switch
        {
            InventoryMovementType.Inbound => quantity,
            InventoryMovementType.Outbound => -quantity,
            InventoryMovementType.Adjustment => quantity,
            _ => throw new ArgumentOutOfRangeException(
                nameof(movementType), movementType, "Unknown inventory movement type."),
        };

    /// <summary>
    /// Builds the metrics read model for one item from its movements as of the supplied instant.
    /// Enforces the CoverageDays null rule (Req 4.10) and produces the reorder suggestion (Req 4.9).
    /// </summary>
    private static InventoryMetrics BuildMetrics(
        InventoryItem item,
        List<InventoryMovement> itemMovements,
        DateTime asOfUtc)
    {
        decimal avgDailyUsage7d = AverageDailyUsage(itemMovements, asOfUtc, IInventoryService.ShortUsageWindowDays);
        decimal avgDailyUsage30d = AverageDailyUsage(itemMovements, asOfUtc, IInventoryService.LongUsageWindowDays);

        // CoverageDays is null exactly when 30-day usage is 0, never 0 (Req 4.10).
        decimal? coverageDays = avgDailyUsage30d == 0m
            ? null
            : item.QuantityAvailable / avgDailyUsage30d;

        bool isLowStock = item.QuantityAvailable <= item.ReorderThreshold;

        return new InventoryMetrics
        {
            InventoryItemId = item.Id,
            QuantityAvailable = item.QuantityAvailable,
            ReorderThreshold = item.ReorderThreshold,
            IsLowStock = isLowStock,
            AvgDailyUsage7d = avgDailyUsage7d,
            AvgDailyUsage30d = avgDailyUsage30d,
            CoverageDays = coverageDays,
            ReorderSuggestion = BuildReorderSuggestion(item, isLowStock, avgDailyUsage30d),
        };
    }

    /// <summary>
    /// Total outbound quantity within the window <c>[asOfUtc - windowDays, asOfUtc]</c> divided by
    /// <paramref name="windowDays"/> (Req 4.9). Only <see cref="InventoryMovementType.Outbound"/>
    /// movements count as usage.
    /// </summary>
    private static decimal AverageDailyUsage(
        List<InventoryMovement> itemMovements,
        DateTime asOfUtc,
        int windowDays)
    {
        DateTime windowStart = asOfUtc.AddDays(-windowDays);

        decimal totalOutbound = itemMovements
            .Where(movement => movement.MovementType == InventoryMovementType.Outbound
                && movement.OccurredAt > windowStart
                && movement.OccurredAt <= asOfUtc)
            .Sum(movement => movement.Quantity);

        return totalOutbound / windowDays;
    }

    /// <summary>
    /// Produces a deterministic reorder suggestion (Req 4.9). When the item is low on stock the
    /// suggested quantity replenishes it to a target level: the greater of its reorder threshold and
    /// the quantity needed to cover the long usage window at the 30-day average usage rate. The
    /// suggestion is zero when no reorder is needed and never negative.
    /// </summary>
    private static ReorderSuggestion BuildReorderSuggestion(
        InventoryItem item,
        bool isLowStock,
        decimal avgDailyUsage30d)
    {
        if (!isLowStock)
        {
            return new ReorderSuggestion
            {
                InventoryItemId = item.Id,
                ShouldReorder = false,
                SuggestedQuantity = 0m,
            };
        }

        decimal coverageTarget = avgDailyUsage30d * IInventoryService.LongUsageWindowDays;
        decimal targetLevel = Math.Max(item.ReorderThreshold, coverageTarget);
        decimal suggested = Math.Max(targetLevel - item.QuantityAvailable, 0m);
        suggested = Math.Round(suggested, SuggestionScale, MidpointRounding.AwayFromZero);

        return new ReorderSuggestion
        {
            InventoryItemId = item.Id,
            ShouldReorder = true,
            SuggestedQuantity = suggested,
        };
    }

    /// <summary>
    /// Builds a generated inventory insight with a stable business key for later idempotent
    /// persistence (Req 4.20, 18.6).
    /// </summary>
    private static BusinessInsight BuildInsight(
        InventoryItem item,
        DateTime asOfUtc,
        InsightSeverity severity,
        string keySuffix,
        string title,
        string message)
        => new()
        {
            WorkspaceId = item.WorkspaceId,
            Severity = severity,
            Title = title,
            Message = message,
            Category = InventoryInsightCategory,
            GeneratedAt = asOfUtc,
            BusinessKey = $"inventory:{keySuffix}:{item.Id}",
        };

    private static string Format(decimal quantity)
        => quantity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
