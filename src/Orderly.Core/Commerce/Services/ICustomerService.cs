namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Customer-facing read model returned by <see cref="ICustomerService"/>: the RFM
/// (Recency / Frequency / Monetary) metrics computed over a customer's <b>completed</b> orders
/// (Req 4.11).
///
/// <para><b>Completed order semantics.</b> An order counts as completed when its sales dimension has
/// reached <see cref="OrderSalesStage.Completed"/> — the stage <c>IOrderService</c> sets when an
/// order is completed. The three order stage dimensions are independent (Req 4.3); only the sales
/// dimension determines completion for RFM purposes.</para>
/// </summary>
public sealed record CustomerRfmMetrics
{
    /// <summary>The customer these metrics describe.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Recency: whole days between the most recent completed order and the as-of instant.
    /// <c>null</c> when the customer has no completed orders.
    /// </summary>
    public int? RecencyDays { get; init; }

    /// <summary>UTC timestamp of the most recent completed order, or <c>null</c> when there are none.</summary>
    public DateTime? LastCompletedOrderAt { get; init; }

    /// <summary>Frequency: the count of completed orders for the customer.</summary>
    public int Frequency { get; init; }

    /// <summary>Monetary: the summed total of the customer's completed orders (scale 2).</summary>
    public CommerceMoney Monetary { get; init; }

    /// <summary><c>true</c> when the customer has at least one completed order.</summary>
    public bool HasCompletedOrders => Frequency > 0;
}

/// <summary>
/// A repurchase reminder produced by <see cref="ICustomerService"/> for a customer whose most recent
/// completed order is at least the configured threshold of days in the past (Req 4.11). Customers
/// with no completed orders are never reminded.
/// </summary>
public sealed record RepurchaseReminder
{
    /// <summary>The customer to remind.</summary>
    public required Guid CustomerId { get; init; }

    /// <summary>UTC timestamp of the customer's most recent completed order.</summary>
    public required DateTime LastCompletedOrderAt { get; init; }

    /// <summary>Whole days since the most recent completed order, as of the evaluation instant.</summary>
    public int RecencyDays { get; init; }

    /// <summary>The count of completed orders for the customer (frequency).</summary>
    public int Frequency { get; init; }

    /// <summary>The summed total of the customer's completed orders (monetary, scale 2).</summary>
    public CommerceMoney Monetary { get; init; }

    /// <summary>The reminder threshold (in days) that was met or exceeded to raise this reminder.</summary>
    public int ReminderThresholdDays { get; init; }

    /// <summary>
    /// The UTC instant from which this customer became due for a reminder
    /// (<see cref="LastCompletedOrderAt"/> plus <see cref="ReminderThresholdDays"/> days).
    /// </summary>
    public DateTime DueSinceUtc { get; init; }
}

/// <summary>
/// Universal customer service (Req 4.1). This slice computes the RFM metrics and repurchase
/// reminders defined by Req 4.11 over the Universal_Domain_Model. Recency, frequency, and monetary
/// value are derived exclusively from <b>completed</b> orders
/// (orders whose <see cref="OrderSalesStage"/> is <see cref="OrderSalesStage.Completed"/>).
///
/// <para>All metric calls take an explicit <c>asOfUtc</c> instant so recency is deterministic and the
/// results are reproducible (no hidden wall-clock dependency).</para>
/// </summary>
public interface ICustomerService
{
    /// <summary>The default repurchase reminder threshold, in days, when none is supplied.</summary>
    const int DefaultReminderThresholdDays = 30;

    /// <summary>
    /// Computes the RFM metrics for a single customer as of <paramref name="asOfUtc"/>. When the
    /// customer has no completed orders the result reports <c>RecencyDays = null</c>,
    /// <c>Frequency = 0</c>, and <c>Monetary = 0.00</c>.
    /// </summary>
    Task<CustomerRfmMetrics> GetMetricsAsync(
        Guid customerId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the RFM metrics for every customer as of <paramref name="asOfUtc"/>. Customers with
    /// no completed orders are included with zeroed frequency/monetary and a null recency.
    /// </summary>
    Task<IReadOnlyList<CustomerRfmMetrics>> GetAllMetricsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces repurchase reminders as of <paramref name="asOfUtc"/> for customers whose most recent
    /// completed order is at least <paramref name="reminderThresholdDays"/> days in the past.
    /// Customers without completed orders are never reminded.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="reminderThresholdDays"/> is negative.
    /// </exception>
    Task<IReadOnlyList<RepurchaseReminder>> GetRepurchaseRemindersAsync(
        DateTime asOfUtc,
        int reminderThresholdDays = DefaultReminderThresholdDays,
        CancellationToken cancellationToken = default);
}
