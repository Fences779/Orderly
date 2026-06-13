using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Universal customer service implementation (Req 4.1, 4.11). Computes the RFM
/// (Recency / Frequency / Monetary) metrics and produces repurchase reminders over the
/// Universal_Domain_Model, derived exclusively from <b>completed</b> orders — orders whose
/// <see cref="Order.SalesStage"/> is <see cref="OrderSalesStage.Completed"/> (the three stage
/// dimensions are independent per Req 4.3; only the sales dimension determines completion for RFM).
///
/// <para><b>Recency timestamp.</b> An order's business date is its <see cref="Order.OrderedAt"/>
/// (a stable, persisted business field). Recency is computed from the most recent completed order's
/// <see cref="Order.OrderedAt"/> rather than the audit <c>UpdatedAt</c>, so edits to an order never
/// shift its recency.</para>
///
/// <para>All metric calls take an explicit <c>asOfUtc</c> instant so recency is deterministic and
/// reproducible with no hidden wall-clock dependency.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads only through the
/// Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceCustomerService : ICustomerService
{
    private readonly ICommerceOrderRepository _orderRepository;
    private readonly ICommerceCustomerRepository _customerRepository;

    /// <summary>
    /// Creates the service over the Commerce order and customer repositories.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when either repository is null.</exception>
    public CommerceCustomerService(
        ICommerceOrderRepository orderRepository,
        ICommerceCustomerRepository customerRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    }

    /// <inheritdoc />
    public async Task<CustomerRfmMetrics> GetMetricsAsync(
        Guid customerId,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Order> orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        List<Order> completed = orders
            .Where(order => order.CustomerId == customerId && IsCompleted(order))
            .ToList();

        return BuildMetrics(customerId, completed, asOfUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerRfmMetrics>> GetAllMetricsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Customer> customers = await _customerRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Order> orders = await _orderRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        // Group completed orders by their owning customer once, then build metrics per customer so
        // customers with no completed orders are still represented with zeroed frequency/monetary.
        Dictionary<Guid, List<Order>> completedByCustomer = orders
            .Where(order => order.CustomerId is not null && IsCompleted(order))
            .GroupBy(order => order.CustomerId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var metrics = new List<CustomerRfmMetrics>(customers.Count);
        foreach (Customer customer in customers)
        {
            List<Order> completed = completedByCustomer.TryGetValue(customer.Id, out List<Order>? value)
                ? value
                : new List<Order>();
            metrics.Add(BuildMetrics(customer.Id, completed, asOfUtc));
        }

        return metrics;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepurchaseReminder>> GetRepurchaseRemindersAsync(
        DateTime asOfUtc,
        int reminderThresholdDays = ICustomerService.DefaultReminderThresholdDays,
        CancellationToken cancellationToken = default)
    {
        if (reminderThresholdDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reminderThresholdDays),
                reminderThresholdDays,
                "The repurchase reminder threshold (in days) must not be negative.");
        }

        IReadOnlyList<CustomerRfmMetrics> allMetrics = await GetAllMetricsAsync(asOfUtc, cancellationToken)
            .ConfigureAwait(false);

        var reminders = new List<RepurchaseReminder>();
        foreach (CustomerRfmMetrics metrics in allMetrics)
        {
            // Customers without completed orders are never reminded (Req 4.11).
            if (!metrics.HasCompletedOrders ||
                metrics.LastCompletedOrderAt is not DateTime lastCompletedOrderAt ||
                metrics.RecencyDays is not int recencyDays)
            {
                continue;
            }

            if (recencyDays < reminderThresholdDays)
            {
                continue;
            }

            reminders.Add(new RepurchaseReminder
            {
                CustomerId = metrics.CustomerId,
                LastCompletedOrderAt = lastCompletedOrderAt,
                RecencyDays = recencyDays,
                Frequency = metrics.Frequency,
                Monetary = metrics.Monetary,
                ReminderThresholdDays = reminderThresholdDays,
                DueSinceUtc = lastCompletedOrderAt.AddDays(reminderThresholdDays),
            });
        }

        return reminders;
    }

    /// <summary>An order counts toward RFM only when its sales dimension has reached completion (Req 4.11).</summary>
    private static bool IsCompleted(Order order) => order.SalesStage == OrderSalesStage.Completed;

    /// <summary>
    /// Builds the RFM read model for a customer from that customer's completed orders. With no
    /// completed orders the result reports a null recency, zero frequency, and zero monetary value.
    /// </summary>
    private static CustomerRfmMetrics BuildMetrics(Guid customerId, List<Order> completedOrders, DateTime asOfUtc)
    {
        if (completedOrders.Count == 0)
        {
            return new CustomerRfmMetrics
            {
                CustomerId = customerId,
                RecencyDays = null,
                LastCompletedOrderAt = null,
                Frequency = 0,
                Monetary = CommerceMoney.Zero,
            };
        }

        DateTime lastCompletedOrderAt = completedOrders.Max(order => order.OrderedAt);
        decimal monetary = completedOrders.Sum(order => order.Total.Amount);

        return new CustomerRfmMetrics
        {
            CustomerId = customerId,
            RecencyDays = WholeDaysBetween(lastCompletedOrderAt, asOfUtc),
            LastCompletedOrderAt = lastCompletedOrderAt,
            Frequency = completedOrders.Count,
            Monetary = CommerceMoney.From(monetary),
        };
    }

    /// <summary>
    /// Whole days elapsed from <paramref name="from"/> to <paramref name="asOf"/>, floored so a
    /// partial day does not count. Returns a negative value when <paramref name="asOf"/> precedes
    /// <paramref name="from"/>.
    /// </summary>
    private static int WholeDaysBetween(DateTime from, DateTime asOf)
        => (int)Math.Floor((asOf - from).TotalDays);
}
