using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based test for the Business_Key idempotency guarantee of the Commerce Service Layer
/// core writes (the shared <c>BusinessKeyIdempotency</c> helper used by
/// <see cref="CommercePaymentService"/>, <see cref="CommerceCashFlowService"/>,
/// <see cref="CommerceOrderService"/> completion, <see cref="CommerceBusinessInsightService"/>, and
/// <see cref="CommerceDashboardService"/>).
///
/// <para><b>Property 14: Core writes are idempotent by Business_Key.</b>
/// For ANY core write operation that generates a <see cref="PaymentRecord"/>,
/// <see cref="CashFlowEntry"/>, <see cref="InventoryMovement"/>, <see cref="BusinessInsight"/>, or
/// <see cref="BusinessMetricSnapshot"/> for which a Business_Key is defined, executing that operation
/// two or more times produces the same set of generated records as executing it once. Re-running a
/// completion or payment reuses the existing records and creates no duplicate financial, inventory,
/// or insight records.</para>
///
/// <para>The property is exercised end-to-end against the real SQLCipher-backed Commerce repositories
/// (an unencrypted temp database, no mocks). Each generated case runs against its own freshly
/// initialized database so the asserted record counts are absolute, then runs every one of the five
/// idempotent core writes <c>reps</c> (≥ 2) times and asserts that exactly one — or, for metric
/// snapshots, exactly the single per-metric set — of each generated record exists afterward, and that
/// the single inventory deduction is applied only once.</para>
///
/// **Validates: Requirements 4.19, 4.20, 18.5, 18.6**
/// </summary>
public sealed class BusinessKeyIdempotencyPropertyTests
{
    private static readonly DateTime AsOf = new(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The number of metric-snapshot records a single dashboard refresh captures per workspace/day.</summary>
    private const int SnapshotMetricCount = 10;

    /// <summary>A single generated idempotency scenario: how many times to re-run, and the amounts/quantities used.</summary>
    private readonly record struct Scenario(
        int Reps,
        int PaymentCents,
        int CashFlowCents,
        int ReceivableCents,
        decimal OrderQuantity,
        decimal ExtraStock);

    // Re-run each operation between 2 and 4 times; a single run is the baseline the result must match.
    private static readonly Gen<int> RepsGen = Gen.Int[2, 4];

    // Monetary amounts in cents stay well inside the CommerceMoney range and keep scale 2.
    private static readonly Gen<int> CentsGen = Gen.Int[0, 100_000_000];

    // Order line quantity 0.01 .. 50.00 (2 dp) and strictly-positive spare stock so the item is never
    // out of stock or low on stock after the single deduction (avoids unrelated inventory insights).
    private static readonly Gen<decimal> OrderQuantityGen =
        Gen.Int[1, 5_000].Select(hundredths => hundredths / 100m);
    private static readonly Gen<decimal> ExtraStockGen =
        Gen.Int[1, 5_000].Select(hundredths => hundredths / 100m);

    private static readonly Gen<Scenario> ScenarioGen =
        RepsGen.Select(CentsGen, CentsGen, CentsGen, OrderQuantityGen, ExtraStockGen,
            (reps, pay, cash, recv, qty, stock) => new Scenario(reps, pay, cash, recv, qty, stock));

    [Fact]
    public void Property14_core_writes_are_idempotent_by_business_key()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                // Each generated case runs against its own isolated database so the asserted record
                // counts are absolute and independent of any other generated case.
                string path = Path.Combine(Path.GetTempPath(), $"orderly-idempotency-{Guid.NewGuid():N}.db");
                try
                {
                    var factory = new SqliteConnectionFactory(path);
                    new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

                    var orders = new CommerceOrderRepository(factory);
                    var orderItems = new OrderItemRepository(factory);
                    var inventoryItems = new InventoryItemRepository(factory);
                    var inventoryMovements = new InventoryMovementRepository(factory);
                    var customers = new CommerceCustomerRepository(factory);
                    var payments = new PaymentRecordRepository(factory);
                    var cashFlows = new CashFlowEntryRepository(factory);
                    var insights = new BusinessInsightRepository(factory);
                    var snapshots = new BusinessMetricSnapshotRepository(factory);

                    var paymentService = new CommercePaymentService(payments, cashFlows);
                    var cashFlowService = new CommerceCashFlowService(cashFlows);
                    var inventoryService = new CommerceInventoryService(inventoryItems, inventoryMovements);
                    var orderService = new CommerceOrderService(
                        factory, orders, orderItems, inventoryItems, inventoryMovements, customers);
                    var insightService = new CommerceBusinessInsightService(
                        inventoryService, cashFlows, reservedProviders: null, insightRepository: insights);
                    var dashboardService = new CommerceDashboardService(
                        orders, cashFlows, inventoryItems, customers, snapshots);

                    Guid workspaceId = Guid.NewGuid();

                    AssertPaymentIdempotent(paymentService, payments, cashFlows, workspaceId, scenario);
                    AssertCashFlowIdempotent(cashFlowService, cashFlows, workspaceId, scenario);
                    AssertInventoryMovementIdempotent(orderService, orders, orderItems, inventoryItems, inventoryMovements, workspaceId, scenario);
                    AssertInsightIdempotent(insightService, cashFlows, insights, workspaceId, scenario);
                    AssertMetricSnapshotIdempotent(dashboardService, snapshots, workspaceId, scenario);
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
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// Re-recording a payment that carries the same Business_Key produces exactly one
    /// <see cref="PaymentRecord"/> and exactly one generated <see cref="CashFlowEntry"/> (Req 4.20, 18.6).
    /// </summary>
    private static void AssertPaymentIdempotent(
        CommercePaymentService service,
        PaymentRecordRepository payments,
        CashFlowEntryRepository cashFlows,
        Guid workspaceId,
        Scenario scenario)
    {
        const string paymentKey = "idempotency:payment";
        const string generatedEntryKey = "payment-cashflow:idempotency:payment";
        CommerceMoney amount = CommerceMoney.From(scenario.PaymentCents / 100m);

        for (int i = 0; i < scenario.Reps; i++)
        {
            var payment = new PaymentRecord
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Amount = amount,
                PaidAt = AsOf,
                BusinessKey = paymentKey,
            };

            service.RecordPaymentAsync(payment, PaymentCashFlowOptions.Generate()).GetAwaiter().GetResult();
        }

        IReadOnlyList<PaymentRecord> allPayments = payments.GetAllAsync().GetAwaiter().GetResult();
        IReadOnlyList<CashFlowEntry> allEntries = cashFlows.GetAllAsync().GetAwaiter().GetResult();

        Assert.Single(allPayments.Where(p => p.BusinessKey == paymentKey));
        Assert.Single(allEntries.Where(e => e.BusinessKey == generatedEntryKey));
    }

    /// <summary>
    /// Re-recording an income entry that carries the same Business_Key produces exactly one
    /// <see cref="CashFlowEntry"/> (Req 4.20, 18.6).
    /// </summary>
    private static void AssertCashFlowIdempotent(
        CommerceCashFlowService service,
        CashFlowEntryRepository cashFlows,
        Guid workspaceId,
        Scenario scenario)
    {
        const string cashFlowKey = "idempotency:cashflow-income";
        CommerceMoney amount = CommerceMoney.From(scenario.CashFlowCents / 100m);

        for (int i = 0; i < scenario.Reps; i++)
        {
            var input = new CashFlowEntryInput
            {
                WorkspaceId = workspaceId,
                Amount = amount,
                OccurredAt = AsOf,
                CategoryName = "收入分类 A",
                BusinessKey = cashFlowKey,
            };

            service.RecordIncomeAsync(input).GetAwaiter().GetResult();
        }

        IReadOnlyList<CashFlowEntry> allEntries = cashFlows.GetAllAsync().GetAwaiter().GetResult();
        Assert.Single(allEntries.Where(e => e.BusinessKey == cashFlowKey));
    }

    /// <summary>
    /// Re-running order completion produces exactly one outbound <see cref="InventoryMovement"/> per
    /// inventory item and deducts the stocked quantity exactly once (Req 4.19, 4.20, 18.6).
    /// </summary>
    private static void AssertInventoryMovementIdempotent(
        CommerceOrderService service,
        CommerceOrderRepository orders,
        OrderItemRepository orderItems,
        InventoryItemRepository inventoryItems,
        InventoryMovementRepository inventoryMovements,
        Guid workspaceId,
        Scenario scenario)
    {
        decimal orderQuantity = scenario.OrderQuantity;
        decimal initialStock = orderQuantity + scenario.ExtraStock;

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = "库存项 A",
            QuantityAvailable = initialStock,
            ReorderThreshold = 0m,
        };
        inventoryItems.CreateAsync(item).GetAwaiter().GetResult();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OrderedAt = AsOf,
        };
        orders.CreateAsync(order).GetAwaiter().GetResult();

        var line = new OrderItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OrderId = order.Id,
            InventoryItemId = item.Id,
            Quantity = orderQuantity,
            UnitPrice = CommerceMoney.From(10.00m),
            UnitCost = CommerceMoney.From(6.00m),
        };
        orderItems.CreateAsync(line).GetAwaiter().GetResult();

        for (int i = 0; i < scenario.Reps; i++)
        {
            OrderCompletionResult result = service.CompleteOrderAsync(order.Id, AsOf).GetAwaiter().GetResult();
            Assert.True(result.IsCompleted, "Order completion with sufficient stock should succeed on every run.");
        }

        string movementKey = $"order-completion:{order.Id:N}:{item.Id:N}";
        IReadOnlyList<InventoryMovement> allMovements = inventoryMovements.GetAllAsync().GetAwaiter().GetResult();
        Assert.Single(allMovements.Where(m => m.BusinessKey == movementKey));

        // The single deduction was applied exactly once: the stock fell by exactly the ordered amount.
        InventoryItem itemAfter = inventoryItems.GetByIdAsync(item.Id).GetAwaiter().GetResult()!;
        Assert.Equal(initialStock - orderQuantity, itemAfter.QuantityAvailable);
    }

    /// <summary>
    /// Re-running insight persistence over the same overdue receivable produces exactly one
    /// <see cref="BusinessInsight"/> for that entry (Req 4.20, 18.6).
    /// </summary>
    private static void AssertInsightIdempotent(
        CommerceBusinessInsightService service,
        CashFlowEntryRepository cashFlows,
        BusinessInsightRepository insights,
        Guid workspaceId,
        Scenario scenario)
    {
        // An unsettled receivable (income) whose due date is already past at AsOf raises exactly one
        // overdue insight, keyed by the entry id, on every generation.
        var overdue = new CashFlowEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Income,
            Amount = CommerceMoney.From(scenario.ReceivableCents / 100m),
            SettledAmount = CommerceMoney.Zero,
            SettlementStatus = CashFlowSettlementStatus.Pending,
            OccurredAt = AsOf.AddDays(-10),
            DueDate = AsOf.AddDays(-5),
        };
        cashFlows.CreateAsync(overdue).GetAwaiter().GetResult();

        string insightKey = $"cashflow:receivable-overdue:{overdue.Id}";

        for (int i = 0; i < scenario.Reps; i++)
        {
            service.PersistInsightsAsync(AsOf).GetAwaiter().GetResult();
        }

        IReadOnlyList<BusinessInsight> allInsights = insights.GetAllAsync().GetAwaiter().GetResult();
        Assert.Single(allInsights.Where(insight => insight.BusinessKey == insightKey));
    }

    /// <summary>
    /// Re-running the dashboard metric-snapshot capture for the same workspace and day produces
    /// exactly one snapshot per metric — no duplicates across re-runs (Req 4.20, 18.6).
    /// </summary>
    private static void AssertMetricSnapshotIdempotent(
        CommerceDashboardService service,
        BusinessMetricSnapshotRepository snapshots,
        Guid workspaceId,
        Scenario scenario)
    {
        for (int i = 0; i < scenario.Reps; i++)
        {
            service.PersistMetricSnapshotsAsync(workspaceId, AsOf).GetAwaiter().GetResult();
        }

        IReadOnlyList<BusinessMetricSnapshot> all = snapshots.GetAllAsync().GetAwaiter().GetResult();
        List<BusinessMetricSnapshot> forWorkspace = all.Where(s => s.WorkspaceId == workspaceId).ToList();

        // Exactly one snapshot per captured metric for this (workspace, day) — re-runs add no duplicates.
        Assert.Equal(SnapshotMetricCount, forWorkspace.Count);
        Assert.Equal(SnapshotMetricCount, forWorkspace.Select(s => s.BusinessKey).Distinct().Count());
    }
}
