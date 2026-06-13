namespace Orderly.Core.Commerce.Services;

/// <summary>
/// A reorder suggestion produced by <see cref="IInventoryService"/> for a single
/// <see cref="InventoryItem"/> (Req 4.9). The suggestion is derived from deterministic local rules
/// only — there is no external/LLM dependency — so identical inputs always yield an identical
/// suggestion.
/// </summary>
public sealed record ReorderSuggestion
{
    /// <summary>The inventory item this suggestion describes.</summary>
    public required Guid InventoryItemId { get; init; }

    /// <summary>
    /// <c>true</c> when the item is low on stock (available quantity ≤ reorder threshold) and a
    /// replenishment is advised; <c>false</c> when no reorder is needed.
    /// </summary>
    public bool ShouldReorder { get; init; }

    /// <summary>
    /// The suggested quantity to reorder so the item is replenished to its target level. Zero when
    /// <see cref="ShouldReorder"/> is <c>false</c>. Never negative.
    /// </summary>
    public decimal SuggestedQuantity { get; init; }
}

/// <summary>
/// The computed inventory metrics for a single <see cref="InventoryItem"/> as of a supplied instant
/// (Req 4.9, 4.10). All values are derived from the item's current available quantity, its reorder
/// threshold, and its recorded outbound movements over the fixed 7-day and 30-day usage windows.
/// </summary>
public sealed record InventoryMetrics
{
    /// <summary>The inventory item these metrics describe.</summary>
    public required Guid InventoryItemId { get; init; }

    /// <summary>The item's available on-hand quantity at evaluation time.</summary>
    public decimal QuantityAvailable { get; init; }

    /// <summary>The item's reorder threshold at evaluation time.</summary>
    public decimal ReorderThreshold { get; init; }

    /// <summary>
    /// <c>true</c> when the available quantity is less than or equal to the reorder threshold
    /// (Req 4.9).
    /// </summary>
    public bool IsLowStock { get; init; }

    /// <summary>
    /// Average daily usage over the fixed 7-day window: total outbound quantity in the last 7 days
    /// divided by 7 (Req 4.9).
    /// </summary>
    public decimal AvgDailyUsage7d { get; init; }

    /// <summary>
    /// Average daily usage over the fixed 30-day window: total outbound quantity in the last 30 days
    /// divided by 30 (Req 4.9). Used as the denominator when computing <see cref="CoverageDays"/>.
    /// </summary>
    public decimal AvgDailyUsage30d { get; init; }

    /// <summary>
    /// Estimated days of remaining coverage, computed as
    /// <see cref="QuantityAvailable"/> / <see cref="AvgDailyUsage30d"/> (Req 4.9). Reported as
    /// <c>null</c> (unavailable / not computable) exactly when <see cref="AvgDailyUsage30d"/> is 0,
    /// and never reported as 0, because a value of 0 would incorrectly indicate no remaining
    /// coverage (Req 4.10).
    /// </summary>
    public decimal? CoverageDays { get; init; }

    /// <summary>The reorder suggestion derived from these metrics (Req 4.9).</summary>
    public required ReorderSuggestion ReorderSuggestion { get; init; }
}

/// <summary>
/// Universal inventory service (Req 4.1). Records inventory movements and applies them to the
/// owning item's available quantity according to the <see cref="InventoryMovementType"/> (Req 4.8),
/// and computes inventory metrics — low-stock status, fixed 7-day and 30-day average daily usage,
/// <c>CoverageDays</c>, a reorder suggestion, and inventory insights (Req 4.9, 4.10).
///
/// <para>All metric calls take an explicit <c>asOfUtc</c> instant so the usage windows are
/// deterministic and the results are reproducible with no hidden wall-clock dependency.</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public interface IInventoryService
{
    /// <summary>The fixed short usage window, in days, used for <see cref="InventoryMetrics.AvgDailyUsage7d"/>.</summary>
    const int ShortUsageWindowDays = 7;

    /// <summary>The fixed long usage window, in days, used for <see cref="InventoryMetrics.AvgDailyUsage30d"/> and coverage.</summary>
    const int LongUsageWindowDays = 30;

    /// <summary>
    /// Records an inventory movement and updates the owning item's available quantity according to
    /// the movement's <see cref="InventoryMovementType"/> (Req 4.8): an
    /// <see cref="InventoryMovementType.Inbound"/> movement increases the quantity by the movement
    /// quantity, an <see cref="InventoryMovementType.Outbound"/> movement decreases it, and an
    /// <see cref="InventoryMovementType.Adjustment"/> applies the movement quantity as a signed
    /// correction (positive increases, negative decreases). The movement is persisted and the
    /// updated item is returned.
    /// </summary>
    /// <param name="movement">The movement to record. Its <see cref="InventoryMovement.InventoryItemId"/> must reference an existing active item.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated <see cref="InventoryItem"/> with its new available quantity.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="movement"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the referenced inventory item does not exist.</exception>
    Task<InventoryItem> RecordMovementAsync(
        InventoryMovement movement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the inventory metrics for a single item as of <paramref name="asOfUtc"/>
    /// (Req 4.9, 4.10).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the inventory item does not exist.</exception>
    Task<InventoryMetrics> GetMetricsAsync(
        Guid inventoryItemId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the inventory metrics for every active inventory item as of
    /// <paramref name="asOfUtc"/> (Req 4.9, 4.10).
    /// </summary>
    Task<IReadOnlyList<InventoryMetrics>> GetAllMetricsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates inventory insights from deterministic local rules only (Req 4.9, 4.14): a critical
    /// insight for items that are out of stock and a warning insight for items that are low on stock.
    /// Each insight carries a stable <see cref="BusinessInsight.BusinessKey"/> so later idempotent
    /// persistence produces no duplicates (Req 4.20, 18.6). Insights are returned, not persisted, by
    /// this slice.
    /// </summary>
    Task<IReadOnlyList<BusinessInsight>> GenerateInventoryInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}
