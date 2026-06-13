using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Inventory;

/// <summary>
/// Property-based test for order completion in
/// <see cref="CommerceOrderService.CompleteOrderAsync"/> (Task 7.6).
///
/// Property 10: Order completion aggregates per <c>InventoryItemId</c> and is all-or-nothing.
/// For ANY order containing a mix of inventory-linked and non-linked order items, the required
/// quantity is aggregated per <c>InventoryItemId</c> across all inventory-linked lines:
/// <list type="bullet">
///   <item><description>if every aggregated quantity is ≤ that item's <c>QuantityAvailable</c>,
///   completion applies <b>exactly one</b> deduction per <c>InventoryItemId</c> equal to its
///   aggregate (non-linked lines neither block completion nor incur a deduction) and updates the
///   linked customer's statistics within the Core_Write_Transaction;</description></item>
///   <item><description>otherwise the completion is rejected, the transaction is rolled back so
///   <b>all</b> inventory quantities and customer statistics are unchanged, and an
///   insufficient-inventory error is returned.</description></item>
/// </list>
///
/// The property is exercised end-to-end against the real SQLCipher-backed Commerce repositories (an
/// unencrypted temp database, no mocks). Each generated case builds a fresh order over a fixed pool
/// of inventory items with random availabilities, then attaches a random set of lines — some linked
/// to an item (possibly several lines to the same item, forcing aggregation) and some non-linked
/// (<c>InventoryItemId == null</c>). Availabilities and line quantities are chosen so generation
/// routinely produces both the "all aggregates fit" (success) and the "at least one aggregate
/// exceeds availability" (rollback) branches.
///
/// **Validates: Requirements 4.6, 4.7, 4.16, 4.17, 18.4**
/// </summary>
public sealed class OrderCompletionAggregationRollbackPropertyTests
{
    private static readonly DateTime CompletedAt = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OrderedAt = CompletedAt.AddDays(-1);

    /// <summary>Fixed pool of inventory items a generated order may reference.</summary>
    private const int ItemCount = 4;

    /// <summary>A single generated order line. <see cref="ItemIndex"/> == -1 means a non-linked line.</summary>
    private readonly record struct LineSpec(int ItemIndex, decimal Quantity);

    /// <summary>A complete generated completion scenario.</summary>
    private sealed record CaseSpec(
        decimal[] Availabilities,
        LineSpec[] Lines,
        bool HasCustomer,
        decimal OrderTotal);

    // Availabilities include 0 so an order line can exceed even an item that exists but is empty.
    private static readonly Gen<decimal> AvailabilityGen = Gen.Int[0, 12].Select(v => (decimal)v);

    // Lines reference one of the ItemCount items, or -1 for a non-linked (service/custom) line.
    // Quantities are >= 1 so every linked aggregate is strictly positive.
    private static readonly Gen<LineSpec> LineGen =
        Gen.Select(Gen.Int[-1, ItemCount - 1], Gen.Int[1, 8],
            (idx, qty) => new LineSpec(idx, (decimal)qty));

    private static readonly Gen<CaseSpec> CaseGen =
        from availabilities in AvailabilityGen.Array[ItemCount]
        from lines in LineGen.Array[0, 8]
        from hasCustomer in Gen.Bool
        from total in Gen.Int[0, 5_000]
        select new CaseSpec(availabilities, lines, hasCustomer, (decimal)total);

    [Fact]
    public void Property10_completion_aggregates_per_inventory_item_and_is_all_or_nothing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-completion-pbt-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

            var orders = new CommerceOrderRepository(factory);
            var orderItems = new OrderItemRepository(factory);
            var items = new InventoryItemRepository(factory);
            var movements = new InventoryMovementRepository(factory);
            var customers = new CommerceCustomerRepository(factory);
            var service = new CommerceOrderService(factory, orders, orderItems, items, movements, customers);

            CaseGen.Sample(
                spec =>
                {
                    Guid workspaceId = Guid.NewGuid();

                    // --- Arrange: items, optional customer, order, and lines (no mocks). ---
                    Guid[] itemIds = new Guid[ItemCount];
                    for (int i = 0; i < ItemCount; i++)
                    {
                        var item = new InventoryItem
                        {
                            Id = Guid.NewGuid(),
                            WorkspaceId = workspaceId,
                            Name = "库存项 " + Guid.NewGuid().ToString("N")[..6],
                            QuantityAvailable = spec.Availabilities[i],
                            ReorderThreshold = 0m,
                        };
                        items.CreateAsync(item).GetAwaiter().GetResult();
                        itemIds[i] = item.Id;
                    }

                    Guid? customerId = null;
                    if (spec.HasCustomer)
                    {
                        var customer = new Customer
                        {
                            Id = Guid.NewGuid(),
                            WorkspaceId = workspaceId,
                            Name = "客户 " + Guid.NewGuid().ToString("N")[..6],
                        };
                        customers.CreateAsync(customer).GetAwaiter().GetResult();
                        customerId = customer.Id;
                    }

                    var order = new Order
                    {
                        Id = Guid.NewGuid(),
                        WorkspaceId = workspaceId,
                        CustomerId = customerId,
                        SalesStage = OrderSalesStage.Confirmed,
                        Total = CommerceMoney.From(spec.OrderTotal),
                        OrderedAt = OrderedAt,
                    };
                    orders.CreateAsync(order).GetAwaiter().GetResult();

                    foreach (LineSpec line in spec.Lines)
                    {
                        orderItems.CreateAsync(new OrderItem
                        {
                            Id = Guid.NewGuid(),
                            WorkspaceId = workspaceId,
                            OrderId = order.Id,
                            InventoryItemId = line.ItemIndex == -1 ? null : itemIds[line.ItemIndex],
                            Quantity = line.Quantity,
                            UnitPrice = CommerceMoney.From(1m),
                            UnitCost = CommerceMoney.From(1m),
                        }).GetAwaiter().GetResult();
                    }

                    // --- Expected model: aggregate required quantity per InventoryItemId. ---
                    Dictionary<int, decimal> requiredByIndex = new();
                    foreach (LineSpec line in spec.Lines)
                    {
                        if (line.ItemIndex == -1)
                        {
                            continue; // Non-linked line: neither aggregated nor deducted.
                        }

                        requiredByIndex.TryGetValue(line.ItemIndex, out decimal running);
                        requiredByIndex[line.ItemIndex] = running + line.Quantity;
                    }

                    bool expectedSuccess = requiredByIndex.All(
                        kvp => kvp.Value <= spec.Availabilities[kvp.Key]);

                    // --- Act. ---
                    OrderCompletionResult result =
                        service.CompleteOrderAsync(order.Id, CompletedAt).GetAwaiter().GetResult();

                    // --- Assert. ---
                    List<InventoryMovement> orderMovements = movements.GetAllAsync().GetAwaiter().GetResult()
                        .Where(m => m.OrderId == order.Id)
                        .ToList();
                    Order reloadedOrder = orders.GetByIdAsync(order.Id).GetAwaiter().GetResult()!;

                    if (expectedSuccess)
                    {
                        Assert.True(result.IsCompleted);
                        Assert.False(result.IsInsufficientInventory);

                        // Exactly one deduction + one outbound movement per DISTINCT referenced item,
                        // each equal to that item's aggregate (Req 4.16, 4.17).
                        Assert.Equal(requiredByIndex.Count, orderMovements.Count);
                        for (int i = 0; i < ItemCount; i++)
                        {
                            decimal aggregate = requiredByIndex.TryGetValue(i, out decimal r) ? r : 0m;
                            decimal expected = spec.Availabilities[i] - aggregate;
                            Assert.Equal(expected, items.GetByIdAsync(itemIds[i]).GetAwaiter().GetResult()!.QuantityAvailable);

                            if (aggregate > 0m)
                            {
                                InventoryMovement movement =
                                    Assert.Single(orderMovements, m => m.InventoryItemId == itemIds[i]);
                                Assert.Equal(aggregate, movement.Quantity);
                                Assert.Equal(InventoryMovementType.Outbound, movement.MovementType);
                            }
                            else
                            {
                                Assert.DoesNotContain(orderMovements, m => m.InventoryItemId == itemIds[i]);
                            }
                        }

                        Assert.Equal(OrderSalesStage.Completed, reloadedOrder.SalesStage);

                        if (spec.HasCustomer)
                        {
                            Customer reloaded = customers.GetByIdAsync(customerId!.Value).GetAwaiter().GetResult()!;
                            Assert.Equal(1, reloaded.CompletedOrderCount);
                            Assert.Equal(CommerceMoney.From(spec.OrderTotal), reloaded.TotalSpend);
                            Assert.Equal(order.OrderedAt, reloaded.LastOrderAt);
                        }
                    }
                    else
                    {
                        Assert.True(result.IsInsufficientInventory);
                        Assert.False(result.IsCompleted);

                        // Rolled back: every inventory quantity is unchanged and no movement exists
                        // for this order (Req 4.7, 18.4).
                        Assert.Empty(orderMovements);
                        for (int i = 0; i < ItemCount; i++)
                        {
                            Assert.Equal(
                                spec.Availabilities[i],
                                items.GetByIdAsync(itemIds[i]).GetAwaiter().GetResult()!.QuantityAvailable);
                        }

                        Assert.NotEqual(OrderSalesStage.Completed, reloadedOrder.SalesStage);

                        // Shortfalls correspond exactly to the over-aggregated items.
                        HashSet<Guid> expectedShortfallItems = requiredByIndex
                            .Where(kvp => kvp.Value > spec.Availabilities[kvp.Key])
                            .Select(kvp => itemIds[kvp.Key])
                            .ToHashSet();
                        Assert.Equal(expectedShortfallItems, result.Shortfalls.Select(s => s.InventoryItemId).ToHashSet());
                        foreach (InventoryShortfall shortfall in result.Shortfalls)
                        {
                            int index = Array.IndexOf(itemIds, shortfall.InventoryItemId);
                            Assert.Equal(requiredByIndex[index], shortfall.RequiredQuantity);
                            Assert.Equal(spec.Availabilities[index], shortfall.QuantityAvailable);
                        }

                        // Customer statistics are untouched on rollback (Req 4.7).
                        if (spec.HasCustomer)
                        {
                            Customer reloaded = customers.GetByIdAsync(customerId!.Value).GetAwaiter().GetResult()!;
                            Assert.Equal(0, reloaded.CompletedOrderCount);
                            Assert.Equal(CommerceMoney.Zero, reloaded.TotalSpend);
                            Assert.Null(reloaded.LastOrderAt);
                        }
                    }
                },
                iter: PbtConfig.MinIterations);
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
