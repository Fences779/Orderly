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
/// Example/unit tests for <see cref="CommerceCustomerService"/> (Task 9.2, Req 4.11). They exercise
/// the real SQLCipher-backed Commerce repositories against an unencrypted temp database (no mocks)
/// to verify the RFM metrics — recency (days since the last completed order), frequency (count of
/// completed orders), monetary (summed total of completed orders) — and repurchase reminders.
///
/// <para>The required edge cases are covered explicitly: a customer with <b>no completed orders</b>,
/// a customer with a <b>single</b> completed order, and <b>ties in recency</b> (two customers whose
/// most recent completed orders share the same business date).</para>
/// </summary>
public sealed class CustomerMetricsTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Workspace = Guid.NewGuid();

    // --- Edge case: no completed orders ---

    [Fact]
    public async Task Customer_with_no_orders_has_zeroed_metrics_and_null_recency()
    {
        await WithServiceAsync(async (service, customers, _, workspaceId) =>
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
    public async Task Customer_with_only_non_completed_orders_has_zeroed_metrics()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);

            // Orders that exist but have not reached the completed sales stage do not count (Req 4.11).
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Draft, 100m, AsOf.AddDays(-5));
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Confirmed, 250m, AsOf.AddDays(-2));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Null(metrics.RecencyDays);
            Assert.Null(metrics.LastCompletedOrderAt);
            Assert.Equal(0, metrics.Frequency);
            Assert.Equal(CommerceMoney.Zero, metrics.Monetary);
        });
    }

    [Fact]
    public async Task Customer_with_no_completed_orders_is_never_reminded()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Draft, 500m, AsOf.AddDays(-365));

            IReadOnlyList<RepurchaseReminder> reminders = await service.GetRepurchaseRemindersAsync(AsOf);

            Assert.DoesNotContain(reminders, reminder => reminder.CustomerId == customer.Id);
        });
    }

    // --- Edge case: single order ---

    [Fact]
    public async Task Single_completed_order_yields_frequency_one_and_exact_recency_and_monetary()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            DateTime orderedAt = AsOf.AddDays(-10);
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Completed, 123.45m, orderedAt);

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(1, metrics.Frequency);
            Assert.Equal(10, metrics.RecencyDays);
            Assert.Equal(orderedAt, metrics.LastCompletedOrderAt);
            Assert.Equal(CommerceMoney.From(123.45m), metrics.Monetary);
            Assert.True(metrics.HasCompletedOrders);
        });
    }

    [Fact]
    public async Task Single_completed_order_on_as_of_instant_has_zero_recency()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Completed, 50m, AsOf);

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(0, metrics.RecencyDays);
        });
    }

    [Fact]
    public async Task Multiple_completed_orders_sum_monetary_and_use_most_recent_for_recency()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer customer = await CreateCustomerAsync(customers, workspaceId);
            DateTime mostRecent = AsOf.AddDays(-3);
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Completed, 100m, AsOf.AddDays(-20));
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Completed, 200m, mostRecent);
            await CreateOrderAsync(orders, workspaceId, customer.Id, OrderSalesStage.Completed, 99.99m, AsOf.AddDays(-12));

            CustomerRfmMetrics metrics = await service.GetMetricsAsync(customer.Id, AsOf);

            Assert.Equal(3, metrics.Frequency);
            Assert.Equal(CommerceMoney.From(399.99m), metrics.Monetary);
            Assert.Equal(mostRecent, metrics.LastCompletedOrderAt);
            Assert.Equal(3, metrics.RecencyDays);
        });
    }

    // --- Edge case: ties in recency ---

    [Fact]
    public async Task Customers_tied_on_recency_report_identical_recency_days()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer first = await CreateCustomerAsync(customers, workspaceId);
            Customer second = await CreateCustomerAsync(customers, workspaceId);

            DateTime sharedLastOrder = AsOf.AddDays(-7);
            // Both customers' most recent completed order falls on the exact same business instant.
            await CreateOrderAsync(orders, workspaceId, first.Id, OrderSalesStage.Completed, 80m, sharedLastOrder);
            await CreateOrderAsync(orders, workspaceId, second.Id, OrderSalesStage.Completed, 160m, sharedLastOrder);

            CustomerRfmMetrics firstMetrics = await service.GetMetricsAsync(first.Id, AsOf);
            CustomerRfmMetrics secondMetrics = await service.GetMetricsAsync(second.Id, AsOf);

            Assert.Equal(7, firstMetrics.RecencyDays);
            Assert.Equal(7, secondMetrics.RecencyDays);
            Assert.Equal(firstMetrics.RecencyDays, secondMetrics.RecencyDays);
            Assert.Equal(firstMetrics.LastCompletedOrderAt, secondMetrics.LastCompletedOrderAt);
        });
    }

    [Fact]
    public async Task Tied_recency_customers_are_both_reminded_when_threshold_met()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer first = await CreateCustomerAsync(customers, workspaceId);
            Customer second = await CreateCustomerAsync(customers, workspaceId);

            DateTime sharedLastOrder = AsOf.AddDays(-30);
            await CreateOrderAsync(orders, workspaceId, first.Id, OrderSalesStage.Completed, 80m, sharedLastOrder);
            await CreateOrderAsync(orders, workspaceId, second.Id, OrderSalesStage.Completed, 160m, sharedLastOrder);

            IReadOnlyList<RepurchaseReminder> reminders =
                await service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: 30);

            RepurchaseReminder firstReminder = Assert.Single(reminders, r => r.CustomerId == first.Id);
            RepurchaseReminder secondReminder = Assert.Single(reminders, r => r.CustomerId == second.Id);

            Assert.Equal(30, firstReminder.RecencyDays);
            Assert.Equal(30, secondReminder.RecencyDays);
            Assert.Equal(firstReminder.DueSinceUtc, secondReminder.DueSinceUtc);
        });
    }

    [Fact]
    public async Task Latest_of_two_orders_sharing_a_date_drives_recency_tie()
    {
        // When a single customer has two completed orders on the same day as another customer's
        // single order, the max() over a tie is still that shared date for everyone.
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer first = await CreateCustomerAsync(customers, workspaceId);
            Customer second = await CreateCustomerAsync(customers, workspaceId);

            DateTime sharedLastOrder = AsOf.AddDays(-5);
            await CreateOrderAsync(orders, workspaceId, first.Id, OrderSalesStage.Completed, 10m, AsOf.AddDays(-15));
            await CreateOrderAsync(orders, workspaceId, first.Id, OrderSalesStage.Completed, 20m, sharedLastOrder);
            await CreateOrderAsync(orders, workspaceId, second.Id, OrderSalesStage.Completed, 30m, sharedLastOrder);

            CustomerRfmMetrics firstMetrics = await service.GetMetricsAsync(first.Id, AsOf);
            CustomerRfmMetrics secondMetrics = await service.GetMetricsAsync(second.Id, AsOf);

            Assert.Equal(sharedLastOrder, firstMetrics.LastCompletedOrderAt);
            Assert.Equal(sharedLastOrder, secondMetrics.LastCompletedOrderAt);
            Assert.Equal(firstMetrics.RecencyDays, secondMetrics.RecencyDays);
            Assert.Equal(2, firstMetrics.Frequency);
            Assert.Equal(1, secondMetrics.Frequency);
        });
    }

    // --- Reminder threshold boundary ---

    [Fact]
    public async Task Reminder_is_raised_at_threshold_but_not_below()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer due = await CreateCustomerAsync(customers, workspaceId);
            Customer recent = await CreateCustomerAsync(customers, workspaceId);

            // Exactly at the threshold -> reminded.
            await CreateOrderAsync(orders, workspaceId, due.Id, OrderSalesStage.Completed, 100m, AsOf.AddDays(-30));
            // Below the threshold -> not reminded.
            await CreateOrderAsync(orders, workspaceId, recent.Id, OrderSalesStage.Completed, 100m, AsOf.AddDays(-29));

            IReadOnlyList<RepurchaseReminder> reminders =
                await service.GetRepurchaseRemindersAsync(AsOf, reminderThresholdDays: 30);

            Assert.Contains(reminders, reminder => reminder.CustomerId == due.Id);
            Assert.DoesNotContain(reminders, reminder => reminder.CustomerId == recent.Id);
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

    // --- GetAllMetrics includes customers without completed orders ---

    [Fact]
    public async Task GetAllMetrics_includes_customers_without_completed_orders()
    {
        await WithServiceAsync(async (service, customers, orders, workspaceId) =>
        {
            Customer withOrder = await CreateCustomerAsync(customers, workspaceId);
            Customer withoutOrder = await CreateCustomerAsync(customers, workspaceId);
            await CreateOrderAsync(orders, workspaceId, withOrder.Id, OrderSalesStage.Completed, 75m, AsOf.AddDays(-1));

            IReadOnlyList<CustomerRfmMetrics> all = await service.GetAllMetricsAsync(AsOf);

            CustomerRfmMetrics withMetrics = Assert.Single(all, m => m.CustomerId == withOrder.Id);
            CustomerRfmMetrics withoutMetrics = Assert.Single(all, m => m.CustomerId == withoutOrder.Id);

            Assert.Equal(1, withMetrics.Frequency);
            Assert.Equal(0, withoutMetrics.Frequency);
            Assert.Null(withoutMetrics.RecencyDays);
            Assert.Equal(CommerceMoney.Zero, withoutMetrics.Monetary);
        });
    }

    // --- Helpers ---

    private static async Task<Customer> CreateCustomerAsync(ICommerceCustomerRepository customers, Guid workspaceId)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = "客户 " + Guid.NewGuid().ToString("N")[..6],
        };

        return await customers.CreateAsync(customer);
    }

    private static async Task<Order> CreateOrderAsync(
        ICommerceOrderRepository orders,
        Guid workspaceId,
        Guid customerId,
        OrderSalesStage salesStage,
        decimal total,
        DateTime orderedAt)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CustomerId = customerId,
            SalesStage = salesStage,
            Total = CommerceMoney.From(total),
            OrderedAt = orderedAt,
        };

        return await orders.CreateAsync(order);
    }

    private static async Task WithServiceAsync(
        Func<CommerceCustomerService, ICommerceCustomerRepository, ICommerceOrderRepository, Guid, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-customers-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var customers = new CommerceCustomerRepository(factory);
            var orders = new CommerceOrderRepository(factory);
            var service = new CommerceCustomerService(orders, customers);

            await body(service, customers, orders, Workspace);
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
