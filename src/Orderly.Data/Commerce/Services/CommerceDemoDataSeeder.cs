using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Seeds a neutral, industry-agnostic demo/QA dataset for the Universal_Domain_Model (Requirement 10).
/// The dataset uses only neutral Simplified Chinese, user-visible values (<c>客户 A</c>/<c>客户 B</c>,
/// <c>商品 A</c>/<c>商品 B</c>, <c>库存项 A</c>/<c>库存项 B</c>, <c>供应商 A</c>, <c>订单 001</c>,
/// <c>收入分类 A</c>, <c>支出分类 A</c>) and contains no English-style demo value, no industry-specific
/// sample value, and no Forbidden_Term (Req 10.1, 10.2).
///
/// <para>The dataset covers at least one record in every required category — orders, order items,
/// customers, inventory items, inventory movements, income, expense, receivable, and payable
/// (Req 10.3) — includes at least one low-stock inventory item whose available quantity is at or below
/// its reorder threshold (Req 10.4), and includes at least one generated insight (Req 10.5).</para>
///
/// <para><b>Placement.</b> The seeder lives in the data layer (<c>Orderly.Data</c>) alongside the
/// Commerce repositories it writes through, and persists every record using those repositories so the
/// data passes the same save-boundary validation as ordinary writes.</para>
///
/// <para><b>Idempotence.</b> Every seeded record uses a deterministic identity derived from a stable
/// seed string, and the seeder skips any record that already exists. Running it any number of times
/// against the same database therefore produces the same dataset with no duplicates. All writes run
/// inside a single <see cref="CoreWriteTransaction"/> so a partial failure leaves the database
/// unchanged.</para>
/// </summary>
public sealed class CommerceDemoDataSeeder
{
    /// <summary>Neutral display name of the demo workspace.</summary>
    public const string WorkspaceName = "演示工作区";

    /// <summary>Neutral display name of the first demo customer (Req 10.1).</summary>
    public const string CustomerAName = "客户 A";

    /// <summary>Neutral display name of the second demo customer (Req 10.1).</summary>
    public const string CustomerBName = "客户 B";

    /// <summary>Neutral display name of the first demo product (Req 10.1).</summary>
    public const string ProductAName = "商品 A";

    /// <summary>Neutral display name of the second demo product (Req 10.1).</summary>
    public const string ProductBName = "商品 B";

    /// <summary>Neutral display name of the first demo inventory item (Req 10.1).</summary>
    public const string InventoryItemAName = "库存项 A";

    /// <summary>Neutral display name of the second demo (low-stock) inventory item (Req 10.1, 10.4).</summary>
    public const string InventoryItemBName = "库存项 B";

    /// <summary>Neutral display name of the demo supplier (Req 10.1).</summary>
    public const string SupplierAName = "供应商 A";

    /// <summary>Neutral order number of the demo order (Req 10.1).</summary>
    public const string OrderNo = "订单 001";

    /// <summary>Neutral income category label of the demo cash-flow income/receivable entries (Req 10.1).</summary>
    public const string IncomeCategoryName = "收入分类 A";

    /// <summary>Neutral expense category label of the demo cash-flow expense/payable entries (Req 10.1).</summary>
    public const string ExpenseCategoryName = "支出分类 A";

    private readonly SqliteConnectionFactory _connectionFactory;

    public CommerceDemoDataSeeder(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Ensures the Commerce schema exists, then seeds the neutral demo dataset idempotently. Returns a
    /// <see cref="CommerceDemoSeedResult"/> describing the workspace and how many records were newly
    /// created in each category (zero for categories already present from a previous run).
    /// </summary>
    public async Task<CommerceDemoSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        await new CommerceSchemaInitializer(_connectionFactory).InitializeAsync(cancellationToken);

        var workspaces = new BusinessWorkspaceRepository(_connectionFactory);
        var suppliers = new SupplierRepository(_connectionFactory);
        var products = new ProductRepository(_connectionFactory);
        var inventoryItems = new InventoryItemRepository(_connectionFactory);
        var inventoryMovements = new InventoryMovementRepository(_connectionFactory);
        var customers = new CommerceCustomerRepository(_connectionFactory);
        var orders = new CommerceOrderRepository(_connectionFactory);
        var orderItems = new OrderItemRepository(_connectionFactory);
        var cashFlowEntries = new CashFlowEntryRepository(_connectionFactory);
        var insights = new BusinessInsightRepository(_connectionFactory);

        Guid workspaceId = DeterministicId("workspace");
        var result = new CommerceDemoSeedResult { WorkspaceId = workspaceId };

        DateTime now = DateTime.UtcNow;

        using var transaction = CoreWriteTransaction.Begin(_connectionFactory);

        // --- Workspace (scoping root) ---
        await EnsureAsync(workspaces, new BusinessWorkspace
        {
            Id = workspaceId,
            CreatedAt = now,
            Name = WorkspaceName,
            DefaultCurrencyCode = "CNY",
        }, cancellationToken);

        // --- Supplier 供应商 A ---
        Guid supplierAId = DeterministicId("supplier-a");
        if (await EnsureAsync(suppliers, new Supplier
        {
            Id = supplierAId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = SupplierAName,
            ContactName = "联系人 A",
            Phone = "13900000001",
            Note = "演示供应商。",
        }, cancellationToken))
        {
            result.SuppliersCreated++;
        }

        // --- Products 商品 A / 商品 B ---
        Guid productAId = DeterministicId("product-a");
        if (await EnsureAsync(products, new Product
        {
            Id = productAId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = ProductAName,
            Code = "SP-A",
            ProductType = ProductType.Physical,
            Description = "演示商品 A。",
            SupplierId = supplierAId,
            DefaultPrice = CommerceMoney.From(100.00m),
            DefaultCost = CommerceMoney.From(60.00m),
        }, cancellationToken))
        {
            result.ProductsCreated++;
        }

        Guid productBId = DeterministicId("product-b");
        if (await EnsureAsync(products, new Product
        {
            Id = productBId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = ProductBName,
            Code = "SP-B",
            ProductType = ProductType.Physical,
            Description = "演示商品 B。",
            SupplierId = supplierAId,
            DefaultPrice = CommerceMoney.From(50.00m),
            DefaultCost = CommerceMoney.From(30.00m),
        }, cancellationToken))
        {
            result.ProductsCreated++;
        }

        // --- Inventory items 库存项 A / 库存项 B (B is low-stock, Req 10.4) ---
        Guid inventoryItemAId = DeterministicId("inventory-item-a");
        if (await EnsureAsync(inventoryItems, new InventoryItem
        {
            Id = inventoryItemAId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = InventoryItemAName,
            Sku = "KC-A",
            ProductId = productAId,
            QuantityAvailable = 100m,
            ReorderThreshold = 10m,
            UnitCost = CommerceMoney.From(60.00m),
        }, cancellationToken))
        {
            result.InventoryItemsCreated++;
        }

        Guid inventoryItemBId = DeterministicId("inventory-item-b");
        if (await EnsureAsync(inventoryItems, new InventoryItem
        {
            Id = inventoryItemBId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = InventoryItemBName,
            Sku = "KC-B",
            ProductId = productBId,
            QuantityAvailable = 3m,      // at/below threshold → low stock (Req 10.4)
            ReorderThreshold = 5m,
            UnitCost = CommerceMoney.From(30.00m),
        }, cancellationToken))
        {
            result.InventoryItemsCreated++;
        }

        // --- Inventory movement: inbound stock for 库存项 A (Req 10.3) ---
        if (await EnsureAsync(inventoryMovements, new InventoryMovement
        {
            Id = DeterministicId("inventory-movement-inbound-a"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            InventoryItemId = inventoryItemAId,
            MovementType = InventoryMovementType.Inbound,
            Quantity = 100m,
            SupplierId = supplierAId,
            OccurredAt = now.AddDays(-7),
            BusinessKey = "demo:inventory-movement:inbound-a",
            Note = "演示入库。",
        }, cancellationToken))
        {
            result.InventoryMovementsCreated++;
        }

        // --- Customers 客户 A / 客户 B (Req 10.3) ---
        Guid customerAId = DeterministicId("customer-a");
        if (await EnsureAsync(customers, new Customer
        {
            Id = customerAId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = CustomerAName,
            Phone = "13800000001",
            LastOrderAt = now.AddDays(-1),
            CompletedOrderCount = 1,
            TotalSpend = CommerceMoney.From(200.00m),
        }, cancellationToken))
        {
            result.CustomersCreated++;
        }

        Guid customerBId = DeterministicId("customer-b");
        if (await EnsureAsync(customers, new Customer
        {
            Id = customerBId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Name = CustomerBName,
            Phone = "13800000002",
        }, cancellationToken))
        {
            result.CustomersCreated++;
        }

        // --- Order 订单 001 for 客户 A (Req 10.3) ---
        // One line of 商品 A drawn from 库存项 A: qty 2 × 100.00 = 200.00; cost 2 × 60.00 = 120.00.
        Guid orderId = DeterministicId("order-001");
        if (await EnsureAsync(orders, new Order
        {
            Id = orderId,
            CreatedAt = now,
            WorkspaceId = workspaceId,
            OrderNo = OrderNo,
            CustomerId = customerAId,
            SalesStage = OrderSalesStage.Completed,
            PaymentStage = OrderPaymentStage.Paid,
            FulfillmentStage = OrderFulfillmentStage.Fulfilled,
            Subtotal = CommerceMoney.From(200.00m),
            Total = CommerceMoney.From(200.00m),
            Cost = CommerceMoney.From(120.00m),
            GrossProfit = CommerceMoney.From(80.00m),
            GrossMargin = 40.00m,
            PaidAmount = CommerceMoney.From(200.00m),
            ReceivableAmount = CommerceMoney.Zero,
            OrderedAt = now.AddDays(-1),
            Note = "演示订单。",
        }, cancellationToken))
        {
            result.OrdersCreated++;
        }

        // --- Order item linked to 库存项 A (Req 10.3) ---
        if (await EnsureAsync(orderItems, new OrderItem
        {
            Id = DeterministicId("order-item-001-1"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            OrderId = orderId,
            ProductId = productAId,
            InventoryItemId = inventoryItemAId,
            Description = "演示订单明细。",
            Quantity = 2m,
            UnitPrice = CommerceMoney.From(100.00m),
            UnitCost = CommerceMoney.From(60.00m),
            LineTotal = CommerceMoney.From(200.00m),
        }, cancellationToken))
        {
            result.OrderItemsCreated++;
        }

        // --- Cash flow: income (settled), expense (settled), receivable (pending), payable (pending) ---
        // Income entry — settled revenue for 订单 001 (Req 10.3).
        if (await EnsureAsync(cashFlowEntries, new CashFlowEntry
        {
            Id = DeterministicId("cashflow-income"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Income,
            Amount = CommerceMoney.From(200.00m),
            SettledAmount = CommerceMoney.From(200.00m),
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = now.AddDays(-1),
            CategoryName = IncomeCategoryName,
            OrderId = orderId,
            BusinessKey = "demo:cashflow:income",
        }, cancellationToken))
        {
            result.IncomeEntriesCreated++;
        }

        // Expense entry — settled stock purchase (Req 10.3).
        if (await EnsureAsync(cashFlowEntries, new CashFlowEntry
        {
            Id = DeterministicId("cashflow-expense"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Expense,
            Amount = CommerceMoney.From(6000.00m),
            SettledAmount = CommerceMoney.From(6000.00m),
            SettlementStatus = CashFlowSettlementStatus.Settled,
            OccurredAt = now.AddDays(-7),
            CategoryName = ExpenseCategoryName,
            BusinessKey = "demo:cashflow:expense",
        }, cancellationToken))
        {
            result.ExpenseEntriesCreated++;
        }

        // Receivable entry — unsettled income with a past due date (Req 10.3).
        if (await EnsureAsync(cashFlowEntries, new CashFlowEntry
        {
            Id = DeterministicId("cashflow-receivable"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Income,
            Amount = CommerceMoney.From(500.00m),
            SettledAmount = CommerceMoney.Zero,
            SettlementStatus = CashFlowSettlementStatus.Pending,
            OccurredAt = now.AddDays(-10),
            DueDate = now.AddDays(-2),
            CategoryName = IncomeCategoryName,
            BusinessKey = "demo:cashflow:receivable",
        }, cancellationToken))
        {
            result.ReceivableEntriesCreated++;
        }

        // Payable entry — unsettled expense with a future due date (Req 10.3).
        if (await EnsureAsync(cashFlowEntries, new CashFlowEntry
        {
            Id = DeterministicId("cashflow-payable"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Direction = CashFlowDirection.Expense,
            Amount = CommerceMoney.From(300.00m),
            SettledAmount = CommerceMoney.Zero,
            SettlementStatus = CashFlowSettlementStatus.Pending,
            OccurredAt = now.AddDays(-3),
            DueDate = now.AddDays(7),
            CategoryName = ExpenseCategoryName,
            BusinessKey = "demo:cashflow:payable",
        }, cancellationToken))
        {
            result.PayableEntriesCreated++;
        }

        // --- Generated insight: low-stock warning for 库存项 B (Req 10.5) ---
        if (await EnsureAsync(insights, new BusinessInsight
        {
            Id = DeterministicId("insight-low-stock-b"),
            CreatedAt = now,
            WorkspaceId = workspaceId,
            Severity = InsightSeverity.Warning,
            Title = "库存偏低",
            Message = $"{InventoryItemBName} 当前可用数量已达到或低于补货阈值，建议尽快补货。",
            Category = "库存",
            GeneratedAt = now,
            BusinessKey = $"inventory:low-stock:{inventoryItemBId}",
        }, cancellationToken))
        {
            result.InsightsCreated++;
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Creates an entity only when no record with the same deterministic identity already exists
    /// (including soft-deleted rows), so repeated runs never insert duplicates. Returns <c>true</c>
    /// when a new record was created and <c>false</c> when an existing record was left untouched.
    /// </summary>
    private static async Task<bool> EnsureAsync<TEntity>(
        ICommerceRepository<TEntity> repository,
        TEntity entity,
        CancellationToken cancellationToken)
        where TEntity : CommerceEntity
    {
        TEntity? existing = await repository.GetByIdIncludingDeletedAsync(entity.Id, cancellationToken);
        if (existing is not null)
        {
            return false;
        }

        await repository.CreateAsync(entity, cancellationToken);
        return true;
    }

    /// <summary>
    /// Derives a stable <see cref="Guid"/> from a seed string so every seeded record has a fixed
    /// identity across runs, which is what makes the seeder idempotent. The hash is used only to
    /// generate local demo identifiers, not for any security purpose.
    /// </summary>
    private static Guid DeterministicId(string seed)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("orderly-demo:" + seed));
        return new Guid(hash);
    }
}

/// <summary>
/// Describes the outcome of a <see cref="CommerceDemoDataSeeder.SeedAsync"/> run: the demo workspace
/// identity and how many records were newly created in each required category (Req 10.3). Counts are
/// zero for categories whose records already existed from a previous idempotent run.
/// </summary>
public sealed class CommerceDemoSeedResult
{
    /// <summary>Identity of the demo <c>BusinessWorkspace</c> that owns every seeded record.</summary>
    public Guid WorkspaceId { get; init; }

    /// <summary>Number of demo customers newly created.</summary>
    public int CustomersCreated { get; internal set; }

    /// <summary>Number of demo products newly created.</summary>
    public int ProductsCreated { get; internal set; }

    /// <summary>Number of demo suppliers newly created.</summary>
    public int SuppliersCreated { get; internal set; }

    /// <summary>Number of demo inventory items newly created.</summary>
    public int InventoryItemsCreated { get; internal set; }

    /// <summary>Number of demo inventory movements newly created.</summary>
    public int InventoryMovementsCreated { get; internal set; }

    /// <summary>Number of demo orders newly created.</summary>
    public int OrdersCreated { get; internal set; }

    /// <summary>Number of demo order items newly created.</summary>
    public int OrderItemsCreated { get; internal set; }

    /// <summary>Number of demo income cash-flow entries newly created.</summary>
    public int IncomeEntriesCreated { get; internal set; }

    /// <summary>Number of demo expense cash-flow entries newly created.</summary>
    public int ExpenseEntriesCreated { get; internal set; }

    /// <summary>Number of demo receivable cash-flow entries newly created.</summary>
    public int ReceivableEntriesCreated { get; internal set; }

    /// <summary>Number of demo payable cash-flow entries newly created.</summary>
    public int PayableEntriesCreated { get; internal set; }

    /// <summary>Number of demo generated insights newly created.</summary>
    public int InsightsCreated { get; internal set; }
}
