using System;
using System.Collections.Generic;
using System.IO;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Cashflow;

/// <summary>
/// Property-based test for the cash-flow health-score bound of <see cref="CommerceCashFlowService"/>
/// (the <see cref="ICashFlowService"/> implementation).
///
/// <para><b>Property 12: Cash-flow health score is a bounded integer.</b>
/// For ANY cash-flow dataset, the computed cash-flow health score is an integer satisfying
/// 0 ≤ score ≤ 100. Because the score is already typed as <see cref="int"/>, the universal claim
/// the property must defend is the inclusive [0, 100] bound holding across arbitrary inputs:
/// any mix of income/expense/transfer directions, any settlement state (including over-settled
/// entries whose settled amount exceeds the gross amount), any amounts across the full
/// <see cref="CommerceMoney"/> range, and any period that includes, partially overlaps, or excludes
/// the generated entries.</para>
///
/// <para>The property is exercised end-to-end against the real SQLCipher-backed Commerce repository
/// (an unencrypted temp database, no mocks). Each generated case seeds a random dataset of cash-flow
/// entries, then asserts both <see cref="ICashFlowService.ComputeHealthScoreAsync"/> and the
/// <see cref="CashFlowPeriodSummary.HealthScore"/> returned by
/// <see cref="ICashFlowService.GetPeriodSummaryAsync"/> fall within [0, 100].</para>
///
/// **Validates: Requirements 4.12**
/// </summary>
public sealed class CashFlowHealthScoreBoundsPropertyTests
{
    // A fixed anchor keeps generated day offsets deterministic and reproducible.
    private static readonly DateTime Anchor = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The largest representable monetary value in cents (CommerceMoney range is ±999,999,999.99).
    private const long MaxCents = 99_999_999_999L;

    /// <summary>One generated cash-flow entry: its direction, gross/settled amounts, and timing.</summary>
    private readonly record struct EntrySpec(
        int Direction,
        long AmountCents,
        long SettledCents,
        int OccurredOffsetDays,
        bool HasDueDate,
        int DueOffsetDays);

    private static readonly Gen<int> DirectionGen = Gen.Int[0, 2];

    // Amounts span the full valid CommerceMoney range, including zero, so creation never throws and
    // the aggregate stress includes very large sums.
    private static readonly Gen<long> CentsGen = Gen.Long[0, MaxCents];

    private static readonly Gen<int> DayOffsetGen = Gen.Int[-400, 400];

    private static readonly Gen<EntrySpec> EntryGen =
        from direction in DirectionGen
        from amount in CentsGen
        // Settled amount is independent of the gross amount on purpose: this includes over-settled
        // entries (settled > gross) to confirm the outstanding-balance clamp keeps the score bounded.
        from settled in CentsGen
        from occurred in DayOffsetGen
        from hasDue in Gen.Bool
        from due in DayOffsetGen
        select new EntrySpec(direction, amount, settled, occurred, hasDue, due);

    private static readonly Gen<List<EntrySpec>> DatasetGen =
        EntryGen.List[0, 20].Select(list => new List<EntrySpec>(list));

    // The query window: a start offset and a non-negative length so end >= start always holds.
    private static readonly Gen<(int StartOffset, int Length)> PeriodGen =
        from start in DayOffsetGen
        from length in Gen.Int[0, 800]
        select (start, length);

    [Fact]
    public void Property12_health_score_is_a_bounded_integer()
    {
        DatasetGen.Select(PeriodGen).Sample(
            tuple =>
            {
                (List<EntrySpec> specs, (int StartOffset, int Length) window) = tuple;

                // Each generated case runs against its own freshly initialized database so datasets
                // never leak between iterations (including under CsCheck shrinking and re-runs). This
                // guarantees ComputeHealthScoreAsync and GetPeriodSummaryAsync read the same isolated
                // state, making the property deterministic.
                string path = Path.Combine(Path.GetTempPath(), $"orderly-cashflow-health-{Guid.NewGuid():N}.db");
                try
                {
                    var factory = new SqliteConnectionFactory(path);
                    new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

                    var repository = new CashFlowEntryRepository(factory);
                    Guid workspaceId = Guid.NewGuid();

                    foreach (EntrySpec spec in specs)
                    {
                        CashFlowEntry entry = ToEntry(workspaceId, spec);
                        repository.CreateAsync(entry).GetAwaiter().GetResult();
                    }

                    var period = new DateRange(
                        Anchor.AddDays(window.StartOffset),
                        Anchor.AddDays(window.StartOffset + window.Length));

                    var service = new CommerceCashFlowService(repository);
                    int score = service.ComputeHealthScoreAsync(period).GetAwaiter().GetResult();

                    // The core claim: the health score is a bounded integer in [0, 100] (Req 4.12).
                    Assert.InRange(score, 0, 100);

                    // The period summary must report the same bounded score for the same dataset.
                    CashFlowPeriodSummary summary = service
                        .GetPeriodSummaryAsync(period)
                        .GetAwaiter().GetResult();
                    Assert.InRange(summary.HealthScore, 0, 100);
                    Assert.Equal(score, summary.HealthScore);
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

    /// <summary>Materializes a generated spec into a persistable <see cref="CashFlowEntry"/>.</summary>
    private static CashFlowEntry ToEntry(Guid workspaceId, EntrySpec spec)
    {
        DateTime occurredAt = Anchor.AddDays(spec.OccurredOffsetDays);

        return new CashFlowEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Direction = (CashFlowDirection)spec.Direction,
            Amount = CommerceMoney.From(spec.AmountCents / 100m),
            SettledAmount = CommerceMoney.From(spec.SettledCents / 100m),
            SettlementStatus = CashFlowSettlementStatus.Pending,
            OccurredAt = occurredAt,
            DueDate = spec.HasDueDate ? Anchor.AddDays(spec.DueOffsetDays) : null,
        };
    }
}
