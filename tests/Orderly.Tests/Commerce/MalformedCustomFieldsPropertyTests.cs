using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for the Commerce repository <c>CustomFieldsJson</c> save boundary
/// (<see cref="CommerceRepositoryBase{TEntity}.CreateAsync"/> /
/// <see cref="CommerceRepositoryBase{TEntity}.UpdateAsync"/>), exercised through a concrete
/// repository (<see cref="ProductRepository"/>) against a real, unencrypted temp SQLite database.
///
/// Property 7: Malformed custom fields are rejected without side effects.
/// For ANY non-null string that is NOT well-formed JSON, attempting to save an entity carrying
/// that value as <c>CustomFieldsJson</c> is rejected with <see cref="InvalidCustomFieldsException"/>
/// and leaves all existing persisted data unchanged.
///
/// The test covers both save paths:
/// <list type="bullet">
///   <item>Create: first persist a valid entity (valid or null <c>CustomFieldsJson</c>), then attempt
///   to Create a second entity carrying a malformed <c>CustomFieldsJson</c>. The Create must throw and
///   persist nothing — the new id is absent and the active row count is unchanged.</item>
///   <item>Update: take the persisted valid entity, set a malformed <c>CustomFieldsJson</c>, and attempt
///   an Update. The Update must throw and the re-read row must equal the original snapshot (custom
///   fields plus every other field and the audit timestamps intact).</item>
/// </list>
///
/// Malformed inputs are generated two ways: a fixed bank of definitely-malformed payloads
/// (unterminated objects/arrays/strings, trailing junk, bare keywords) plus arbitrary text filtered
/// through <see cref="JsonDocument.Parse(string)"/> so any string that happens to parse as valid JSON
/// (a bare number, <c>true</c>/<c>false</c>/<c>null</c>, a quoted string, etc.) is skipped and the
/// property only asserts on truly-malformed inputs. Temp database files are removed in a
/// <c>finally</c> block.
///
/// **Validates: Requirements 3.12**
/// </summary>
public class MalformedCustomFieldsPropertyTests
{
    // Definitely-malformed JSON payloads. Each is non-null and fails JsonDocument.Parse under the
    // default (strict) reader options: unterminated containers/strings, trailing commas, trailing
    // junk, and truncated keywords.
    private static readonly Gen<string> ConstMalformedGen = Gen.OneOfConst(
        "{",
        "}",
        "[",
        "[1,2",
        "{\"k\":1",
        "{ \"k\": }",
        "{ : }",
        "{\"a\":1,}",
        "[1,2,]",
        "\"unterminated",
        "{\"a\":}",
        "{}{}",
        "nul",
        "tru",
        "fals",
        "garbage text {[",
        "123abc",
        "{\"a\":1}trailing");

    // Arbitrary text, kept to truly-malformed cases only by filtering out anything that parses as
    // well-formed JSON (e.g. a bare number, a keyword, or an accidentally-balanced structure).
    private static readonly Gen<string> ArbitraryMalformedGen =
        Gen.String[0, 40].Where(s => !IsWellFormedJson(s));

    // Weight the curated bank higher than the arbitrary stream so each run reliably exercises the
    // hand-picked malformed shapes while still covering randomly-generated malformed text.
    private static readonly Gen<string> MalformedJsonGen =
        Gen.OneOf(ConstMalformedGen, ConstMalformedGen, ArbitraryMalformedGen);

    // Valid baseline CustomFieldsJson for the pre-existing persisted entity: null (allowed, skips
    // validation) or assorted well-formed JSON shapes.
    private static readonly Gen<string?> ValidBaselineGen = Gen.OneOf(
        Gen.Const((string?)null),
        Gen.Const((string?)"{}"),
        Gen.Const((string?)"{\"region\":\"A\"}"),
        Gen.Const((string?)"[1,2,3]"),
        Gen.Const((string?)"\"text\""),
        Gen.Const((string?)"42"));

    [Fact]
    public void Property7_malformed_custom_fields_are_rejected_without_side_effects()
    {
        var scenarioGen =
            from baseline in ValidBaselineGen
            from malformed in MalformedJsonGen
            select (baseline, malformed);

        scenarioGen.Sample(
            scenario =>
            {
                (string? baselineCustomFields, string malformedCustomFields) = scenario;

                WithTempDatabase(path =>
                {
                    InitSchema(path);
                    var repository = new ProductRepository(new SqliteConnectionFactory(path));

                    // Persist a valid entity that must remain untouched by any rejected save.
                    Product persisted = NewProduct(baselineCustomFields);
                    repository.CreateAsync(persisted).GetAwaiter().GetResult();

                    Product original = GetById(repository, persisted.Id);
                    int countBefore = CountActive(repository);

                    // --- Update path: malformed value must be rejected and change nothing. ---
                    Product toUpdate = GetById(repository, persisted.Id);
                    toUpdate.CustomFieldsJson = malformedCustomFields;
                    Assert.Throws<InvalidCustomFieldsException>(
                        () => repository.UpdateAsync(toUpdate).GetAwaiter().GetResult());

                    Product afterFailedUpdate = GetById(repository, persisted.Id);
                    AssertSameRow(original, afterFailedUpdate);
                    Assert.Equal(countBefore, CountActive(repository));

                    // --- Create path: malformed value must be rejected and persist nothing. ---
                    Product toCreate = NewProduct(malformedCustomFields);
                    Assert.Throws<InvalidCustomFieldsException>(
                        () => repository.CreateAsync(toCreate).GetAwaiter().GetResult());

                    Assert.Null(repository.GetByIdAsync(toCreate.Id).GetAwaiter().GetResult());
                    Assert.Equal(countBefore, CountActive(repository));

                    // The original row is still intact after the rejected create as well.
                    AssertSameRow(original, GetById(repository, persisted.Id));

                    return true;
                });
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Update_with_malformed_custom_fields_throws_and_leaves_row_unchanged()
    {
        WithTempDatabase(path =>
        {
            InitSchema(path);
            var repository = new ProductRepository(new SqliteConnectionFactory(path));

            Product persisted = NewProduct("{\"k\":\"v\"}");
            repository.CreateAsync(persisted).GetAwaiter().GetResult();
            Product original = GetById(repository, persisted.Id);

            Product toUpdate = GetById(repository, persisted.Id);
            toUpdate.CustomFieldsJson = "{ not json";
            Assert.Throws<InvalidCustomFieldsException>(
                () => repository.UpdateAsync(toUpdate).GetAwaiter().GetResult());

            AssertSameRow(original, GetById(repository, persisted.Id));
            return true;
        });
    }

    [Fact]
    public void Create_with_malformed_custom_fields_throws_and_persists_nothing()
    {
        WithTempDatabase(path =>
        {
            InitSchema(path);
            var repository = new ProductRepository(new SqliteConnectionFactory(path));

            Product toCreate = NewProduct("[1,2");
            Assert.Throws<InvalidCustomFieldsException>(
                () => repository.CreateAsync(toCreate).GetAwaiter().GetResult());

            Assert.Null(repository.GetByIdAsync(toCreate.Id).GetAwaiter().GetResult());
            Assert.Empty(repository.GetAllAsync().GetAwaiter().GetResult());
            return true;
        });
    }

    [Fact]
    public void Null_custom_fields_are_allowed_on_save()
    {
        WithTempDatabase(path =>
        {
            InitSchema(path);
            var repository = new ProductRepository(new SqliteConnectionFactory(path));

            Product product = NewProduct(null);
            repository.CreateAsync(product).GetAwaiter().GetResult();

            Product stored = GetById(repository, product.Id);
            Assert.Null(stored.CustomFieldsJson);
            return true;
        });
    }

    // --- Helpers ---

    private static bool IsWellFormedJson(string value)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Product NewProduct(string? customFieldsJson) => new()
    {
        WorkspaceId = Guid.NewGuid(),
        Name = "商品 A",
        Code = "P-001",
        ProductType = ProductType.Physical,
        Description = "示例描述",
        DefaultPrice = CommerceMoney.From(12.50m),
        DefaultCost = CommerceMoney.From(8.00m),
        CustomFieldsJson = customFieldsJson,
    };

    private static Product GetById(ProductRepository repository, Guid id)
    {
        Product? entity = repository.GetByIdAsync(id).GetAwaiter().GetResult();
        Assert.NotNull(entity);
        return entity!;
    }

    private static int CountActive(ProductRepository repository)
        => repository.GetAllAsync().GetAwaiter().GetResult().Count;

    /// <summary>
    /// Asserts that a re-read row is byte-for-byte equivalent to the original snapshot across the
    /// custom-fields value, every other persisted field, and the audit timestamps — confirming a
    /// rejected save left existing persisted data unchanged.
    /// </summary>
    private static void AssertSameRow(Product expected, Product actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.WorkspaceId, actual.WorkspaceId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Code, actual.Code);
        Assert.Equal(expected.ProductType, actual.ProductType);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.DefaultPrice, actual.DefaultPrice);
        Assert.Equal(expected.DefaultCost, actual.DefaultCost);
        Assert.Equal(expected.CustomFieldsJson, actual.CustomFieldsJson);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.DeletedAt, actual.DeletedAt);
        Assert.Equal(expected.Lifecycle, actual.Lifecycle);
    }

    private static void InitSchema(string databasePath)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        var initializer = new CommerceSchemaInitializer(factory);
        initializer.InitializeAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates a unique temp database file path, runs <paramref name="action"/>, then removes the
    /// temp file (and clears the SQLite connection pool) in a finally block so no temp artifacts leak.
    /// </summary>
    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"orderly-malformed-custom-fields-{Guid.NewGuid():N}.db");

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
