using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for <see cref="CommerceEntity"/> soft-delete / archive recoverability.
///
/// Property 4: Soft-delete is recoverable and excluded from active queries.
/// For ANY entity, after a soft-delete OR archive operation:
///   - the entity does NOT appear in active queries (modeled as filtering a collection by
///     <see cref="CommerceEntity.IsActive"/>, i.e. Lifecycle == Active &amp;&amp; DeletedAt == null),
///   - its <see cref="CommerceEntity.DeletedAt"/> is non-null,
///   - its <see cref="CommerceEntity.Lifecycle"/> is the corresponding Archived/Deleted value,
///   - and the entity remains recoverable with its stored data intact: <see cref="CommerceEntity.Recover"/>
///     returns it to Active with DeletedAt null, makes it reappear in active queries, and leaves
///     the stored data unchanged throughout.
///
/// <see cref="CommerceEntity"/> is abstract, so this file defines a small self-contained concrete
/// subclass (<see cref="StoredDataEntity"/>) that carries stored data (a string and an int) so the
/// test can assert that data survives soft-delete/archive and recover. It deliberately does NOT
/// depend on the 18 concrete entities (implemented in parallel by task 3.2).
///
/// **Validates: Requirements 2.9**
/// </summary>
public class SoftDeleteRecoverabilityPropertyTests
{
    /// <summary>
    /// Minimal test-only concrete <see cref="CommerceEntity"/> carrying stored data so the test
    /// can prove data is retained across soft-delete/archive/recover. Kept self-contained and
    /// independent of the concrete domain entities.
    /// </summary>
    private sealed class StoredDataEntity : CommerceEntity
    {
        public StoredDataEntity(string label, int quantity, string? customFieldsJson)
        {
            Label = label;
            Quantity = quantity;
            if (customFieldsJson is not null)
            {
                // Exercises the CustomFieldsJson personalization surface; stored as provided.
                CustomFieldsJson = customFieldsJson;
            }
        }

        /// <summary>Arbitrary stored string payload used to assert data survival.</summary>
        public string Label { get; }

        /// <summary>Arbitrary stored numeric payload used to assert data survival.</summary>
        public int Quantity { get; }
    }

    /// <summary>How the entity is removed from active queries.</summary>
    private enum RemovalKind
    {
        SoftDelete,
        Archive
    }

    // Random stored data plus a random choice of SoftDelete vs Archive and an optional custom-fields payload.
    private static readonly Gen<(string Label, int Quantity, string? CustomFields, RemovalKind Removal)> ScenarioGen =
        Gen.String[0, 64].Select(
            Gen.Int[int.MinValue, int.MaxValue],
            Gen.String[0, 32].Null(),
            Gen.Enum<RemovalKind>(),
            (label, quantity, customFields, removal) => (label, quantity, customFields, removal));

    [Fact]
    public void Property4_soft_delete_or_archive_is_recoverable_and_excluded_from_active_queries()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                var (label, quantity, customFields, removal) = scenario;

                var entity = new StoredDataEntity(label, quantity, customFields);

                // A modeled "active collection" containing this entity plus an unrelated, always-active
                // sibling so we can observe both exclusion and reappearance against active-query filtering.
                var sibling = new StoredDataEntity("sibling", 0, null);
                var store = new List<StoredDataEntity> { entity, sibling };

                // Snapshot the stored data and immutable identity before any lifecycle operation.
                Guid id = entity.Id;
                DateTime createdAt = entity.CreatedAt;
                string labelBefore = entity.Label;
                int quantityBefore = entity.Quantity;
                string? customFieldsBefore = entity.CustomFieldsJson;

                // Precondition: the entity starts active and visible to active queries.
                Assert.True(entity.IsActive);
                Assert.Null(entity.DeletedAt);
                Assert.Equal(EntityLifecycleStatus.Active, entity.Lifecycle);
                Assert.Contains(entity, ActiveQuery(store));

                // --- Soft-delete or archive ---
                EntityLifecycleStatus expectedStatus;
                if (removal == RemovalKind.SoftDelete)
                {
                    entity.SoftDelete();
                    expectedStatus = EntityLifecycleStatus.Deleted;
                }
                else
                {
                    entity.Archive();
                    expectedStatus = EntityLifecycleStatus.Archived;
                }

                // Excluded from active queries, while the unrelated sibling remains visible.
                Assert.False(entity.IsActive);
                Assert.DoesNotContain(entity, ActiveQuery(store));
                Assert.Contains(sibling, ActiveQuery(store));

                // DeletedAt is non-null and Lifecycle is the corresponding archived/deleted value.
                Assert.NotNull(entity.DeletedAt);
                Assert.Equal(expectedStatus, entity.Lifecycle);

                // Stored data and immutable identity are retained (recoverable, not destroyed).
                Assert.Equal(id, entity.Id);
                Assert.Equal(createdAt, entity.CreatedAt);
                Assert.Equal(labelBefore, entity.Label);
                Assert.Equal(quantityBefore, entity.Quantity);
                Assert.Equal(customFieldsBefore, entity.CustomFieldsJson);

                // --- Recover ---
                entity.Recover();

                // Returned to Active, DeletedAt cleared, reappears in active queries.
                Assert.True(entity.IsActive);
                Assert.Null(entity.DeletedAt);
                Assert.Equal(EntityLifecycleStatus.Active, entity.Lifecycle);
                Assert.Contains(entity, ActiveQuery(store));

                // Stored data and immutable identity are unchanged after the full round-trip.
                Assert.Equal(id, entity.Id);
                Assert.Equal(createdAt, entity.CreatedAt);
                Assert.Equal(labelBefore, entity.Label);
                Assert.Equal(quantityBefore, entity.Quantity);
                Assert.Equal(customFieldsBefore, entity.CustomFieldsJson);
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void SoftDelete_excludes_from_active_query_and_recover_restores_with_data_intact()
    {
        var entity = new StoredDataEntity("widget", 42, "{\"k\":\"v\"}");
        var store = new List<StoredDataEntity> { entity };

        entity.SoftDelete();

        Assert.False(entity.IsActive);
        Assert.Equal(EntityLifecycleStatus.Deleted, entity.Lifecycle);
        Assert.NotNull(entity.DeletedAt);
        Assert.Empty(ActiveQuery(store));

        entity.Recover();

        Assert.True(entity.IsActive);
        Assert.Equal(EntityLifecycleStatus.Active, entity.Lifecycle);
        Assert.Null(entity.DeletedAt);
        Assert.Single(ActiveQuery(store));
        Assert.Equal("widget", entity.Label);
        Assert.Equal(42, entity.Quantity);
        Assert.Equal("{\"k\":\"v\"}", entity.CustomFieldsJson);
    }

    [Fact]
    public void Archive_excludes_from_active_query_and_recover_restores_with_data_intact()
    {
        var entity = new StoredDataEntity("gadget", -7, null);
        var store = new List<StoredDataEntity> { entity };

        entity.Archive();

        Assert.False(entity.IsActive);
        Assert.Equal(EntityLifecycleStatus.Archived, entity.Lifecycle);
        Assert.NotNull(entity.DeletedAt);
        Assert.Empty(ActiveQuery(store));

        entity.Recover();

        Assert.True(entity.IsActive);
        Assert.Equal(EntityLifecycleStatus.Active, entity.Lifecycle);
        Assert.Null(entity.DeletedAt);
        Assert.Single(ActiveQuery(store));
        Assert.Equal("gadget", entity.Label);
        Assert.Equal(-7, entity.Quantity);
    }

    /// <summary>
    /// Models an "active query": filters a collection to the entities that are active, i.e.
    /// <see cref="CommerceEntity.IsActive"/> (Lifecycle == Active &amp;&amp; DeletedAt == null).
    /// </summary>
    private static IReadOnlyList<StoredDataEntity> ActiveQuery(IEnumerable<StoredDataEntity> store) =>
        store.Where(e => e.IsActive).ToList();
}
