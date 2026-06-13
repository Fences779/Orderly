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

namespace Orderly.Tests.Inventory;

/// <summary>
/// Example/unit tests for order completion in <see cref="CommerceOrderService.CompleteOrderAsync"/>
/// (Task 7.5). They exercise the real SQLCipher-backed Commerce repositories against an unencrypted
/// temp database (no mocks) and verify, within the Core_Write_Transaction:
/// <list type="bullet">
///   <item><description>required quantity is aggregated per <c>InventoryItemId</c> across lines and applied as exactly one deduction + one movement (Req 4.6, 4.16, 4.17);</description></item>
///   <item><description>non-linked lines neither block completion nor deduct (Req 4.6);</description></item>
///   <item><description>insufficient inventory rejects and rolls the whole transaction back (Req 4.7, 18.3);</description></item>
///   <item><description>re-running completion is idempotent — no double deduction, no duplicate movement (Req 4.20, 18.6);</description></item>
///   <item><description>linked customer statistics are refreshed on completion (Req 4.6).</description></item>
/// </list>
/// The universal aggregation/rollback and atomicity properties are covered separately by Property 10
/// (Task 7.6) and Property 19 (Task 7.7).
/// </summary>
public sealed class OrderCompletionExampleTests
{
    private static readonly DateTime CompletedAt = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Completion_aggregates_per_inventory_item_and_applies_one_deduction_each()
    {
        await WithServiceAsync(async ctx =>
        {
            InventoryItem itemA = await ctx.CreateItemAsync(quantity: 10m);
            InventoryItem itemB = await ctx.CreateItemAsync(quantity: 8m);
            Order order = await ctx.CreateOrderAsync(customerId: null);

            // Two lines reference itemA (aggregate 3 + 4 = 7) and one references itemB (2).
            await ctx.AddLineAsync(order, itemA, quantity: 3m);
            await ctx.AddLineAsync(order, itemA, quantity: 4m);
            await ctx.AddLineAsync(order, itemB, quantity: 2m);

            OrderCompletionResult result = await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);

            Assert.True(result.IsCompleted);
            Assert.Equal(10m - 7m, (await ctx.Items.GetByIdAsync(itemA.Id))!.QuantityAvailable);
            Assert.Equal(8m - 2m, (await ctx.Items.GetByIdAsync(itemB.Id))!.QuantityAvailable);

            // Exactly one outbound movement per InventoryItemId, each equal to the aggregate.
            List<InventoryMovement> movements = (await ctx.Movements.GetAllAsync()).ToList();
            Assert.Equal(2, movements.Count);
            Assert.Equal(7m, movements.Single(m => m.InventoryItemId == itemA.Id).Quantity);
            Assert.Equal(2m, movements.Single(m => m.InventoryItemId == itemB.Id).Quantity);
            Assert.All(movements, m => Assert.Equal(InventoryMovementType.Outbound, m.MovementType));
            Assert.All(movements, m => Assert.Equal(order.Id, m.OrderId));

            Assert.Equal(OrderSalesStage.Completed, (await ctx.Orders.GetByIdAsync(order.Id))!.SalesStage);
        });
    }

    [Fact]
    public async Task Non_linked_lines_neither_block_nor_deduct()
    {
        await WithServiceAsync(async ctx =>
        {
            InventoryItem item = await ctx.CreateItemAsync(quantity: 5m);
            Order order = await ctx.CreateOrderAsync(customerId: null);

            await ctx.AddLineAsync(order, item, quantity: 2m);
            // A service / custom line with no inventory link must not participate.
            await ctx.AddLineAsync(order, inventoryItem: null, quantity: 99m);

            OrderCompletionResult result = await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);

            Assert.True(result.IsCompleted);
            Assert.Equal(3m, (await ctx.Items.GetByIdAsync(item.Id))!.QuantityAvailable);
            Assert.Single(await ctx.Movements.GetAllAsync());
        });
    }

    [Fact]
    public async Task Insufficient_inventory_rejects_and_rolls_everything_back()
    {
        await WithServiceAsync(async ctx =>
        {
            InventoryItem plentiful = await ctx.CreateItemAsync(quantity: 100m);
            InventoryItem scarce = await ctx.CreateItemAsync(quantity: 1m);
            Customer customer = await ctx.CreateCustomerAsync();
            Order order = await ctx.CreateOrderAsync(customerId: customer.Id, total: 250m);

            await ctx.AddLineAsync(order, plentiful, quantity: 5m);
            // Aggregated requirement (2 + 3 = 5) exceeds the scarce item's availability (1).
            await ctx.AddLineAsync(order, scarce, quantity: 2m);
            await ctx.AddLineAsync(order, scarce, quantity: 3m);

            OrderCompletionResult result = await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);

            Assert.True(result.IsInsufficientInventory);
            InventoryShortfall shortfall = Assert.Single(result.Shortfalls);
            Assert.Equal(scarce.Id, shortfall.InventoryItemId);
            Assert.Equal(5m, shortfall.RequiredQuantity);
            Assert.Equal(1m, shortfall.QuantityAvailable);

            // Nothing changed: no deduction (even for the plentiful item), no movement, order not
            // completed, customer statistics untouched (Req 4.7, 18.3).
            Assert.Equal(100m, (await ctx.Items.GetByIdAsync(plentiful.Id))!.QuantityAvailable);
            Assert.Equal(1m, (await ctx.Items.GetByIdAsync(scarce.Id))!.QuantityAvailable);
            Assert.Empty(await ctx.Movements.GetAllAsync());

            Order reloaded = (await ctx.Orders.GetByIdAsync(order.Id))!;
            Assert.NotEqual(OrderSalesStage.Completed, reloaded.SalesStage);

            Customer reloadedCustomer = (await ctx.Customers.GetByIdAsync(customer.Id))!;
            Assert.Equal(0, reloadedCustomer.CompletedOrderCount);
            Assert.Equal(CommerceMoney.Zero, reloadedCustomer.TotalSpend);
            Assert.Null(reloadedCustomer.LastOrderAt);
        });
    }

    [Fact]
    public async Task Completion_updates_linked_customer_statistics()
    {
        await WithServiceAsync(async ctx =>
        {
            InventoryItem item = await ctx.CreateItemAsync(quantity: 10m);
            Customer customer = await ctx.CreateCustomerAsync();
            Order order = await ctx.CreateOrderAsync(customerId: customer.Id, total: 150m, orderedAt: CompletedAt.AddDays(-2));
            await ctx.AddLineAsync(order, item, quantity: 1m);

            await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);

            Customer reloaded = (await ctx.Customers.GetByIdAsync(customer.Id))!;
            Assert.Equal(1, reloaded.CompletedOrderCount);
            Assert.Equal(CommerceMoney.From(150m), reloaded.TotalSpend);
            Assert.Equal(order.OrderedAt, reloaded.LastOrderAt);
        });
    }

    [Fact]
    public async Task Re_running_completion_is_idempotent()
    {
        await WithServiceAsync(async ctx =>
        {
            InventoryItem item = await ctx.CreateItemAsync(quantity: 10m);
            Customer customer = await ctx.CreateCustomerAsync();
            Order order = await ctx.CreateOrderAsync(customerId: customer.Id, total: 100m);
            await ctx.AddLineAsync(order, item, quantity: 4m);

            OrderCompletionResult first = await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);
            OrderCompletionResult second = await ctx.Service.CompleteOrderAsync(order.Id, CompletedAt);

            Assert.True(first.IsCompleted);
            Assert.True(second.IsCompleted);

            // The deduction and movement are applied once, not twice.
            Assert.Equal(6m, (await ctx.Items.GetByIdAsync(item.Id))!.QuantityAvailable);
            Assert.Single(await ctx.Movements.GetAllAsync());

            Customer reloaded = (await ctx.Customers.GetByIdAsync(customer.Id))!;
            Assert.Equal(1, reloaded.CompletedOrderCount);
            Assert.Equal(CommerceMoney.From(100m), reloaded.TotalSpend);
        });
    }

    // --- Harness ---

    private static async Task WithServiceAsync(Func<CompletionContext, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-completion-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var orders = new CommerceOrderRepository(factory);
            var orderItems = new OrderItemRepository(factory);
            var items = new InventoryItemRepository(factory);
            var movements = new InventoryMovementRepository(factory);
            var customers = new CommerceCustomerRepository(factory);
            var service = new CommerceOrderService(factory, orders, orderItems, items, movements, customers);

            await body(new CompletionContext(service, orders, orderItems, items, movements, customers, Guid.NewGuid()));
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

    private sealed class CompletionContext
    {
        public CompletionContext(
            CommerceOrderService service,
            ICommerceOrderRepository orders,
            IOrderItemRepository orderItems,
            IInventoryItemRepository items,
            IInventoryMovementRepository movements,
            ICommerceCustomerRepository customers,
            Guid workspaceId)
        {
            Service = service;
            Orders = orders;
            OrderItems = orderItems;
            Items = items;
            Movements = movements;
            Customers = customers;
            WorkspaceId = workspaceId;
        }

        public CommerceOrderService Service { get; }
        public ICommerceOrderRepository Orders { get; }
        public IOrderItemRepository OrderItems { get; }
        public IInventoryItemRepository Items { get; }
        public IInventoryMovementRepository Movements { get; }
        public ICommerceCustomerRepository Customers { get; }
        public Guid WorkspaceId { get; }

        public Task<InventoryItem> CreateItemAsync(decimal quantity)
            => Items.CreateAsync(new InventoryItem
            {
                Id = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                Name = "库存项 " + Guid.NewGuid().ToString("N")[..6],
                QuantityAvailable = quantity,
                ReorderThreshold = 0m,
            });

        public Task<Customer> CreateCustomerAsync()
            => Customers.CreateAsync(new Customer
            {
                Id = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                Name = "客户 " + Guid.NewGuid().ToString("N")[..6],
            });

        public Task<Order> CreateOrderAsync(Guid? customerId, decimal total = 0m, DateTime? orderedAt = null)
            => Orders.CreateAsync(new Order
            {
                Id = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                CustomerId = customerId,
                SalesStage = OrderSalesStage.Confirmed,
                Total = CommerceMoney.From(total),
                OrderedAt = orderedAt ?? CompletedAt.AddDays(-1),
            });

        public Task<OrderItem> AddLineAsync(Order order, InventoryItem? inventoryItem, decimal quantity)
            => OrderItems.CreateAsync(new OrderItem
            {
                Id = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                OrderId = order.Id,
                InventoryItemId = inventoryItem?.Id,
                Quantity = quantity,
                UnitPrice = CommerceMoney.From(1m),
                UnitCost = CommerceMoney.From(1m),
            });
    }
}
