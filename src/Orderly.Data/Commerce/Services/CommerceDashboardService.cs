using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Universal dashboard service implementation (Req 4.1, 4.13). Produces a unified
/// <see cref="DashboardSnapshot"/> combining aggregate metrics with a dense 7-day trend series,
/// derived deterministically from the Universal_Domain_Model through the Commerce repositories.
///
/// <para><b>Completion semantics.</b> Revenue and gross profit count only <b>completed</b> orders —
/// orders whose <see cref="Order.SalesStage"/> is <see cref="OrderSalesStage.Completed"/>. The three
/// stage dimensions are independent (Req 4.3); only the sales dimension determines completion.</para>
///
/// <para><b>Cash flow.</b> Cash inflow/outflow sum the <see cref="CashFlowEntry.Amount"/> of
/// <see cref="CashFlowDirection.Income"/> / <see cref="CashFlowDirection.Expense"/> entries
/// respectively. <see cref="CashFlowDirection.Transfer"/> entries are net-zero and excluded
/// (Req 4.12).</para>
///
/// <para><b>Trend window.</b> The trend is exactly <see cref="IDashboardService.TrendWindowDays"/>
/// daily points covering the UTC calendar day of the requested instant and the preceding 6 days,
/// ordered ascending. Days with no activity are present with zeroed values so the series is dense.
/// Orders are attributed to their <see cref="Order.OrderedAt"/> day and cash flow to its
/// <see cref="CashFlowEntry.OccurredAt"/> day.</para>
///
/// <para>All metric calls take an explicit <c>asOfUtc</c> instant so the result is reproducible with
/// no hidden wall-clock dependency. This type is industry-agnostic and free of any Forbidden_Term,
/// and reads only through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceDashboardService : IDashboardService
{
    private readonly ICommerceOrderRepository _orderRepository;
    private readonly ICashFlowEntryRepository _cashFlowRepository;
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly ICommerceCustomerRepository _customerRepository;
    private readonly IBusinessMetricSnapshotRepository? _snapshotRepository;

    /// <summary>
    /// Creates the service over the Commerce order, cash-flow, inventory item, and customer
    /// repositories, plus the optional metric-snapshot repository used by
    /// <see cref="PersistMetricSnapshotsAsync"/>. When <paramref name="snapshotRepository"/> is
    /// omitted the read-only snapshot slice still works, but
    /// <see cref="PersistMetricSnapshotsAsync"/> then throws because it has nowhere to persist.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when any required repository is null.</exception>
    public CommerceDashboardService(
        ICommerceOrderRepository orderRepository,
        ICashFlowEntryRepository cashFlowRepository,
        IInventoryItemRepository inventoryItemRepository,
        ICommerceCustomerRepository customerRepository,
        IBusinessMetricSnapshotRepository? snapshotRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _cashFlowRepository = cashFlowRepository ?? throw new ArgumentNullException(nameof(cashFlowRepository));
        _inventoryItemRepository = inventoryItemRepository ?? throw new ArgumentNullException(nameof(inventoryItemRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _snapshotRepository = snapshotRepository;
    }

    /// <inheritdoc />
    public async Task<DashboardSnapshot> GetSnapshotAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CashFlowEntry> cashFlows = await _cashFlowRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<InventoryItem> items = await _inventoryItemRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Customer> customers = await _customerRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        DashboardMetrics metrics = BuildMetrics(orders, cashFlows, items, customers);
        IReadOnlyList<DashboardTrendPoint> trend = BuildTrend(orders, cashFlows, asOfUtc);

        return new DashboardSnapshot
        {
            AsOfUtc = asOfUtc,
            Metrics = metrics,
            Trend = trend,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BusinessMetricSnapshot>> PersistMetricSnapshotsAsync(
        Guid workspaceId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotRepository is null)
        {
            throw new InvalidOperationException(
                "This CommerceDashboardService was created without the metric-snapshot repository required to persist snapshots; supply an IBusinessMetricSnapshotRepository to the constructor.");
        }

        DashboardSnapshot snapshot = await GetSnapshotAsync(asOfUtc, cancellationToken).ConfigureAwait(false);

        // Capture the headline metrics as workspace-scoped snapshot records. Each carries a stable
        // Business_Key for its (workspace, metric, UTC day) so re-running the refresh on the same day
        // reuses the existing record rather than inserting a duplicate (Req 4.20, 18.6).
        IReadOnlyList<BusinessMetricSnapshot> candidates = BuildMetricSnapshots(workspaceId, asOfUtc, snapshot.Metrics);

        var persisted = new List<BusinessMetricSnapshot>(candidates.Count);
        foreach (BusinessMetricSnapshot candidate in candidates)
        {
            BusinessMetricSnapshot stored = await BusinessKeyIdempotency
                .CreateIdempotentAsync(_snapshotRepository, candidate, s => s.BusinessKey, cancellationToken)
                .ConfigureAwait(false);
            persisted.Add(stored);
        }

        return persisted;
    }

    /// <summary>
    /// Builds the workspace-scoped metric snapshots captured from <paramref name="metrics"/> as of
    /// <paramref name="asOfUtc"/>. Each snapshot's Business_Key is stable for its (workspace, metric
    /// key, UTC calendar day) so the capture is idempotent within a day (Req 4.20, 18.6). The captured
    /// <paramref name="metrics"/> are the dashboard aggregates; the workspace tag records which
    /// workspace requested the capture.
    /// </summary>
    private static IReadOnlyList<BusinessMetricSnapshot> BuildMetricSnapshots(
        Guid workspaceId,
        DateTime asOfUtc,
        DashboardMetrics metrics)
    {
        DateOnly day = DateOnly.FromDateTime(asOfUtc);

        BusinessMetricSnapshot Numeric(string metricKey, decimal value) => new()
        {
            WorkspaceId = workspaceId,
            MetricKey = metricKey,
            CapturedAt = asOfUtc,
            NumericValue = value,
            BusinessKey = SnapshotBusinessKey(workspaceId, metricKey, day),
        };

        BusinessMetricSnapshot Money(string metricKey, CommerceMoney value) => new()
        {
            WorkspaceId = workspaceId,
            MetricKey = metricKey,
            CapturedAt = asOfUtc,
            NumericValue = value.Amount,
            MoneyValue = value,
            BusinessKey = SnapshotBusinessKey(workspaceId, metricKey, day),
        };

        return new[]
        {
            Numeric("total-orders", metrics.TotalOrders),
            Numeric("completed-orders", metrics.CompletedOrders),
            Numeric("customer-count", metrics.CustomerCount),
            Numeric("low-stock-item-count", metrics.LowStockItemCount),
            Money("total-revenue", metrics.TotalRevenue),
            Money("gross-profit", metrics.GrossProfit),
            Money("outstanding-receivable", metrics.OutstandingReceivable),
            Money("cash-inflow", metrics.CashInflow),
            Money("cash-outflow", metrics.CashOutflow),
            Money("net-cash-flow", metrics.NetCashFlow),
        };
    }

    /// <summary>
    /// The stable Business_Key for a captured metric snapshot, unique per workspace, metric, and UTC
    /// calendar day, making the daily capture idempotent (Req 4.20, 18.6). Contains no Forbidden_Term.
    /// </summary>
    private static string SnapshotBusinessKey(Guid workspaceId, string metricKey, DateOnly day)
        => $"metric-snapshot:{workspaceId:N}:{metricKey}:{day:yyyy-MM-dd}";

    /// <summary>Builds the aggregate (point-in-time) metrics across all active records (Req 4.13).</summary>
    private static DashboardMetrics BuildMetrics(
        IReadOnlyList<Order> orders,
        IReadOnlyList<CashFlowEntry> cashFlows,
        IReadOnlyList<InventoryItem> items,
        IReadOnlyList<Customer> customers)
    {
        List<Order> completed = orders.Where(IsCompleted).ToList();

        decimal totalRevenue = completed.Sum(order => order.Total.Amount);
        decimal grossProfit = completed.Sum(order => order.GrossProfit.Amount);
        decimal outstandingReceivable = orders.Sum(order => order.ReceivableAmount.Amount);

        decimal cashInflow = cashFlows
            .Where(entry => entry.Direction == CashFlowDirection.Income)
            .Sum(entry => entry.Amount.Amount);
        decimal cashOutflow = cashFlows
            .Where(entry => entry.Direction == CashFlowDirection.Expense)
            .Sum(entry => entry.Amount.Amount);

        int lowStockItemCount = items.Count(item => item.QuantityAvailable <= item.ReorderThreshold);

        return new DashboardMetrics
        {
            TotalOrders = orders.Count,
            CompletedOrders = completed.Count,
            TotalRevenue = CommerceMoney.From(totalRevenue),
            GrossProfit = CommerceMoney.From(grossProfit),
            OutstandingReceivable = CommerceMoney.From(outstandingReceivable),
            CashInflow = CommerceMoney.From(cashInflow),
            CashOutflow = CommerceMoney.From(cashOutflow),
            NetCashFlow = CommerceMoney.From(cashInflow - cashOutflow),
            CustomerCount = customers.Count,
            LowStockItemCount = lowStockItemCount,
        };
    }

    /// <summary>
    /// Builds the dense 7-day trend series ending on the UTC calendar day of
    /// <paramref name="asOfUtc"/> (Req 4.13). Every day in the window is present, with zeroed values
    /// for days that had no activity, so the series has no gaps.
    /// </summary>
    private static IReadOnlyList<DashboardTrendPoint> BuildTrend(
        IReadOnlyList<Order> orders,
        IReadOnlyList<CashFlowEntry> cashFlows,
        DateTime asOfUtc)
    {
        DateOnly endDay = DateOnly.FromDateTime(asOfUtc);
        DateOnly startDay = endDay.AddDays(-(IDashboardService.TrendWindowDays - 1));

        // Pre-aggregate completed-order revenue/count by day so each lookup is O(1).
        Dictionary<DateOnly, (int Count, decimal Revenue)> ordersByDay = orders
            .Where(IsCompleted)
            .GroupBy(order => DateOnly.FromDateTime(order.OrderedAt))
            .ToDictionary(
                group => group.Key,
                group => (group.Count(), group.Sum(order => order.Total.Amount)));

        Dictionary<DateOnly, decimal> inflowByDay = cashFlows
            .Where(entry => entry.Direction == CashFlowDirection.Income)
            .GroupBy(entry => DateOnly.FromDateTime(entry.OccurredAt))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount.Amount));

        Dictionary<DateOnly, decimal> outflowByDay = cashFlows
            .Where(entry => entry.Direction == CashFlowDirection.Expense)
            .GroupBy(entry => DateOnly.FromDateTime(entry.OccurredAt))
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Amount.Amount));

        var points = new List<DashboardTrendPoint>(IDashboardService.TrendWindowDays);
        for (DateOnly day = startDay; day <= endDay; day = day.AddDays(1))
        {
            (int count, decimal revenue) = ordersByDay.TryGetValue(day, out (int Count, decimal Revenue) orderDay)
                ? orderDay
                : (0, 0m);
            decimal inflow = inflowByDay.TryGetValue(day, out decimal inflowValue) ? inflowValue : 0m;
            decimal outflow = outflowByDay.TryGetValue(day, out decimal outflowValue) ? outflowValue : 0m;

            points.Add(new DashboardTrendPoint
            {
                Date = day,
                CompletedOrderCount = count,
                Revenue = CommerceMoney.From(revenue),
                CashInflow = CommerceMoney.From(inflow),
                CashOutflow = CommerceMoney.From(outflow),
            });
        }

        return points;
    }

    /// <summary>An order counts as completed when its sales dimension has reached completion (Req 4.3).</summary>
    private static bool IsCompleted(Order order) => order.SalesStage == OrderSalesStage.Completed;
}
