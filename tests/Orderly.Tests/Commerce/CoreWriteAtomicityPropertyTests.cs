using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based test for the atomicity of the project's <c>Core_Write_Transaction</c>
/// (<see cref="CoreWriteTransaction"/>), the mechanism every core business write uses.
///
/// <para><b>Property 19: Core write operations are atomic.</b> For ANY core business write
/// operation and ANY injected failure at an arbitrary point within its Core_Write_Transaction, the
/// resulting data state equals the pre-operation state, with no partial update.</para>
///
/// <para>The test models a core write as a generated sequence of repository writes performed inside
/// a single <see cref="CoreWriteTransaction"/> against the real SQLCipher-backed Commerce
/// repositories (no mocks) over an unencrypted temp database. Each generated scenario fixes an
/// arbitrary <c>failureIndex</c> in <c>[0, writeCount]</c> — the point at which a failure is injected
/// (before the first write, between any two writes, or after the last write but before commit). The
/// transaction is abandoned at that point (it never commits), so disposal must roll the whole thing
/// back. The test asserts:</para>
/// <list type="number">
///   <item><description>after the aborted write the full data snapshot is byte-for-byte equal to the pre-operation snapshot (atomic rollback, no partial update);</description></item>
///   <item><description>committing the very same write sequence against an identically-seeded database DOES change the data — a non-triviality guard proving the writes were real, so the rollback equality in (1) is meaningful.</description></item>
/// </list>
/// Each scenario touches two tables (inventory items are updated, inventory movements are inserted),
/// so the property exercises all-or-nothing behavior spanning more than one table. Temp database
/// files are removed in a <c>finally</c> block.
///
/// **Validates: Requirements 18.1, 18.3**
/// </summary>
public class CoreWriteAtomicityPropertyTests
{
    private static readonly DateTime OccurredAt = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A unique, recognizable exception used as the injected mid-transaction failure.</summary>
    private sealed class InjectedFailureException : Exception
    {
    }

    /// <summary>One planned write: set inventory item <see cref="TargetIndex"/>'s quantity to
    /// <see cref="NewQuantity"/> and record an outbound movement of <see cref="MovementQuantity"/>.</summary>
    private readonly record struct PlannedWrite(int TargetIndex, decimal NewQuantity, decimal MovementQuantity);

    /// <summary>A generated atomicity scenario: the initial item quantities, the planned writes, and
    /// the arbitrary point at which the transaction is abandoned without committing.</summary>
    private sealed record Scenario(
        decimal[] InitialQuantities,
        PlannedWrite[] Writes,
        int FailureIndex);

    private static readonly Gen<decimal> QuantityGen = Gen.Int[0, 1000].Select(i => (decimal)i);

    private static readonly Gen<Scenario> ScenarioGen =
        from itemCount in Gen.Int[1, 4]
        from initialQuantities in QuantityGen.Array[itemCount]
        from writeCount in Gen.Int[1, 6]
        from writes in
            (from target in Gen.Int[0, itemCount - 1]
             from newQuantity in QuantityGen
             from movementQuantity in Gen.Int[1, 500].Select(i => (decimal)i)
             select new PlannedWrite(target, newQuantity, movementQuantity)).Array[writeCount]
        // failureIndex in [0, writeCount]: 0 = fail before any write, writeCount = fail after the
        // last write but before commit. Either way the transaction never commits.
        from failureIndex in Gen.Int[0, writeCount]
        select new Scenario(initialQuantities, writes, failureIndex);

    [Fact]
    public void Property19_core_write_is_atomic_under_injected_failure()
    {
        ScenarioGen.Sample(
            scenario => RunScenario(scenario),
            iter: PbtConfig.MinIterations);
    }

    // --- Focused examples complementing the property above ---

    [Fact]
    public void Failure_before_any_write_leaves_data_unchanged()
    {
        RunScenario(new Scenario(
            InitialQuantities: new[] { 10m, 20m },
            Writes: new[] { new PlannedWrite(0, 5m, 5m), new PlannedWrite(1, 7m, 13m) },
            FailureIndex: 0));
    }

    [Fact]
    public void Failure_after_last_write_but_before_commit_rolls_everything_back()
    {
        RunScenario(new Scenario(
            InitialQuantities: new[] { 10m, 20m, 30m },
            Writes: new[]
            {
                new PlannedWrite(0, 1m, 9m),
                new PlannedWrite(2, 2m, 28m),
                new PlannedWrite(0, 0m, 1m),
            },
            FailureIndex: 3));
    }

    // --- Scenario runner ---

    private static void RunScenario(Scenario scenario)
    {
        // Stable identities so the aborted database and the committed database are seeded identically.
        Guid workspaceId = Guid.NewGuid();
        Guid[] itemIds = scenario.InitialQuantities.Select(_ => Guid.NewGuid()).ToArray();

        // 1) Pre-operation snapshot, captured from a freshly-seeded database.
        string preSnapshot = WithTempDatabase(path =>
        {
            (_, IInventoryItemRepository _, IInventoryMovementRepository _) = Seed(path, workspaceId, itemIds, scenario.InitialQuantities);
            return CaptureSnapshot(path);
        });

        // 2) Aborted run: apply the writes up to the injected failure point inside one
        //    Core_Write_Transaction, never commit, then snapshot.
        string abortedSnapshot = WithTempDatabase(path =>
        {
            (SqliteConnectionFactory factory, IInventoryItemRepository items, IInventoryMovementRepository movements) =
                Seed(path, workspaceId, itemIds, scenario.InitialQuantities);

            try
            {
                RunCoreWriteAsync(factory, items, movements, workspaceId, itemIds, scenario, commit: false)
                    .GetAwaiter().GetResult();
            }
            catch (InjectedFailureException)
            {
                // Expected: the injected failure propagated out of the transaction scope, whose
                // disposal rolled everything back.
            }

            return CaptureSnapshot(path);
        });

        // The aborted write left the data exactly as it was before the operation (Req 18.1, 18.3).
        Assert.Equal(preSnapshot, abortedSnapshot);

        // 3) Non-triviality guard: committing the SAME write sequence against an identically-seeded
        //    database DOES change the data, proving the writes were real and the rollback above was
        //    not vacuously true.
        string committedSnapshot = WithTempDatabase(path =>
        {
            (SqliteConnectionFactory factory, IInventoryItemRepository items, IInventoryMovementRepository movements) =
                Seed(path, workspaceId, itemIds, scenario.InitialQuantities);

            RunCoreWriteAsync(factory, items, movements, workspaceId, itemIds, scenario, commit: true)
                .GetAwaiter().GetResult();

            return CaptureSnapshot(path);
        });

        // Every scenario has at least one write, and committing always inserts at least one movement
        // (the pre-operation snapshot has none), so the committed state must differ from the original.
        Assert.NotEqual(preSnapshot, committedSnapshot);
    }

    /// <summary>
    /// Executes the generated write sequence inside a single <see cref="CoreWriteTransaction"/>. When
    /// <paramref name="commit"/> is false the transaction is abandoned at <c>FailureIndex</c> by
    /// throwing <see cref="InjectedFailureException"/>, so disposal rolls everything back. When
    /// <paramref name="commit"/> is true every write is applied and the transaction commits.
    /// <see cref="CoreWriteTransaction.Begin"/> is called synchronously (per its contract) so the
    /// ambient transaction is visible to the awaited repository calls that follow.
    /// </summary>
    private static async Task RunCoreWriteAsync(
        SqliteConnectionFactory factory,
        IInventoryItemRepository items,
        IInventoryMovementRepository movements,
        Guid workspaceId,
        IReadOnlyList<Guid> itemIds,
        Scenario scenario,
        bool commit)
    {
        using CoreWriteTransaction transaction = CoreWriteTransaction.Begin(factory);

        for (int i = 0; i < scenario.Writes.Length; i++)
        {
            if (!commit && i == scenario.FailureIndex)
            {
                throw new InjectedFailureException();
            }

            PlannedWrite write = scenario.Writes[i];
            Guid itemId = itemIds[write.TargetIndex];

            InventoryItem item = await items.GetByIdAsync(itemId).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Seeded inventory item '{itemId}' was not found.");
            item.QuantityAvailable = write.NewQuantity;
            await items.UpdateAsync(item).ConfigureAwait(false);

            await movements.CreateAsync(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                InventoryItemId = itemId,
                MovementType = InventoryMovementType.Outbound,
                Quantity = write.MovementQuantity,
                OccurredAt = OccurredAt,
            }).ConfigureAwait(false);
        }

        if (!commit)
        {
            // Inject a failure after the final write but before commit (FailureIndex == writeCount).
            throw new InjectedFailureException();
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    // --- Seeding, snapshotting, and temp-database helpers ---

    private static (SqliteConnectionFactory Factory, IInventoryItemRepository Items, IInventoryMovementRepository Movements) Seed(
        string path,
        Guid workspaceId,
        IReadOnlyList<Guid> itemIds,
        IReadOnlyList<decimal> initialQuantities)
    {
        var factory = new SqliteConnectionFactory(path);
        new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

        var items = new InventoryItemRepository(factory);
        var movements = new InventoryMovementRepository(factory);

        for (int i = 0; i < itemIds.Count; i++)
        {
            items.CreateAsync(new InventoryItem
            {
                Id = itemIds[i],
                WorkspaceId = workspaceId,
                Name = "库存项 " + itemIds[i].ToString("N")[..6],
                QuantityAvailable = initialQuantities[i],
                ReorderThreshold = 0m,
            }).GetAwaiter().GetResult();
        }

        return (factory, items, movements);
    }

    /// <summary>
    /// Builds a deterministic, canonical snapshot of the data the core write touches: every inventory
    /// item (id + quantity) ordered by id, then every inventory movement (item + type + quantity)
    /// ordered canonically. Movement ids are intentionally excluded because they are randomly
    /// generated per run; ordering is fixed so the snapshot depends only on data content.
    /// </summary>
    private static string CaptureSnapshot(string path)
    {
        var factory = new SqliteConnectionFactory(path);
        var items = new InventoryItemRepository(factory);
        var movements = new InventoryMovementRepository(factory);

        IReadOnlyList<InventoryItem> allItems = items.GetAllAsync().GetAwaiter().GetResult();
        IReadOnlyList<InventoryMovement> allMovements = movements.GetAllAsync().GetAwaiter().GetResult();

        var builder = new StringBuilder();

        foreach (InventoryItem item in allItems.OrderBy(i => i.Id))
        {
            builder.Append("item|")
                .Append(item.Id.ToString("N")).Append('|')
                .Append(item.QuantityAvailable.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        foreach (InventoryMovement movement in allMovements
            .OrderBy(m => m.InventoryItemId)
            .ThenBy(m => m.MovementType)
            .ThenBy(m => m.Quantity))
        {
            builder.Append("movement|")
                .Append(movement.InventoryItemId.ToString("N")).Append('|')
                .Append(movement.MovementType).Append('|')
                .Append(movement.Quantity.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"orderly-core-write-atomicity-{Guid.NewGuid():N}.db");

        try
        {
            return action(path);
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
                    // Best-effort cleanup; a transiently-locked temp file is not a test failure.
                }
            }
        }
    }
}
