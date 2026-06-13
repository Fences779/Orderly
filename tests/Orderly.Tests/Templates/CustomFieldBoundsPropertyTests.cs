using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Templates;

/// <summary>
/// Property-based test for <see cref="CommerceCustomFieldService"/> (Req 5.4), exercised through the
/// real SQLCipher-backed <see cref="CustomFieldDefinitionRepository"/> against an unencrypted temp
/// SQLite database (no mocks).
///
/// <para><b>Property 17: Custom-field definitions are bounded and singly-typed.</b> For ANY sequence of
/// additions, a <see cref="CustomFieldDefinition"/> is associated with exactly one entity type and at
/// most <see cref="ICustomFieldService.MaxDefinitionsPerEntityType"/> (100) per entity type are allowed;
/// the 101st add for an entity type is rejected with
/// <see cref="CustomFieldDefinitionOutcome.CustomFieldLimitExceeded"/> and persists nothing.</para>
///
/// <para>The generator picks 1–3 distinct <see cref="BusinessEntityType"/> groups within a single
/// template and, for each group, a requested add count drawn either from a small range or a
/// near-boundary range (around 100) so the per-type limit and its rejection are exercised frequently.
/// For each group the test performs the requested adds in sequence and asserts that exactly the first
/// <c>min(requested, 100)</c> succeed (each persisting a definition whose
/// <see cref="CustomFieldDefinition.TargetEntityType"/> equals the group's type — the singly-typed
/// half), every add beyond 100 is rejected with the limit-exceeded outcome and persists nothing, and
/// the active count per type equals <c>min(requested, 100)</c>. Cross-type independence is verified by
/// confirming the total active count equals the sum of the per-type minima.</para>
///
/// **Validates: Requirements 5.4**
/// </summary>
public sealed class CustomFieldBoundsPropertyTests
{
    private const int Max = ICustomFieldService.MaxDefinitionsPerEntityType;

    private static readonly BusinessEntityType[] AllEntityTypes = Enum.GetValues<BusinessEntityType>();

    private static readonly Gen<BusinessEntityType> EntityTypeGen =
        Gen.Int[0, AllEntityTypes.Length - 1].Select(i => AllEntityTypes[i]);

    // A requested add count: usually small (cheap) but often near the per-type boundary so the 100/101
    // limit is straddled and the rejection path is exercised across iterations.
    private static readonly Gen<int> RequestedCountGen = Gen.OneOf(
        Gen.Int[0, 5],
        Gen.Int[Max - 2, Max + 3]);

    // 1–3 distinct entity-type groups, each paired with its requested add count.
    private static readonly Gen<(BusinessEntityType Type, int Requested)[]> GroupsGen =
        from types in EntityTypeGen.Array[1, 3].Select(a => a.Distinct().ToArray())
        from counts in RequestedCountGen.Array[types.Length]
        select types.Zip(counts, (t, c) => (t, c)).ToArray();

    [Fact]
    public void Property17_custom_field_definitions_are_bounded_and_singly_typed()
    {
        GroupsGen.Sample(
            groups =>
            {
                WithTempDatabase(path =>
                {
                    ICustomFieldService service = NewService(path);
                    Guid templateId = Guid.NewGuid();

                    foreach ((BusinessEntityType type, int requested) in groups)
                    {
                        int expectedAccepted = Math.Min(requested, Max);

                        for (int i = 0; i < requested; i++)
                        {
                            CustomFieldDefinitionResult result = service
                                .AddDefinitionAsync(NewDefinition(templateId, type, $"{type}_{i}"))
                                .GetAwaiter().GetResult();

                            if (i < Max)
                            {
                                // Within the per-type bound: accepted and singly-typed.
                                Assert.True(result.IsAdded, $"[{type}] add #{i} should succeed: {result.Error}");
                                Assert.Equal(CustomFieldDefinitionOutcome.Added, result.Outcome);
                                Assert.NotNull(result.Definition);
                                Assert.Equal(type, result.Definition!.TargetEntityType);
                                Assert.Equal(templateId, result.Definition.TemplateId);
                            }
                            else
                            {
                                // The 101st (and beyond) for this type is rejected and persists nothing.
                                Assert.False(result.IsAdded, $"[{type}] add #{i} should be rejected");
                                Assert.True(result.IsLimitExceeded);
                                Assert.Equal(CustomFieldDefinitionOutcome.CustomFieldLimitExceeded, result.Outcome);
                                Assert.Null(result.Definition);
                                Assert.False(string.IsNullOrWhiteSpace(result.Error));
                            }
                        }

                        // Active count for the type equals min(requested, 100); each is the target type.
                        IReadOnlyList<CustomFieldDefinition> active = service
                            .GetByEntityTypeAsync(templateId, type).GetAwaiter().GetResult();
                        Assert.Equal(expectedAccepted, active.Count);
                        Assert.All(active, d => Assert.Equal(type, d.TargetEntityType));
                    }

                    // Cross-type independence: total active equals the sum of the per-type minima.
                    int expectedTotal = groups.Sum(g => Math.Min(g.Requested, Max));
                    int actualTotal = service.GetByTemplateAsync(templateId).GetAwaiter().GetResult().Count;
                    Assert.Equal(expectedTotal, actualTotal);

                    return true;
                });
            },
            iter: PbtConfig.MinIterations);
    }

    private static CustomFieldDefinition NewDefinition(Guid templateId, BusinessEntityType entityType, string key) => new()
    {
        Id = Guid.NewGuid(),
        TemplateId = templateId,
        TargetEntityType = entityType,
        DataType = CustomFieldDataType.Text,
        FieldKey = key,
        DisplayName = key,
    };

    private static ICustomFieldService NewService(string databasePath)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();
        return new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
    }

    /// <summary>
    /// Creates a unique temp database file path, runs <paramref name="action"/>, then removes the temp
    /// file (and clears the SQLite connection pool) in a finally block so no temp artifacts leak.
    /// </summary>
    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"orderly-custom-field-bounds-{Guid.NewGuid():N}.db");

        try
        {
            return action(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteDatabaseFiles(path);
        }
    }

    private static void TryDeleteDatabaseFiles(string path)
    {
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
