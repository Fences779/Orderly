namespace Orderly.Core.Commerce.Services;

/// <summary>
/// The aggregate (point-in-time) metrics portion of a <see cref="DashboardSnapshot"/> (Req 4.13).
/// Every value is derived deterministically from the Universal_Domain_Model as of the snapshot's
/// instant — there is no external/LLM dependency — so identical inputs always yield identical
/// metrics.
/// </summary>
public sealed record DashboardMetrics
{
    /// <summary>The count of active (non-soft-deleted) orders.</summary>
    public int TotalOrders { get; init; }

    /// <summary>
    /// The count of orders whose sales dimension has reached
    /// <see cref="OrderSalesStage.Completed"/>. The three stage dimensions are independent (Req 4.3);
    /// only the sales dimension determines completion.
    /// </summary>
    public int CompletedOrders { get; init; }

    /// <summary>Total revenue: the summed <see cref="Order.Total"/> of completed orders (scale 2).</summary>
    public CommerceMoney TotalRevenue { get; init; }

    /// <summary>Total gross profit: the summed <see cref="Order.GrossProfit"/> of completed orders (scale 2).</summary>
    public CommerceMoney GrossProfit { get; init; }

    /// <summary>
    /// Outstanding receivable: the summed <see cref="Order.ReceivableAmount"/> across all active
    /// orders (scale 2).
    /// </summary>
    public CommerceMoney OutstandingReceivable { get; init; }

    /// <summary>
    /// Cash inflow: the summed <see cref="CashFlowEntry.Amount"/> of
    /// <see cref="CashFlowDirection.Income"/> entries (scale 2). Transfers are net-zero and excluded.
    /// </summary>
    public CommerceMoney CashInflow { get; init; }

    /// <summary>
    /// Cash outflow: the summed <see cref="CashFlowEntry.Amount"/> of
    /// <see cref="CashFlowDirection.Expense"/> entries (scale 2). Transfers are net-zero and excluded.
    /// </summary>
    public CommerceMoney CashOutflow { get; init; }

    /// <summary>Net cash flow: <see cref="CashInflow"/> minus <see cref="CashOutflow"/> (scale 2).</summary>
    public CommerceMoney NetCashFlow { get; init; }

    /// <summary>The count of active customers.</summary>
    public int CustomerCount { get; init; }

    /// <summary>
    /// The count of active inventory items that are low on stock (available quantity less than or
    /// equal to the item reorder threshold) (Req 4.9).
    /// </summary>
    public int LowStockItemCount { get; init; }
}

/// <summary>
/// One day of a 7-day dashboard trend series (Req 4.13). The values are attributed to the calendar
/// day (in UTC) on which the underlying business event occurred: orders by
/// <see cref="Order.OrderedAt"/> and cash flow by <see cref="CashFlowEntry.OccurredAt"/>.
/// </summary>
public sealed record DashboardTrendPoint
{
    /// <summary>The UTC calendar day this point describes.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The count of completed orders placed (by <see cref="Order.OrderedAt"/>) on this day.</summary>
    public int CompletedOrderCount { get; init; }

    /// <summary>Revenue: the summed <see cref="Order.Total"/> of completed orders placed on this day (scale 2).</summary>
    public CommerceMoney Revenue { get; init; }

    /// <summary>Cash inflow: the summed income amount that occurred on this day (scale 2).</summary>
    public CommerceMoney CashInflow { get; init; }

    /// <summary>Cash outflow: the summed expense amount that occurred on this day (scale 2).</summary>
    public CommerceMoney CashOutflow { get; init; }
}

/// <summary>
/// A unified dashboard read model returned by <see cref="IDashboardService"/> (Req 4.13): the
/// aggregate <see cref="DashboardMetrics"/> plus a 7-day <see cref="Trend"/> series. The snapshot is
/// computed deterministically as of <see cref="AsOfUtc"/> with no hidden wall-clock dependency.
/// </summary>
public sealed record DashboardSnapshot
{
    /// <summary>The UTC instant the snapshot was computed for.</summary>
    public required DateTime AsOfUtc { get; init; }

    /// <summary>The aggregate (point-in-time) metrics.</summary>
    public required DashboardMetrics Metrics { get; init; }

    /// <summary>
    /// The 7-day trend series: exactly 7 daily points covering the calendar day of
    /// <see cref="AsOfUtc"/> and the preceding 6 days, ordered ascending by date. Days with no
    /// activity are present with zeroed values so the series is dense (no gaps).
    /// </summary>
    public required IReadOnlyList<DashboardTrendPoint> Trend { get; init; }
}

/// <summary>
/// Universal dashboard service (Req 4.1, 4.13). Produces a unified <see cref="DashboardSnapshot"/>
/// combining aggregate metrics with a dense 7-day trend series, computed from the
/// Universal_Domain_Model through the Commerce repositories.
///
/// <para>The snapshot call takes an explicit <c>asOfUtc</c> instant so the trend window is
/// deterministic and the result is reproducible with no hidden wall-clock dependency.</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public interface IDashboardService
{
    /// <summary>The fixed length, in days, of the dashboard trend series (Req 4.13).</summary>
    const int TrendWindowDays = 7;

    /// <summary>
    /// Builds the unified dashboard snapshot as of <paramref name="asOfUtc"/>: aggregate metrics plus
    /// the dense 7-day trend series (Req 4.13).
    /// </summary>
    Task<DashboardSnapshot> GetSnapshotAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the aggregate dashboard metrics as of <paramref name="asOfUtc"/> into a set of
    /// workspace-scoped <see cref="BusinessMetricSnapshot"/> records for
    /// <paramref name="workspaceId"/>, persisted idempotently by
    /// <see cref="BusinessMetricSnapshot.BusinessKey"/> (Req 4.20, 18.6). Each snapshot's Business_Key
    /// is stable for the (workspace, metric, UTC day) it captures, so re-running a refresh on the same
    /// day reuses the existing snapshots rather than inserting duplicates. The returned list is the
    /// persisted set (existing where a Business_Key matched, newly created otherwise).
    /// </summary>
    Task<IReadOnlyList<BusinessMetricSnapshot>> PersistMetricSnapshotsAsync(
        Guid workspaceId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}
