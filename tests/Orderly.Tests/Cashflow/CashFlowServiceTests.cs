using System;
using System.IO;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;

namespace Orderly.Tests.Cashflow;

/// <summary>
/// Example/unit tests for <see cref="CommerceCashFlowService"/> (Task 10.3, Req 4.12). They exercise
/// the real SQLCipher-backed Commerce repository against an unencrypted temp database (no mocks) to
/// verify recording of income/expense/receivable/payable entries, settlement transitions through
/// <see cref="CashFlowSettlementStatus"/>, period summaries, and the bounded integer health score.
/// The universal health-bounds property is covered separately by the Property 12 test (Task 10.4).
/// </summary>
public sealed class CashFlowServiceTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecordIncome_is_settled_in_full()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry entry = await service.RecordIncomeAsync(Input(workspaceId, 100m));

            Assert.Equal(CashFlowDirection.Income, entry.Direction);
            Assert.Equal(CashFlowSettlementStatus.Settled, entry.SettlementStatus);
            Assert.Equal(100m, entry.SettledAmount.Amount);
        });
    }

    [Fact]
    public async Task RecordExpense_is_settled_in_full()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry entry = await service.RecordExpenseAsync(Input(workspaceId, 40m));

            Assert.Equal(CashFlowDirection.Expense, entry.Direction);
            Assert.Equal(CashFlowSettlementStatus.Settled, entry.SettlementStatus);
            Assert.Equal(40m, entry.SettledAmount.Amount);
        });
    }

    [Fact]
    public async Task RecordReceivable_starts_pending_when_due_in_future()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry entry = await service.RecordReceivableAsync(
                Input(workspaceId, 200m, dueDate: AsOf.AddDays(30)));

            Assert.Equal(CashFlowDirection.Income, entry.Direction);
            Assert.Equal(CashFlowSettlementStatus.Pending, entry.SettlementStatus);
            Assert.Equal(0m, entry.SettledAmount.Amount);
        });
    }

    [Fact]
    public async Task RecordPayable_starts_overdue_when_due_in_past()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry entry = await service.RecordPayableAsync(
                Input(workspaceId, 75m, occurredAt: AsOf, dueDate: AsOf.AddDays(-1)));

            Assert.Equal(CashFlowDirection.Expense, entry.Direction);
            Assert.Equal(CashFlowSettlementStatus.Overdue, entry.SettlementStatus);
        });
    }

    [Fact]
    public async Task Settle_partially_then_fully_advances_status()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry receivable = await service.RecordReceivableAsync(
                Input(workspaceId, 100m, dueDate: AsOf.AddDays(30)));

            CashFlowEntry partial = await service.SettleAsync(receivable.Id, CommerceMoney.From(40m), AsOf);
            Assert.Equal(CashFlowSettlementStatus.PartiallySettled, partial.SettlementStatus);
            Assert.Equal(40m, partial.SettledAmount.Amount);

            CashFlowEntry full = await service.SettleAsync(receivable.Id, CommerceMoney.From(60m), AsOf);
            Assert.Equal(CashFlowSettlementStatus.Settled, full.SettlementStatus);
            Assert.Equal(100m, full.SettledAmount.Amount);
        });
    }

    [Fact]
    public async Task Settle_never_exceeds_gross_amount()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry payable = await service.RecordPayableAsync(
                Input(workspaceId, 50m, dueDate: AsOf.AddDays(10)));

            CashFlowEntry settled = await service.SettleAsync(payable.Id, CommerceMoney.From(999m), AsOf);

            Assert.Equal(50m, settled.SettledAmount.Amount);
            Assert.Equal(CashFlowSettlementStatus.Settled, settled.SettlementStatus);
        });
    }

    [Fact]
    public async Task Settle_with_negative_amount_throws()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            CashFlowEntry receivable = await service.RecordReceivableAsync(Input(workspaceId, 10m));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.SettleAsync(receivable.Id, CommerceMoney.From(-1m), AsOf));
        });
    }

    [Fact]
    public async Task Settle_missing_entry_throws()
    {
        await WithServiceAsync(async (service, _, _) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SettleAsync(Guid.NewGuid(), CommerceMoney.From(1m), AsOf));
        });
    }

    [Fact]
    public async Task PeriodSummary_aggregates_realized_and_outstanding_and_excludes_transfers()
    {
        await WithServiceAsync(async (service, repository, workspaceId) =>
        {
            await service.RecordIncomeAsync(Input(workspaceId, 300m, occurredAt: AsOf));
            await service.RecordExpenseAsync(Input(workspaceId, 120m, occurredAt: AsOf));
            await service.RecordReceivableAsync(Input(workspaceId, 200m, occurredAt: AsOf, dueDate: AsOf.AddDays(30)));
            await service.RecordPayableAsync(Input(workspaceId, 50m, occurredAt: AsOf, dueDate: AsOf.AddDays(30)));

            // A transfer is net-zero and must not affect any aggregate.
            await repository.CreateAsync(new CashFlowEntry
            {
                WorkspaceId = workspaceId,
                Direction = CashFlowDirection.Transfer,
                Amount = CommerceMoney.From(1000m),
                SettledAmount = CommerceMoney.From(1000m),
                SettlementStatus = CashFlowSettlementStatus.Settled,
                OccurredAt = AsOf,
            });

            var period = new DateRange(AsOf.AddDays(-1), AsOf.AddDays(1));
            CashFlowPeriodSummary summary = await service.GetPeriodSummaryAsync(period);

            Assert.Equal(300m, summary.RealizedIncome.Amount);
            Assert.Equal(120m, summary.RealizedExpense.Amount);
            Assert.Equal(180m, summary.NetCashFlow.Amount);
            Assert.Equal(200m, summary.OutstandingReceivable.Amount);
            Assert.Equal(50m, summary.OutstandingPayable.Amount);
        });
    }

    [Fact]
    public async Task PeriodSummary_excludes_entries_outside_the_window()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            await service.RecordIncomeAsync(Input(workspaceId, 500m, occurredAt: AsOf.AddDays(-40)));

            var period = new DateRange(AsOf.AddDays(-7), AsOf);
            CashFlowPeriodSummary summary = await service.GetPeriodSummaryAsync(period);

            Assert.Equal(0m, summary.RealizedIncome.Amount);
        });
    }

    [Fact]
    public async Task HealthScore_is_full_with_no_activity()
    {
        await WithServiceAsync(async (service, _, _) =>
        {
            int score = await service.ComputeHealthScoreAsync(new DateRange(AsOf.AddDays(-7), AsOf));
            Assert.Equal(100, score);
        });
    }

    [Fact]
    public async Task HealthScore_is_lowest_when_only_obligations_exist()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            await service.RecordExpenseAsync(Input(workspaceId, 100m, occurredAt: AsOf));

            int score = await service.ComputeHealthScoreAsync(new DateRange(AsOf.AddDays(-1), AsOf.AddDays(1)));
            Assert.Equal(0, score);
        });
    }

    [Fact]
    public async Task HealthScore_stays_within_bounds_for_mixed_activity()
    {
        await WithServiceAsync(async (service, _, workspaceId) =>
        {
            await service.RecordIncomeAsync(Input(workspaceId, 300m, occurredAt: AsOf));
            await service.RecordExpenseAsync(Input(workspaceId, 100m, occurredAt: AsOf));
            await service.RecordPayableAsync(Input(workspaceId, 100m, occurredAt: AsOf, dueDate: AsOf.AddDays(5)));

            int score = await service.ComputeHealthScoreAsync(new DateRange(AsOf.AddDays(-1), AsOf.AddDays(1)));

            Assert.InRange(score, 0, 100);
            // inflows = 300, outflows = 100 + 100 = 200, ratio = 300/500 = 0.6 -> 60.
            Assert.Equal(60, score);
        });
    }

    // --- Helpers ---

    private static CashFlowEntryInput Input(
        Guid workspaceId,
        decimal amount,
        DateTime? occurredAt = null,
        DateTime? dueDate = null)
        => new()
        {
            WorkspaceId = workspaceId,
            Amount = CommerceMoney.From(amount),
            OccurredAt = occurredAt ?? AsOf,
            DueDate = dueDate,
        };

    private static async Task WithServiceAsync(
        Func<CommerceCashFlowService, ICashFlowEntryRepository, Guid, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-cashflow-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            var repository = new CashFlowEntryRepository(factory);
            var service = new CommerceCashFlowService(repository);

            await body(service, repository, Guid.NewGuid());
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
