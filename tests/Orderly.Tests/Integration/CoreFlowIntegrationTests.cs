using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Integration;

/// <summary>
/// End-to-end integration test for the <c>Core_Flow</c> (Req 16.9, 16.10): the 13-step runnable
/// flow that exercises the real Commerce Service Layer over a temp SQLite database with no mocks —
/// create customer → create product → create inventory item → record inbound movement → create order
/// → add order item → record payment → advance fulfillment → complete order → deduct inventory →
/// generate cash flow → refresh workbench metrics → generate insights.
///
/// <para>The flow is executed by a small <see cref="CoreFlowRunner"/> harness that runs the steps in
/// the required order and, on the first failing step, <b>halts</b> the flow, reports which step
/// failed, and leaves the data established by the preceding steps intact (halt-and-preserve, Req
/// 16.10). Each step asserts the observable outcome it is responsible for — inventory deducted, cash
/// flow generated, workbench (dashboard) metrics refreshed, and insights generated.</para>
///
/// <para>Every step uses the production Commerce service implementations
/// (<see cref="CommerceProductService"/>, <see cref="CommerceInventoryService"/>,
/// <see cref="CommerceOrderService"/>, <see cref="CommercePaymentService"/>,
/// <see cref="CommerceCashFlowService"/>, <see cref="CommerceDashboardService"/>,
/// <see cref="CommerceBusinessInsightService"/>, <see cref="CommerceCustomerService"/>) and their
/// SQLCipher-backed repositories over an unencrypted temp database initialized by
/// <see cref="CommerceSchemaInitializer"/>.</para>
/// </summary>
public sealed class CoreFlowIntegrationTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    // --- Fixed quantities/prices so the asserted outcomes are deterministic ---
    private const decimal InboundQuantity = 10m;   // stock received
    private const decimal OrderQuantity = 8m;       // stock sold on the order
    private const decimal ReorderThreshold = 5m;    // low-stock threshold (post-sale qty 2 ≤ 5)
    private const decimal UnitPrice = 100m;
    private const decimal UnitCost = 60m;
    private const decimal OrderTotal = OrderQuantity * UnitPrice;       // 800
    private const decimal OrderGrossProfit = OrderQuantity * (UnitPrice - UnitCost); // 320
    private const decimal RemainingStock = InboundQuantity - OrderQuantity; // 2

    [Fact]
    public async Task Core_flow_runs_all_13_steps_end_to_end()
    {
        await WithEnvironmentAsync(async env =>
        {
            var runner = new CoreFlowRunner();
            Guid customerId = Guid.NewGuid();
            Guid productId = Guid.NewGuid();
            Guid inventoryItemId = Guid.NewGuid();
            Guid orderId = Guid.NewGuid();

            // The order and its line are kept in scope so the recalculation/transition steps operate
            // on the same in-memory aggregate the persistence steps wrote.
            Order order = null!;
            List<OrderItem> orderItems = new();

            // 1) Create customer.
            await runner.StepAsync(1, "create customer", async () =>
            {
                await env.CustomerRepository.CreateAsync(new Customer
                {
                    Id = customerId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试客户",
                    Phone = "13800000000",
                });

                Customer? persisted = await env.CustomerRepository.GetByIdAsync(customerId);
                Assert.NotNull(persisted);
                Assert.Equal("测试客户", persisted!.Name);
            });

            // 2) Create product (through the product service).
            await runner.StepAsync(2, "create product", async () =>
            {
                await env.ProductService.CreateAsync(new Product
                {
                    Id = productId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试产品",
                    DefaultPrice = CommerceMoney.From(UnitPrice),
                    DefaultCost = CommerceMoney.From(UnitCost),
                });

                Assert.NotNull(await env.ProductService.GetByIdAsync(productId));
            });

            // 3) Create inventory item (starts empty).
            await runner.StepAsync(3, "create inventory item", async () =>
            {
                await env.InventoryItemRepository.CreateAsync(new InventoryItem
                {
                    Id = inventoryItemId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试库存项",
                    ProductId = productId,
                    QuantityAvailable = 0m,
                    ReorderThreshold = ReorderThreshold,
                    UnitCost = CommerceMoney.From(UnitCost),
                });

                Assert.NotNull(await env.InventoryItemRepository.GetByIdAsync(inventoryItemId));
            });

            // 4) Record an inbound movement; the item's available quantity rises by the inbound qty.
            await runner.StepAsync(4, "record inbound movement", async () =>
            {
                InventoryItem updated = await env.InventoryService.RecordMovementAsync(new InventoryMovement
                {
                    WorkspaceId = env.WorkspaceId,
                    InventoryItemId = inventoryItemId,
                    MovementType = InventoryMovementType.Inbound,
                    Quantity = InboundQuantity,
                    OccurredAt = AsOf,
                });

                Assert.Equal(InboundQuantity, updated.QuantityAvailable);
            });

            // 5) Create the order (confirmed sale, linked to the customer).
            await runner.StepAsync(5, "create order", async () =>
            {
                order = new Order
                {
                    Id = orderId,
                    WorkspaceId = env.WorkspaceId,
                    CustomerId = customerId,
                    OrderNo = "SO-0001",
                    SalesStage = OrderSalesStage.Confirmed,
                    OrderedAt = AsOf,
                };
                await env.OrderRepository.CreateAsync(order);

                Assert.NotNull(await env.OrderRepository.GetByIdAsync(orderId));
            });

            // 6) Add an order line drawing from the inventory item, then recalculate the order totals.
            await runner.StepAsync(6, "add order item", async () =>
            {
                await env.OrderItemRepository.CreateAsync(new OrderItem
                {
                    WorkspaceId = env.WorkspaceId,
                    OrderId = orderId,
                    ProductId = productId,
                    InventoryItemId = inventoryItemId,
                    Quantity = OrderQuantity,
                    UnitPrice = CommerceMoney.From(UnitPrice),
                    UnitCost = CommerceMoney.From(UnitCost),
                });

                orderItems = (await env.OrderItemRepository.GetAllAsync())
                    .Where(i => i.OrderId == orderId)
                    .ToList();

                env.OrderService.RecalculateOrder(order, orderItems, Array.Empty<PaymentRecord>());
                await env.OrderRepository.UpdateAsync(order);

                Assert.Equal(OrderTotal, order.Total.Amount);
                Assert.Equal(OrderGrossProfit, order.GrossProfit.Amount);
                Assert.Equal(OrderTotal, order.ReceivableAmount.Amount);
            });

            // 7) Record the payment, then recalculate so paid/receivable reflect it.
            await runner.StepAsync(7, "record payment", async () =>
            {
                PaymentResult result = await env.PaymentService.RecordPaymentAsync(
                    new PaymentRecord
                    {
                        WorkspaceId = env.WorkspaceId,
                        OrderId = orderId,
                        Amount = CommerceMoney.From(OrderTotal),
                        PaidAt = AsOf,
                        Method = "现金",
                    },
                    PaymentCashFlowOptions.None);

                Assert.NotNull(result.Payment);

                List<PaymentRecord> payments = (await env.PaymentRepository.GetAllAsync())
                    .Where(p => p.OrderId == orderId)
                    .ToList();

                env.OrderService.RecalculateOrder(order, orderItems, payments);
                await env.OrderRepository.UpdateAsync(order);

                Assert.Equal(OrderTotal, order.PaidAmount.Amount);
                Assert.Equal(0m, order.ReceivableAmount.Amount);
            });

            // 8) Advance the fulfillment dimension (NotStarted -> InProgress) via the active workflow.
            await runner.StepAsync(8, "advance fulfillment", async () =>
            {
                var workflow = new OrderWorkflowConfiguration
                {
                    Transitions =
                    [
                        new OrderStageTransition
                        {
                            FromFulfillmentStage = OrderFulfillmentStage.NotStarted,
                            ToFulfillmentStage = OrderFulfillmentStage.InProgress,
                        },
                    ],
                };

                OrderStageTransitionResult result = env.OrderService.ApplyStageTransition(
                    order,
                    new OrderStageTransitionRequest { TargetFulfillmentStage = OrderFulfillmentStage.InProgress },
                    workflow);

                Assert.True(result.IsApplied, $"Expected fulfillment transition to apply, got {result.Outcome}.");
                await env.OrderRepository.UpdateAsync(order);

                Assert.Equal(OrderFulfillmentStage.InProgress, order.FulfillmentStage);
            });

            // 9) Complete the order (atomic inventory deduction + customer-stats refresh).
            await runner.StepAsync(9, "complete order", async () =>
            {
                OrderCompletionResult completion = await env.OrderService.CompleteOrderAsync(orderId, AsOf);

                Assert.True(
                    completion.IsCompleted,
                    $"Expected order completion to succeed, got {completion.Outcome}.");

                Order? persisted = await env.OrderRepository.GetByIdAsync(orderId);
                Assert.Equal(OrderSalesStage.Completed, persisted!.SalesStage);
            });

            // 10) Inventory deducted: the item dropped by the order quantity and one outbound movement exists.
            await runner.StepAsync(10, "deduct inventory", async () =>
            {
                InventoryItem? itemAfter = await env.InventoryItemRepository.GetByIdAsync(inventoryItemId);
                Assert.Equal(RemainingStock, itemAfter!.QuantityAvailable);

                List<InventoryMovement> outbound = (await env.InventoryMovementRepository.GetAllAsync())
                    .Where(m => m.InventoryItemId == inventoryItemId
                        && m.MovementType == InventoryMovementType.Outbound)
                    .ToList();

                Assert.Single(outbound);
                Assert.Equal(OrderQuantity, outbound[0].Quantity);
            });

            // 11) Generate cash flow: record the recognized income for the completed order.
            await runner.StepAsync(11, "generate cash flow", async () =>
            {
                CashFlowEntry income = await env.CashFlowService.RecordIncomeAsync(new CashFlowEntryInput
                {
                    WorkspaceId = env.WorkspaceId,
                    Amount = CommerceMoney.From(OrderTotal),
                    OccurredAt = AsOf,
                    OrderId = orderId,
                    CategoryName = "销售收入",
                    BusinessKey = $"core-flow-income:{orderId:N}",
                });

                Assert.Equal(CashFlowDirection.Income, income.Direction);

                IReadOnlyList<CashFlowEntry> entries = await env.CashFlowRepository.GetAllAsync();
                Assert.Contains(entries, e => e.OrderId == orderId && e.Direction == CashFlowDirection.Income);
            });

            // 12) Refresh workbench (dashboard) metrics and persist the snapshot.
            await runner.StepAsync(12, "refresh workbench metrics", async () =>
            {
                IReadOnlyList<BusinessMetricSnapshot> snapshots =
                    await env.DashboardService.PersistMetricSnapshotsAsync(env.WorkspaceId, AsOf);

                Assert.NotEmpty(snapshots);

                BusinessMetricSnapshot revenueSnapshot = snapshots.Single(s => s.MetricKey == "total-revenue");
                Assert.Equal(OrderTotal, revenueSnapshot.MoneyValue!.Value.Amount);

                DashboardSnapshot snapshot = await env.DashboardService.GetSnapshotAsync(AsOf);
                Assert.Equal(1, snapshot.Metrics.CompletedOrders);
                Assert.Equal(OrderTotal, snapshot.Metrics.TotalRevenue.Amount);
                Assert.Equal(OrderTotal, snapshot.Metrics.CashInflow.Amount);
                Assert.Equal(1, snapshot.Metrics.LowStockItemCount);
            });

            // 13) Generate insights: the post-sale low-stock item raises a deterministic warning.
            await runner.StepAsync(13, "generate insights", async () =>
            {
                IReadOnlyList<BusinessInsight> insights = await env.InsightService.PersistInsightsAsync(AsOf);

                Assert.NotEmpty(insights);
                Assert.Contains(
                    insights,
                    i => i.BusinessKey == $"inventory:low-stock:{inventoryItemId}"
                        && i.Severity == InsightSeverity.Warning);
            });

            // Every step completed in order.
            Assert.Equal(13, runner.CompletedSteps);
        });
    }

    /// <summary>
    /// Validates the halt-and-preserve behavior (Req 16.10): when a step fails, the flow halts at that
    /// step, identifies it, and the data established by the preceding steps is preserved. Here step 5
    /// is forced to fail by completing a non-existent order; the test asserts the failure names step 5
    /// and that the customer, product, inventory item, and inbound movement from steps 1–4 survive.
    /// </summary>
    [Fact]
    public async Task Core_flow_halts_at_failing_step_and_preserves_prior_data()
    {
        await WithEnvironmentAsync(async env =>
        {
            var runner = new CoreFlowRunner();
            Guid customerId = Guid.NewGuid();
            Guid productId = Guid.NewGuid();
            Guid inventoryItemId = Guid.NewGuid();

            await runner.StepAsync(1, "create customer", () =>
                env.CustomerRepository.CreateAsync(new Customer
                {
                    Id = customerId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试客户",
                }));

            await runner.StepAsync(2, "create product", () =>
                env.ProductService.CreateAsync(new Product
                {
                    Id = productId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试产品",
                }));

            await runner.StepAsync(3, "create inventory item", () =>
                env.InventoryItemRepository.CreateAsync(new InventoryItem
                {
                    Id = inventoryItemId,
                    WorkspaceId = env.WorkspaceId,
                    Name = "测试库存项",
                    QuantityAvailable = 0m,
                    ReorderThreshold = ReorderThreshold,
                }));

            await runner.StepAsync(4, "record inbound movement", () =>
                env.InventoryService.RecordMovementAsync(new InventoryMovement
                {
                    WorkspaceId = env.WorkspaceId,
                    InventoryItemId = inventoryItemId,
                    MovementType = InventoryMovementType.Inbound,
                    Quantity = InboundQuantity,
                    OccurredAt = AsOf,
                }));

            // Force step 5 to fail: completing an order that was never created throws.
            CoreFlowStepException failure = await Assert.ThrowsAsync<CoreFlowStepException>(() =>
                runner.StepAsync(5, "create order", () =>
                    env.OrderService.CompleteOrderAsync(Guid.NewGuid(), AsOf)));

            // The flow halts at the failing step and identifies it.
            Assert.Equal(5, failure.StepNumber);
            Assert.Equal("create order", failure.StepName);
            Assert.Equal(4, runner.CompletedSteps);

            // The data established by steps 1–4 is preserved.
            Assert.NotNull(await env.CustomerRepository.GetByIdAsync(customerId));
            Assert.NotNull(await env.ProductService.GetByIdAsync(productId));
            InventoryItem? item = await env.InventoryItemRepository.GetByIdAsync(inventoryItemId);
            Assert.NotNull(item);
            Assert.Equal(InboundQuantity, item!.QuantityAvailable);
        });
    }

    // --- Core_Flow harness ---

    /// <summary>
    /// Runs Core_Flow steps in order. On the first step that throws, it wraps the cause in a
    /// <see cref="CoreFlowStepException"/> naming the failing step and stops advancing — implementing
    /// the halt-and-preserve contract (Req 16.10). <see cref="CompletedSteps"/> reflects the last
    /// successfully completed step number.
    /// </summary>
    private sealed class CoreFlowRunner
    {
        public int CompletedSteps { get; private set; }

        public async Task StepAsync(int number, string name, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                throw new CoreFlowStepException(number, name, ex);
            }

            CompletedSteps = number;
        }
    }

    /// <summary>Identifies the Core_Flow step at which the flow halted (Req 16.10).</summary>
    private sealed class CoreFlowStepException : Exception
    {
        public CoreFlowStepException(int stepNumber, string stepName, Exception inner)
            : base($"Core_Flow halted at step {stepNumber} ({stepName}): {inner.Message}", inner)
        {
            StepNumber = stepNumber;
            StepName = stepName;
        }

        public int StepNumber { get; }

        public string StepName { get; }
    }

    // --- Environment wiring (real services + repositories over a temp SQLite database) ---

    private sealed class CoreFlowEnvironment
    {
        public required Guid WorkspaceId { get; init; }
        public required CommerceCustomerRepository CustomerRepository { get; init; }
        public required ProductRepository ProductRepository { get; init; }
        public required InventoryItemRepository InventoryItemRepository { get; init; }
        public required InventoryMovementRepository InventoryMovementRepository { get; init; }
        public required CommerceOrderRepository OrderRepository { get; init; }
        public required OrderItemRepository OrderItemRepository { get; init; }
        public required PaymentRecordRepository PaymentRepository { get; init; }
        public required CashFlowEntryRepository CashFlowRepository { get; init; }

        public required CommerceProductService ProductService { get; init; }
        public required CommerceInventoryService InventoryService { get; init; }
        public required CommerceOrderService OrderService { get; init; }
        public required CommercePaymentService PaymentService { get; init; }
        public required CommerceCashFlowService CashFlowService { get; init; }
        public required CommerceDashboardService DashboardService { get; init; }
        public required CommerceBusinessInsightService InsightService { get; init; }
        public required CommerceCustomerService CustomerService { get; init; }
    }

    private static async Task WithEnvironmentAsync(Func<CoreFlowEnvironment, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-core-flow-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var customerRepository = new CommerceCustomerRepository(factory);
            var productRepository = new ProductRepository(factory);
            var inventoryItemRepository = new InventoryItemRepository(factory);
            var inventoryMovementRepository = new InventoryMovementRepository(factory);
            var orderRepository = new CommerceOrderRepository(factory);
            var orderItemRepository = new OrderItemRepository(factory);
            var paymentRepository = new PaymentRecordRepository(factory);
            var cashFlowRepository = new CashFlowEntryRepository(factory);
            var snapshotRepository = new BusinessMetricSnapshotRepository(factory);
            var insightRepository = new BusinessInsightRepository(factory);

            var inventoryService = new CommerceInventoryService(inventoryItemRepository, inventoryMovementRepository);

            var environment = new CoreFlowEnvironment
            {
                WorkspaceId = Guid.NewGuid(),
                CustomerRepository = customerRepository,
                ProductRepository = productRepository,
                InventoryItemRepository = inventoryItemRepository,
                InventoryMovementRepository = inventoryMovementRepository,
                OrderRepository = orderRepository,
                OrderItemRepository = orderItemRepository,
                PaymentRepository = paymentRepository,
                CashFlowRepository = cashFlowRepository,
                ProductService = new CommerceProductService(productRepository),
                InventoryService = inventoryService,
                // The dependency constructor supports CompleteOrderAsync as well as the stateless
                // RecalculateOrder / ApplyStageTransition operations used earlier in the flow.
                OrderService = new CommerceOrderService(
                    factory,
                    orderRepository,
                    orderItemRepository,
                    inventoryItemRepository,
                    inventoryMovementRepository,
                    customerRepository),
                PaymentService = new CommercePaymentService(paymentRepository, cashFlowRepository, factory),
                CashFlowService = new CommerceCashFlowService(cashFlowRepository),
                DashboardService = new CommerceDashboardService(
                    orderRepository,
                    cashFlowRepository,
                    inventoryItemRepository,
                    customerRepository,
                    snapshotRepository),
                InsightService = new CommerceBusinessInsightService(
                    inventoryService,
                    cashFlowRepository,
                    reservedProviders: null,
                    insightRepository: insightRepository),
                CustomerService = new CommerceCustomerService(orderRepository, customerRepository),
            };

            await body(environment);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
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
