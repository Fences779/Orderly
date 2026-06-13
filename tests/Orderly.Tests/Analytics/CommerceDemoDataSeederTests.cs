using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Analytics;

/// <summary>
/// Unit tests for the composition of the neutral demo/QA dataset produced by
/// <see cref="CommerceDemoDataSeeder"/> (Task 18.2, Req 10.3–10.5, and the neutrality guards of
/// Req 10.1–10.2). They run the real seeder against an unencrypted temp database (no mocks) and
/// then read the persisted records back through the real Commerce repositories to verify:
/// <list type="bullet">
///   <item>category coverage — at least one record in every required category: orders, order items,
///   customers, inventory items, inventory movements, income, expense, receivable, and payable
///   (Req 10.3);</item>
///   <item>at least one low-stock inventory item whose available quantity is at or below its reorder
///   threshold (Req 10.4);</item>
///   <item>at least one generated insight (Req 10.5); and</item>
///   <item>that every user-visible value uses the neutral Simplified Chinese demo values and contains
///   no Forbidden_Term and no English-style demo value such as "Customer A" (Req 10.1–10.2).</item>
/// </list>
/// </summary>
public sealed class CommerceDemoDataSeederTests
{
    [Fact]
    public async Task Seed_covers_at_least_one_record_in_every_required_category()
    {
        await WithSeededDatabaseAsync(async (result, repos) =>
        {
            // The reported per-category creation counts cover every required category (Req 10.3).
            Assert.True(result.OrdersCreated >= 1, "expected at least one order");
            Assert.True(result.OrderItemsCreated >= 1, "expected at least one order item");
            Assert.True(result.CustomersCreated >= 1, "expected at least one customer");
            Assert.True(result.InventoryItemsCreated >= 1, "expected at least one inventory item");
            Assert.True(result.InventoryMovementsCreated >= 1, "expected at least one inventory movement");
            Assert.True(result.IncomeEntriesCreated >= 1, "expected at least one income entry");
            Assert.True(result.ExpenseEntriesCreated >= 1, "expected at least one expense entry");
            Assert.True(result.ReceivableEntriesCreated >= 1, "expected at least one receivable entry");
            Assert.True(result.PayableEntriesCreated >= 1, "expected at least one payable entry");

            // The persisted records independently confirm coverage of every required category.
            Assert.NotEmpty(await repos.Orders.GetAllAsync());
            Assert.NotEmpty(await repos.OrderItems.GetAllAsync());
            Assert.NotEmpty(await repos.Customers.GetAllAsync());
            Assert.NotEmpty(await repos.InventoryItems.GetAllAsync());
            Assert.NotEmpty(await repos.InventoryMovements.GetAllAsync());

            IReadOnlyList<CashFlowEntry> cashFlow = await repos.CashFlow.GetAllAsync();

            // Income vs. expense by direction (Req 10.3).
            Assert.Contains(cashFlow, e => e.Direction == CashFlowDirection.Income);
            Assert.Contains(cashFlow, e => e.Direction == CashFlowDirection.Expense);

            // Receivable = unsettled income; payable = unsettled expense (Req 10.3).
            Assert.Contains(
                cashFlow,
                e => e.Direction == CashFlowDirection.Income
                     && e.SettlementStatus != CashFlowSettlementStatus.Settled);
            Assert.Contains(
                cashFlow,
                e => e.Direction == CashFlowDirection.Expense
                     && e.SettlementStatus != CashFlowSettlementStatus.Settled);
        });
    }

    [Fact]
    public async Task Seed_includes_at_least_one_low_stock_inventory_item()
    {
        await WithSeededDatabaseAsync(async (_, repos) =>
        {
            IReadOnlyList<InventoryItem> items = await repos.InventoryItems.GetAllAsync();

            // At least one item is at or below its reorder threshold (Req 10.4).
            Assert.Contains(items, i => i.QuantityAvailable <= i.ReorderThreshold);

            // The specific low-stock demo item is 库存项 B.
            InventoryItem lowStock = Assert.Single(
                items,
                i => i.Name == CommerceDemoDataSeeder.InventoryItemBName);
            Assert.True(lowStock.QuantityAvailable <= lowStock.ReorderThreshold);
        });
    }

    [Fact]
    public async Task Seed_includes_at_least_one_generated_insight()
    {
        await WithSeededDatabaseAsync(async (result, repos) =>
        {
            Assert.True(result.InsightsCreated >= 1, "expected at least one generated insight");

            IReadOnlyList<BusinessInsight> insights = await repos.Insights.GetAllAsync();
            Assert.NotEmpty(insights); // Req 10.5
        });
    }

    [Fact]
    public async Task Seed_uses_the_neutral_simplified_chinese_demo_values()
    {
        await WithSeededDatabaseAsync(async (_, repos) =>
        {
            // The exact neutral, Simplified Chinese, user-visible values are present (Req 10.1).
            IReadOnlyList<string> customerNames = (await repos.Customers.GetAllAsync()).Select(c => c.Name).ToList();
            Assert.Contains(CommerceDemoDataSeeder.CustomerAName, customerNames);
            Assert.Contains(CommerceDemoDataSeeder.CustomerBName, customerNames);

            IReadOnlyList<string> productNames = (await repos.Products.GetAllAsync()).Select(p => p.Name).ToList();
            Assert.Contains(CommerceDemoDataSeeder.ProductAName, productNames);
            Assert.Contains(CommerceDemoDataSeeder.ProductBName, productNames);

            IReadOnlyList<string> itemNames = (await repos.InventoryItems.GetAllAsync()).Select(i => i.Name).ToList();
            Assert.Contains(CommerceDemoDataSeeder.InventoryItemAName, itemNames);
            Assert.Contains(CommerceDemoDataSeeder.InventoryItemBName, itemNames);

            Assert.Contains(CommerceDemoDataSeeder.SupplierAName, (await repos.Suppliers.GetAllAsync()).Select(s => s.Name));
            Assert.Contains(CommerceDemoDataSeeder.OrderNo, (await repos.Orders.GetAllAsync()).Select(o => o.OrderNo));

            IReadOnlyList<string?> categories = (await repos.CashFlow.GetAllAsync()).Select(e => e.CategoryName).ToList();
            Assert.Contains(CommerceDemoDataSeeder.IncomeCategoryName, categories);
            Assert.Contains(CommerceDemoDataSeeder.ExpenseCategoryName, categories);
        });
    }

    [Fact]
    public async Task Seed_contains_no_forbidden_term_in_any_user_visible_value()
    {
        await WithSeededDatabaseAsync(async (_, repos) =>
        {
            IReadOnlyList<string> values = await CollectAllStringValuesAsync(repos);
            IReadOnlyList<string> forbiddenTerms = BuildForbiddenTerms();

            foreach (string value in values)
            {
                foreach (string term in forbiddenTerms)
                {
                    Assert.False(
                        value.Contains(term, StringComparison.OrdinalIgnoreCase),
                        $"Seeded value '{value}' contains forbidden term '{term}'.");
                }
            }
        });
    }

    [Fact]
    public async Task Seed_contains_no_english_style_demo_value()
    {
        await WithSeededDatabaseAsync(async (_, repos) =>
        {
            // English-style demo values such as "Customer A" / "Product A" are an ASCII word of three
            // or more letters followed by a single uppercase letter or digit (Req 10.1). Neutral demo
            // names use Simplified Chinese prefixes (客户/商品/库存项/供应商/订单), so they never match.
            var englishStyle = new Regex(@"\b[A-Za-z]{3,}\s+[A-Z0-9]\b", RegexOptions.CultureInvariant);

            foreach (string value in await CollectDisplayNamesAsync(repos))
            {
                Assert.False(
                    englishStyle.IsMatch(value),
                    $"Seeded display value '{value}' looks like an English-style demo value.");
            }
        });
    }

    // --- Helpers ---

    /// <summary>
    /// User-visible display names that Req 10.1 governs. SKUs, product codes, and Business_Keys are
    /// internal identifiers (e.g. "SP-A", "demo:cashflow:income"), not user-visible demo values, so
    /// they are intentionally excluded from the English-style check.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CollectDisplayNamesAsync(SeededRepositories repos)
    {
        var values = new List<string?>();
        values.AddRange((await repos.Customers.GetAllAsync()).Select(c => c.Name));
        values.AddRange((await repos.Products.GetAllAsync()).Select(p => p.Name));
        values.AddRange((await repos.InventoryItems.GetAllAsync()).Select(i => i.Name));
        values.AddRange((await repos.Suppliers.GetAllAsync()).Select(s => s.Name));
        values.AddRange((await repos.Orders.GetAllAsync()).Select(o => o.OrderNo));
        values.AddRange((await repos.CashFlow.GetAllAsync()).Select(e => e.CategoryName));
        values.AddRange((await repos.Insights.GetAllAsync()).SelectMany(i => new[] { i.Title, i.Message }));
        return values.Where(v => !string.IsNullOrEmpty(v)).Cast<string>().ToList();
    }

    /// <summary>
    /// Every non-empty string value persisted across the seeded records, used for the Forbidden_Term
    /// scan (Req 10.2). This includes display names, notes, contact names, categories, and insight text.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CollectAllStringValuesAsync(SeededRepositories repos)
    {
        var values = new List<string?>();

        foreach (Customer c in await repos.Customers.GetAllAsync())
        {
            values.Add(c.Name);
            values.Add(c.Phone);
        }

        foreach (Product p in await repos.Products.GetAllAsync())
        {
            values.Add(p.Name);
            values.Add(p.Code);
            values.Add(p.Description);
        }

        foreach (InventoryItem i in await repos.InventoryItems.GetAllAsync())
        {
            values.Add(i.Name);
            values.Add(i.Sku);
        }

        foreach (Supplier s in await repos.Suppliers.GetAllAsync())
        {
            values.Add(s.Name);
            values.Add(s.ContactName);
            values.Add(s.Note);
        }

        foreach (Order o in await repos.Orders.GetAllAsync())
        {
            values.Add(o.OrderNo);
            values.Add(o.Note);
        }

        foreach (OrderItem item in await repos.OrderItems.GetAllAsync())
        {
            values.Add(item.Description);
        }

        foreach (InventoryMovement m in await repos.InventoryMovements.GetAllAsync())
        {
            values.Add(m.Note);
        }

        foreach (CashFlowEntry e in await repos.CashFlow.GetAllAsync())
        {
            values.Add(e.CategoryName);
        }

        foreach (BusinessInsight insight in await repos.Insights.GetAllAsync())
        {
            values.Add(insight.Title);
            values.Add(insight.Message);
            values.Add(insight.Category);
        }

        return values.Where(v => !string.IsNullOrEmpty(v)).Cast<string>().ToList();
    }

    /// <summary>
    /// Builds the Forbidden_Terms list from constraint C-4 (requirements.md). Each term is assembled
    /// from fragments so this source file never holds a complete Forbidden_Term contiguously (Req 11.8),
    /// which keeps the file itself clean for the Forbidden_Terms_Test scan.
    /// </summary>
    private static IReadOnlyList<string> BuildForbiddenTerms()
    {
        return new[]
        {
            "String" + "Narration",
            "串" + "述",
            "admin" + "Pc" + "Gateway",
            "brac" + "elet",
            "be" + "ad",
            "be" + "ad" + "s",
            "wr" + "ist",
            "wr" + "ist" + "Size",
            "dia" + "meter",
            "珠" + "串",
            "珠" + "子",
            "手" + "围",
            "直" + "径",
            "材" + "质",
            "订单" + "设计",
            "成" + "品",
            "平均" + "每" + "串",
            "Orderly" + "-" + "SN",
            "start" + "-" + "sn",
        };
    }

    private static async Task WithSeededDatabaseAsync(Func<CommerceDemoSeedResult, SeededRepositories, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-demo-seed-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            CommerceDemoSeedResult result = await new CommerceDemoDataSeeder(factory).SeedAsync();

            var repos = new SeededRepositories(factory);
            await body(result, repos);
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

    /// <summary>Bundles the Commerce repositories used to read back the seeded dataset.</summary>
    private sealed class SeededRepositories
    {
        public SeededRepositories(SqliteConnectionFactory factory)
        {
            Suppliers = new SupplierRepository(factory);
            Products = new ProductRepository(factory);
            InventoryItems = new InventoryItemRepository(factory);
            InventoryMovements = new InventoryMovementRepository(factory);
            Customers = new CommerceCustomerRepository(factory);
            Orders = new CommerceOrderRepository(factory);
            OrderItems = new OrderItemRepository(factory);
            CashFlow = new CashFlowEntryRepository(factory);
            Insights = new BusinessInsightRepository(factory);
        }

        public SupplierRepository Suppliers { get; }

        public ProductRepository Products { get; }

        public InventoryItemRepository InventoryItems { get; }

        public InventoryMovementRepository InventoryMovements { get; }

        public CommerceCustomerRepository Customers { get; }

        public CommerceOrderRepository Orders { get; }

        public OrderItemRepository OrderItems { get; }

        public CashFlowEntryRepository CashFlow { get; }

        public BusinessInsightRepository Insights { get; }
    }
}
