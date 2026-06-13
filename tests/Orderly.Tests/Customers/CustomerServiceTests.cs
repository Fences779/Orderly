using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Customers;

/// <summary>
/// Example/unit tests for <see cref="CommerceCustomerService"/> (Task 9.1, Req 4.11). They exercise
/// the real SQLCipher-backed Commerce repositories against an unencrypted temp database (no mocks)
/// to verify the RFM metrics — recency (days since the last completed order), frequency (count of
/// completed orders), and monetary (summed total of completed orders) — plus repurchase reminders.
///
/// <para>Coverage focuses on the edge cases called out by Task 9.2: a customer with no completed
/// orders, a customer with a single order, and ties in recency (several completed orders sharing the
/// most-recent <see cref="Order.OrderedAt"/>). Only orders whose <see cref="Order.SalesStage"/> is
/// <see cref="OrderSalesStage.Completed"/> contribute to the metrics (Req 4.3, 4.11).</para>
/// </summary>
public sealed class CustomerServiceTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    // --- Edge case: no completed orders ---

    [Fact]
    public async Task Customer_with_no_orders_has_null_recency_zero_frequency_and_zero_monetary()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(customer.Id, metrics.CustomerId);
            Assert.Null(metrics.RecencyDays);
            Assert.Null(metrics.LastCompletedOrderAt);
            Assert.Equal(0, metrics.Frequency);
            Assert.Equal(CommerceMoney.Zero, metrics.Monetary);
            Assert.False(metrics.HasCompletedOrders);
        });
    }

    [Fact]
    public async Task Customer_with_only_non_completed_orders_is_treated_as_having_no_completed_orders()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);

            // Every non-completed sales stage must be excluded from the RFM metrics (Req 4.11).
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Draft, 100m, AsOf.AddDays(-2)));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Quoted, 200m, AsOf.AddDays(-3)));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Confirmed, 300m, AsOf.AddDays(-4)));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Cancelled, 400m, AsOf.AddDays(-5)));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Null(metrics.RecencyDays);
            Assert.Equal(0, metrics.Frequency);
            Assert.Equal(CommerceMoney.Zero, metrics.Monetary);
        });
    }

    // --- Edge case: single order ---

    [Fact]
    public async Task Single_completed_order_yields_recency_frequency_one_and_its_total()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            DateTime orderedAt = AsOf.AddDays(-10);
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 150.25m, orderedAt));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(10, metrics.RecencyDays);
            Assert.Equal(orderedAt, metrics.LastCompletedOrderAt);
            Assert.Equal(1, metrics.Frequency);
            Assert.Equal(CommerceMoney.From(150.25m), metrics.Monetary);
            Assert.True(metrics.HasCompletedOrders);
        });
    }

    [Fact]
    public async Task Recency_is_zero_when_the_only_completed_order_is_on_the_as_of_instant()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 80m, AsOf));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(0, metrics.RecencyDays);
        });
    }

    // --- Edge case: ties in recency ---

    [Fact]
    public async Task Ties_in_recency_count_all_completed_orders_and_sum_their_totals()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            DateTime mostRecent = AsOf.AddDays(-5);

            // Two completed orders share the most-recent OrderedAt (a recency tie) and an older one
            // sits behind them. Recency must anchor to the tied most-recent timestamp, frequency must
            // count all three, and monetary must sum all three totals (Req 4.11).
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 100m, mostRecent));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 50m, mostRecent));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 25m, AsOf.AddDays(-20)));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(5, metrics.RecencyDays);
            Assert.Equal(mostRecent, metrics.LastCompletedOrderAt);
            Assert.Equal(3, metrics.Frequency);
            Assert.Equal(CommerceMoney.From(175m), metrics.Monetary);
        });
    }

    // --- Mixed: completed orders separated from non-completed ones ---

    [Fact]
    public async Task Recency_uses_most_recent_completed_order_ignoring_later_non_completed_orders()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);

            // A later draft order must not shift recency away from the earlier completed one.
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 200m, AsOf.AddDays(-12)));
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Draft, 999m, AsOf.AddDays(-1)));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(12, metrics.RecencyDays);
            Assert.Equal(1, metrics.Frequency);
            Assert.Equal(CommerceMoney.From(200m), metrics.Monetary);
        });
    }

    // --- GetAllMetricsAsync ---

    [Fact]
    public async Task GetAllMetrics_includes_customers_without_completed_orders()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer withOrder = await CreateCustomerAsync(customers, workspaceId);
            Customer withoutOrder = await CreateCustomerAsync(customers, workspaceId);
            await orders.CreateAsync(Order(workspaceId, withOrder.Id, OrderSalesStage.Completed, 60m, AsOf.AddDays(-3)));

            IReadOnlyList<CustomerRfmMetrics> all = await service.GetAllMetricsAsync(AsOf);

            Assert.Equal(2, all.Count);

            CustomerRfmMetrics active = all.Single(m => m.CustomerId == withOrder.Id);
            Assert.Equal(1, active.Frequency);
            Assert.Equal(3, active.RecencyDays);

            CustomerRfmMetrics idle = all.Single(m => m.CustomerId == withoutOrder.Id);
            Assert.Equal(0, idle.Frequency);
            Assert.Null(idle.RecencyDays);
            Assert.Equal(CommerceMoney.Zero, idle.Monetary);
        });
    }

    // --- Repurchase reminders ---

    [Fact]
    public async Task Repurchase_reminder_raised_when_recency_meets_threshold()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            DateTime orderedAt = AsOf.AddDays(-30);
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 120m, orderedAt));

            IReadOnlyList<RepurchaseReminder> reminders =
                await service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: 30);

            RepurchaseReminder reminder = Assert.Single(reminders);
            Assert.Equal(customer.Id, reminder.CustomerId);
            Assert.Equal(30, reminder.RecencyDays);
            Assert.Equal(orderedAt, reminder.LastCompletedOrderAt);
            Assert.Equal(1, reminder.Frequency);
            Assert.Equal(CommerceMoney.From(120m), reminder.Monetary);
            Assert.Equal(30, reminder.ReminderThresholdDays);
            Assert.Equal(orderedAt.AddDays(30), reminder.DueSinceUtc);
        });
    }

    [Fact]
    public async Task Repurchase_reminder_not_raised_when_recency_below_threshold()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Completed, 120m, AsOf.AddDays(-29)));

            IReadOnlyList<RepurchaseReminder> reminders =
                await service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: 30);

            Assert.Empty(reminders);
        });
    }

    [Fact]
    public async Task Customers_without_completed_orders_are_never_reminded()
    {
        await WithServiceAsync(async (service, orders, customers, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            // Only a stale non-completed order exists; it must never trigger a reminder.
            await orders.CreateAsync(Order(workspaceId, customer.Id, OrderSalesStage.Draft, 500m, AsOf.AddDays(-365)));

            IReadOnlyList<RepurchaseReminder> reminders =
                await service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: 30);

            Assert.Empty(reminders);
        });
    }

    [Fact]
    public async Task Negative_reminder_threshold_throws()
    {
        await WithServiceAsync(async (service, _, _, _) =>
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: -1));
        });
    }

    // --- Helpers ---

    private static Order Order(
        Guid workspaceId,
        Guid customerId,
        OrderSalesStage salesStage,
        decimal total,
        DateTime orderedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CustomerId = customerId,
            SalesStage = salesStage,
            Total = CommerceMoney.From(total),
            OrderedAt = orderedAt,
        };

    private static async Task<Customer> CreateCustomerAsync(
        ICommerceCustomerRepository customers,
        Guid workspaceId)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = "客户 " + Guid.NewGuid().ToString("N")[..6],
        };

        return await customers.CreateAsync(customer);
    }

    private static async Task WithServiceAsync(
        Func<CommerceCustomerService, ICommerceOrderRepository, ICommerceCustomerRepository, Guid, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-customer-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var orders = new CommerceOrderRepository(factory);
            var customers = new CommerceCustomerRepository(factory);
            var service = new CommerceCustomerService(orders, customers);

            await body(service, orders, customers, Guid.NewGuid());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Best-effort cleanup of temp files.
                }
            }
        }
    }
}
