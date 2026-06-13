using System;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for the audit-timestamp guarantees on <see cref="CommerceEntity"/>.
///
/// Property 3: Mutation preserves CreatedAt and advances UpdatedAt.
/// For ANY entity and ANY mutation of a persisted field, the entity's <see cref="CommerceEntity.CreatedAt"/>
/// is unchanged and its <see cref="CommerceEntity.UpdatedAt"/> is set to a current UTC time greater than
/// or equal to its previous <see cref="CommerceEntity.UpdatedAt"/>.
///
/// Because timer resolution can produce two reads within the same tick, the implementation clamps
/// <c>UpdatedAt</c> to be monotonic non-decreasing (<c>&gt;=</c>), which is exactly what this property asserts.
///
/// **Validates: Requirements 2.8**
/// </summary>
public class AuditTimestampPropertyTests
{
    /// <summary>
    /// Minimal test-only concrete entity. <see cref="CommerceEntity"/> is abstract with a protected
    /// <c>MarkUpdated()</c>, so this subclass exposes a persisted field (<see cref="Name"/>) whose setter
    /// advances the audit timestamp the same way real entities do. It keeps the test self-contained and
    /// independent of the 18 concrete entities.
    /// </summary>
    private sealed class TestCommerceEntity : CommerceEntity
    {
        private string? _name;

        /// <summary>A persisted field whose setter advances <c>UpdatedAt</c> via the base hook.</summary>
        public string? Name
        {
            get => _name;
            set
            {
                _name = value;
                MarkUpdated();
            }
        }

        /// <summary>Drives a persisted-field mutation directly through the protected base hook.</summary>
        public void TouchPersistedField() => MarkUpdated();
    }

    /// <summary>The kinds of persisted-field mutations exercised by the property.</summary>
    private enum MutationKind
    {
        SetCustomFields,
        SetName,
        TouchPersistedField,
        Archive,
        SoftDelete,
        Recover,
    }

    // String payloads applied to CustomFieldsJson / Name, including null and arbitrary content.
    private static readonly Gen<string?> StringPayloadGen =
        Gen.OneOf(
            Gen.Const((string?)null),
            Gen.Const(string.Empty),
            Gen.String);

    private static readonly Gen<MutationKind> MutationKindGen = Gen.OneOfConst(
        MutationKind.SetCustomFields,
        MutationKind.SetName,
        MutationKind.TouchPersistedField,
        MutationKind.Archive,
        MutationKind.SoftDelete,
        MutationKind.Recover);

    // A single mutation: a kind plus an optional string payload (used only by the string-setting kinds).
    private static readonly Gen<(MutationKind Kind, string? Payload)> MutationGen =
        MutationKindGen.Select(StringPayloadGen, (kind, payload) => (kind, payload));

    // A non-empty sequence of mutations applied in order to one entity.
    private static readonly Gen<(MutationKind Kind, string? Payload)[]> MutationSequenceGen =
        MutationGen.Array[1, 25];

    [Fact]
    public void Property3_mutation_preserves_CreatedAt_and_advances_UpdatedAt()
    {
        MutationSequenceGen.Sample(
            mutations =>
            {
                var entity = new TestCommerceEntity();

                // CreatedAt is fixed at construction; capture it once for the whole sequence.
                DateTime originalCreatedAt = entity.CreatedAt;
                Assert.Equal(DateTimeKind.Utc, originalCreatedAt.Kind);

                foreach ((MutationKind kind, string? payload) in mutations)
                {
                    DateTime previousUpdatedAt = entity.UpdatedAt;

                    ApplyMutation(entity, kind, payload);

                    DateTime now = DateTime.UtcNow;

                    // CreatedAt never changes across any mutation.
                    Assert.Equal(originalCreatedAt, entity.CreatedAt);

                    // UpdatedAt is monotonic non-decreasing.
                    Assert.True(
                        entity.UpdatedAt >= previousUpdatedAt,
                        $"UpdatedAt regressed after {kind}: {entity.UpdatedAt:O} < {previousUpdatedAt:O}");

                    // UpdatedAt is a current UTC time (not in the future, correctly kinded).
                    Assert.Equal(DateTimeKind.Utc, entity.UpdatedAt.Kind);
                    Assert.True(
                        entity.UpdatedAt <= now,
                        $"UpdatedAt is in the future after {kind}: {entity.UpdatedAt:O} > {now:O}");
                }
            },
            iter: PbtConfig.MinIterations);
    }

    private static void ApplyMutation(TestCommerceEntity entity, MutationKind kind, string? payload)
    {
        switch (kind)
        {
            case MutationKind.SetCustomFields:
                entity.CustomFieldsJson = payload;
                break;
            case MutationKind.SetName:
                entity.Name = payload;
                break;
            case MutationKind.TouchPersistedField:
                entity.TouchPersistedField();
                break;
            case MutationKind.Archive:
                entity.Archive();
                break;
            case MutationKind.SoftDelete:
                entity.SoftDelete();
                break;
            case MutationKind.Recover:
                entity.Recover();
                break;
            default:
                throw new InvalidOperationException($"Unhandled mutation kind: {kind}");
        }
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Setting_custom_fields_preserves_CreatedAt_and_does_not_regress_UpdatedAt()
    {
        var entity = new TestCommerceEntity();
        DateTime createdAt = entity.CreatedAt;
        DateTime before = entity.UpdatedAt;

        entity.CustomFieldsJson = "{\"k\":\"v\"}";

        Assert.Equal(createdAt, entity.CreatedAt);
        Assert.True(entity.UpdatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, entity.UpdatedAt.Kind);
    }

    [Fact]
    public void Archive_then_recover_advances_UpdatedAt_and_keeps_CreatedAt()
    {
        var entity = new TestCommerceEntity();
        DateTime createdAt = entity.CreatedAt;

        DateTime beforeArchive = entity.UpdatedAt;
        entity.Archive();
        Assert.True(entity.UpdatedAt >= beforeArchive);

        DateTime beforeRecover = entity.UpdatedAt;
        entity.Recover();
        Assert.True(entity.UpdatedAt >= beforeRecover);

        Assert.Equal(createdAt, entity.CreatedAt);
    }
}
