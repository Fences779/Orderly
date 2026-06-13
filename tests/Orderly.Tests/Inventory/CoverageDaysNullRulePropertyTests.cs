using System;
using System.Collections.Generic;
using System.IO;
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
/// Property-based test for the <c>CoverageDays</c> null rule of
/// <see cref="CommerceInventoryService"/>.
///
/// Property 11: CoverageDays is null exactly when 30-day usage is zero.
/// For ANY inventory metrics request: if <c>AvgDailyUsage30d</c> is 0 then <c>CoverageDays</c>
/// is reported as <c>null</c> (never 0); otherwise <c>CoverageDays</c> equals
/// <c>QuantityAvailable</c> divided by <c>AvgDailyUsage30d</c>.
///
/// The property is exercised end-to-end against the real SQLCipher-backed Commerce repositories
/// (an unencrypted temp database, no mocks). Each generated case produces a fresh inventory item
/// with random available quantity plus a random set of movements whose types, quantities, and
/// timestamps are chosen so that the 30-day outbound usage is sometimes zero and sometimes
/// positive, hitting both branches of the rule.
///
/// **Validates: Requirements 4.10**
/// </summary>
public sealed class CoverageDaysNullRulePropertyTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A single generated movement: type, positive quantity, and how many days before AsOf it occurred.</summary>
    private readonly record struct MovementSpec(InventoryMovementType Type, decimal Quantity, int DaysAgo);

    private static readonly Gen<InventoryMovementType> MovementTypeGen =
        Gen.Int[0, 2].Select(i => i switch
        {
            0 => InventoryMovementType.Inbound,
            1 => InventoryMovementType.Outbound,
            _ => InventoryMovementType.Adjustment,
        });

    // Day offsets span inside the 30-day window (1..29), the boundary (30, excluded), and
    // outside it (31..60) so generation routinely produces both zero and positive 30-day usage.
    private static readonly Gen<MovementSpec> MovementGen =
        MovementTypeGen.Select(
            Gen.Int[1, 100],
            Gen.Int[1, 60],
            (type, qty, daysAgo) => new MovementSpec(type, qty, daysAgo));

    private static readonly Gen<List<MovementSpec>> MovementsGen =
        MovementGen.List[0, 8].Select(list => new List<MovementSpec>(list));

    // Available quantity covers negatives, zero, and positive fractional values; CoverageDays may
    // legitimately be 0 (quantity 0 with positive usage) but is null only when usage is zero.
    private static readonly Gen<decimal> QuantityGen =
        Gen.Int[-500, 5_000].Select(Gen.Int[0, 99], (units, cents) => units + cents / 100m);

    [Fact]
    public void Property11_coverage_days_is_null_exactly_when_thirty_day_usage_is_zero()
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-coverage-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

            var items = new InventoryItemRepository(factory);
            var movements = new InventoryMovementRepository(factory);
            var service = new CommerceInventoryService(items, movements);

            QuantityGen.Select(MovementsGen, (quantity, movements) => (quantity, movements)).Sample(
                tuple =>
                {
                    (decimal quantity, List<MovementSpec> specs) = tuple;
                    Guid workspaceId = Guid.NewGuid();

                    var item = new InventoryItem
                    {
                        Id = Guid.NewGuid(),
                        WorkspaceId = workspaceId,
                        Name = "库存项 " + Guid.NewGuid().ToString("N")[..6],
                        QuantityAvailable = quantity,
                        ReorderThreshold = 1m,
                    };
                    items.CreateAsync(item).GetAwaiter().GetResult();

                    foreach (MovementSpec spec in specs)
                    {
                        movements.CreateAsync(new InventoryMovement
                        {
                            Id = Guid.NewGuid(),
                            WorkspaceId = workspaceId,
                            InventoryItemId = item.Id,
                            MovementType = spec.Type,
                            Quantity = spec.Quantity,
                            OccurredAt = AsOf.AddDays(-spec.DaysAgo),
                        }).GetAwaiter().GetResult();
                    }

                    InventoryMetrics metrics = service.GetMetricsAsync(item.Id, AsOf).GetAwaiter().GetResult();

                    if (metrics.AvgDailyUsage30d == 0m)
                    {
                        // Reported as null (never 0) exactly when 30-day usage is zero.
                        Assert.Null(metrics.CoverageDays);
                    }
                    else
                    {
                        // Otherwise CoverageDays equals QuantityAvailable / AvgDailyUsage30d.
                        Assert.NotNull(metrics.CoverageDays);
                        Assert.Equal(item.QuantityAvailable / metrics.AvgDailyUsage30d, metrics.CoverageDays!.Value);
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
