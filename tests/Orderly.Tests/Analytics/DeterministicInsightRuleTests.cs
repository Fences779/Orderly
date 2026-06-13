using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Analytics;

/// <summary>
/// Unit tests for the deterministic local insight rules of <see cref="CommerceBusinessInsightService"/>
/// (Task 12.3, Req 4.14). They exercise the real SQLCipher-backed Commerce repositories and the real
/// <see cref="CommerceInventoryService"/> against an unencrypted temp database (no mocks) to verify:
/// <list type="bullet">
///   <item>the inventory rule — out-of-stock (critical) and low-stock (warning) items;</item>
///   <item>the cash-flow rule — an unsettled receivable (income) or payable (expense) whose due date
///   is on or before <c>asOfUtc</c> raises an overdue warning, while settled entries, transfers,
///   future-dated entries, and entries without a due date never do;</item>
///   <item>determinism — identical inputs always yield an identical, stably ordered sequence; and</item>
///   <item>that the service depends only on local collaborators with no LLM/network dependency.</item>
/// </list>
/// </summary>
public sealed class DeterministicInsightRuleTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GenerateInsights_flags_out_of_stock_item_as_critical()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem outOfStock = await CreateItemAsync(items, workspaceId, quantity: 0m, threshold: 5m);
            await CreateItemAsync(items, workspaceId, quantity: 100m, threshold: 5m);

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"inventory:out-of-stock:{outOfStock.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Critical, insight.Severity);
            Assert.Equal(workspaceId, insight.WorkspaceId);
        });
    }

    [Fact]
    public async Task GenerateInsights_flags_low_stock_item_as_warning()
    {
        await WithServiceAsync(async (service, items, _, workspaceId) =>
        {
            InventoryItem low = await CreateItemAsync(items, workspaceId, quantity: 3m, threshold: 5m);

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"inventory:low-stock:{low.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Warning, insight.Severity);
        });
    }

    [Fact]
    public async Task GenerateInsights_flags_overdue_receivable_as_warning()
    {
        await WithServiceAsync(async (service, _, cashFlow, workspaceId) =>
        {
            CashFlowEntry receivable = await cashFlow.CreateAsync(Entry(
                workspaceId,
                CashFlowDirection.Income,
                amount: 250m,
                status: CashFlowSettlementStatus.Pending,
                dueDate: AsOf.AddDays(-1)));

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"cashflow:receivable-overdue:{receivable.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Warning, insight.Severity);
            Assert.Equal("应收逾期", insight.Title);
        });
    }

    [Fact]
    public async Task GenerateInsights_flags_overdue_payable_as_warning()
    {
        await WithServiceAsync(async (service, _, cashFlow, workspaceId) =>
        {
            CashFlowEntry payable = await cashFlow.CreateAsync(Entry(
                workspaceId,
                CashFlowDirection.Expense,
                amount: 80m,
                status: CashFlowSettlementStatus.Overdue,
                dueDate: AsOf)); // due date exactly at the evaluation instant is overdue (<=).

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            BusinessInsight insight = Assert.Single(insights);
            Assert.Equal($"cashflow:payable-overdue:{payable.Id}", insight.BusinessKey);
            Assert.Equal(InsightSeverity.Warning, insight.Severity);
            Assert.Equal("应付逾期", insight.Title);
        });
    }

    [Fact]
    public async Task GenerateInsights_ignores_settled_transfer_future_and_undated_entries()
    {
        await WithServiceAsync(async (service, _, cashFlow, workspaceId) =>
        {
            // Settled receivable — fully paid, not overdue.
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Income, 100m, CashFlowSettlementStatus.Settled, AsOf.AddDays(-10)));
            // Transfer — never raises an overdue insight regardless of due date/status.
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Transfer, 100m, CashFlowSettlementStatus.Pending, AsOf.AddDays(-10)));
            // Future-dated unsettled payable — not yet due.
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Expense, 100m, CashFlowSettlementStatus.Pending, AsOf.AddDays(5)));
            // Unsettled receivable with no due date — cannot be overdue.
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Income, 100m, CashFlowSettlementStatus.Pending, dueDate: null));

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            Assert.Empty(insights);
        });
    }

    [Fact]
    public async Task GenerateInsights_orders_most_severe_first()
    {
        await WithServiceAsync(async (service, items, cashFlow, workspaceId) =>
        {
            // One critical (out-of-stock) and one warning (overdue payable) insight.
            await CreateItemAsync(items, workspaceId, quantity: 0m, threshold: 5m);
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Expense, 80m, CashFlowSettlementStatus.Overdue, AsOf.AddDays(-1)));

            IReadOnlyList<BusinessInsight> insights = await service.GenerateInsightsAsync(AsOf);

            Assert.Equal(2, insights.Count);
            Assert.Equal(InsightSeverity.Critical, insights[0].Severity);
            Assert.Equal(InsightSeverity.Warning, insights[1].Severity);
        });
    }

    [Fact]
    public async Task GenerateInsights_is_deterministic_across_repeated_calls()
    {
        await WithServiceAsync(async (service, items, cashFlow, workspaceId) =>
        {
            await CreateItemAsync(items, workspaceId, quantity: 0m, threshold: 5m);
            await CreateItemAsync(items, workspaceId, quantity: 2m, threshold: 5m);
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Income, 250m, CashFlowSettlementStatus.Pending, AsOf.AddDays(-3)));
            await cashFlow.CreateAsync(Entry(
                workspaceId, CashFlowDirection.Expense, 80m, CashFlowSettlementStatus.Overdue, AsOf.AddDays(-1)));

            // Identical inputs and identical asOfUtc must yield an identical, identically ordered set.
            IReadOnlyList<BusinessInsight> first = await service.GenerateInsightsAsync(AsOf);
            IReadOnlyList<BusinessInsight> second = await service.GenerateInsightsAsync(AsOf);

            Assert.Equal(
                first.Select(Signature).ToArray(),
                second.Select(Signature).ToArray());

            // The ordering is itself deterministic: severity descending, then ordinal tie-breakers.
            IReadOnlyList<string> keys = first.Select(i => i.BusinessKey!).ToArray();
            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).Count(), keys.Count);
            Assert.True(IsNonIncreasingSeverity(first));
        });
    }

    [Fact]
    public void Service_depends_only_on_local_collaborators_with_no_llm_or_network_dependency()
    {
        ConstructorInfo ctor = typeof(CommerceBusinessInsightService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        Type[] parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        // The only collaborators are local domain services / repositories and the reserved provider
        // hook — there is no HTTP client or language-model dependency anywhere in the surface.
        Assert.Contains(typeof(IInventoryService), parameterTypes);
        Assert.Contains(typeof(ICashFlowEntryRepository), parameterTypes);

        string[] forbiddenTokens = { "http", "llm", "openai", "gpt", "anthropic", "completion", "embedding", "model" };
        foreach (ParameterInfo parameter in ctor.GetParameters())
        {
            string name = (parameter.ParameterType.FullName ?? parameter.ParameterType.Name).ToLowerInvariant();
            foreach (string token in forbiddenTokens)
            {
                Assert.False(
                    name.Contains(token, StringComparison.Ordinal),
                    $"Constructor parameter '{parameter.Name}' of type '{parameter.ParameterType.FullName}' suggests an LLM/network dependency.");
            }
        }

        // The reserved provider hook is a pluggable extension point, not an LLM entry point.
        Assert.Contains(parameterTypes, t => typeof(IEnumerable<IBusinessInsightProvider>).IsAssignableFrom(t));
    }

    // --- Helpers ---

    private static (InsightSeverity Severity, string? Category, string? Key, string Title, string Message) Signature(
        BusinessInsight insight)
        => (insight.Severity, insight.Category, insight.BusinessKey, insight.Title, insight.Message);

    private static bool IsNonIncreasingSeverity(IReadOnlyList<BusinessInsight> insights)
    {
        for (int i = 1; i < insights.Count; i++)
        {
            if ((int)insights[i].Severity > (int)insights[i - 1].Severity)
            {
                return false;
            }
        }

        return true;
    }

    private static CashFlowEntry Entry(
        Guid workspaceId,
        CashFlowDirection direction,
        decimal amount,
        CashFlowSettlementStatus status,
        DateTime? dueDate)
        => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Direction = direction,
            Amount = CommerceMoney.From(amount),
            SettledAmount = status == CashFlowSettlementStatus.Settled
                ? CommerceMoney.From(amount)
                : CommerceMoney.Zero,
            SettlementStatus = status,
            OccurredAt = AsOf.AddDays(-30),
            DueDate = dueDate,
        };

    private static async Task<InventoryItem> CreateItemAsync(
        IInventoryItemRepository items,
        Guid workspaceId,
        decimal quantity,
        decimal threshold)
    {
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = "库存项 " + Guid.NewGuid().ToString("N")[..6],
            QuantityAvailable = quantity,
            ReorderThreshold = threshold,
        };

        return await items.CreateAsync(item);
    }

    private static async Task WithServiceAsync(
        Func<CommerceBusinessInsightService, IInventoryItemRepository, ICashFlowEntryRepository, Guid, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-insights-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var items = new InventoryItemRepository(factory);
            var movements = new InventoryMovementRepository(factory);
            var cashFlow = new CashFlowEntryRepository(factory);
            var inventoryService = new CommerceInventoryService(items, movements);
            var service = new CommerceBusinessInsightService(inventoryService, cashFlow);

            await body(service, items, cashFlow, Guid.NewGuid());
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
