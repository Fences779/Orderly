using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Property-based tests for <see cref="CommerceBusinessTemplateService.ImportAsync"/> (Req 5.2),
/// exercised through the real SQLCipher-backed Commerce repositories
/// (<see cref="BusinessTemplateRepository"/>, <see cref="BusinessWorkspaceRepository"/>) against an
/// unencrypted temp SQLite database (no mocks).
///
/// <para><b>Property 16: Invalid template imports are rejected without side effects.</b> For ANY JSON
/// payload that fails schema validation or references an undefined Universal_Domain_Model entity type,
/// the import is rejected with <see cref="TemplateImportOutcome.TemplateImportInvalid"/> and an error
/// identifying the specific validation failure, and all existing Business_Templates remain unchanged.</para>
///
/// <para>The generator produces invalid payloads spanning every rejection rule the service enforces
/// (mirroring the validation in <see cref="CommerceBusinessTemplateService.ImportAsync"/>):
/// malformed (non-parseable) JSON; a well-formed-but-non-object root; a missing/non-numeric/unsupported
/// <c>schemaVersion</c>; a missing/empty/non-string <c>templateKey</c>; a missing/empty/non-string
/// <c>displayName</c>; a non-object <c>config</c>; and a <c>config</c> that references an undefined
/// entity type via an <c>entityType</c>/<c>targetEntityType</c> property (including nested positions).
/// Every category is constructed to be genuinely invalid, so the property never asserts on a payload
/// the service would legitimately accept; a complementary control test confirms a valid payload still
/// imports, ruling out a service that rejects everything.</para>
///
/// <para>To prove the no-side-effects half, the database is seeded with the built-in template plus two
/// workspace-owned templates, a full snapshot (id, key, workspace, built-in flag, display name, config,
/// and both audit timestamps) is captured before the import, and the same snapshot is re-read and
/// compared field-for-field afterward. Temp database files are removed in a <c>finally</c> block.</para>
///
/// **Validates: Requirements 5.2**
/// </summary>
public sealed class InvalidTemplateImportPropertyTests
{
    private static readonly Gen<string> KeyOrNameGen =
        Gen.Char['a', 'z'].Array[1, 12].Select(chars => new string(chars));

    // --- Category 1: malformed (non-parseable) JSON. ---
    private static readonly Gen<string> MalformedJsonGen = Gen.OneOf(
        Gen.OneOfConst(
            "{",
            "}",
            "[",
            "[1,2",
            "{\"schemaVersion\":1",
            "{ \"k\": }",
            "{\"a\":1,}",
            "\"unterminated",
            "{}{}",
            "nul",
            "garbage {[",
            "{\"a\":1}trailing",
            "{,}"),
        Gen.String[0, 30].Where(s => !IsWellFormedJson(s)));

    // --- Category 2: well-formed JSON whose root is not an object. ---
    private static readonly Gen<string> NonObjectRootGen = Gen.OneOfConst(
        "[]",
        "[1,2,3]",
        "123",
        "-4.5",
        "\"hello\"",
        "true",
        "false",
        "null",
        "[{\"schemaVersion\":1}]");

    // --- Category 3: object root with a missing / non-numeric / unsupported schemaVersion. ---
    private static readonly Gen<string> BadSchemaVersionGen =
        from key in KeyOrNameGen
        from name in KeyOrNameGen
        from variant in Gen.Int[0, 4]
        select BuildBadSchemaVersion(variant, key, name);

    // --- Category 4: missing / empty / non-string templateKey. ---
    private static readonly Gen<string> MissingTemplateKeyGen =
        from name in KeyOrNameGen
        from variant in Gen.Int[0, 3]
        select BuildMissingTemplateKey(variant, name);

    // --- Category 5: missing / empty / non-string displayName. ---
    private static readonly Gen<string> MissingDisplayNameGen =
        from key in KeyOrNameGen
        from variant in Gen.Int[0, 3]
        select BuildMissingDisplayName(variant, key);

    // --- Category 6: a non-object (and non-null) config. ---
    private static readonly Gen<string> NonObjectConfigGen =
        from key in KeyOrNameGen
        from name in KeyOrNameGen
        from variant in Gen.Int[0, 3]
        select BuildNonObjectConfig(variant, key, name);

    // Entity-type names that are NOT defined members of BusinessEntityType (case-sensitive).
    private static readonly Gen<string> InvalidEntityTypeNameGen = Gen.OneOf(
        Gen.OneOfConst("Banana", "Unknown", "FooBar", "order", "customer", "客户", "ProductX"),
        Gen.Char['a', 'z'].Array[1, 10].Select(chars => "Undefined_" + new string(chars)));

    // --- Category 7: config references an undefined entity type. ---
    private static readonly Gen<string> UndefinedEntityTypeGen =
        from key in KeyOrNameGen
        from name in KeyOrNameGen
        from badType in InvalidEntityTypeNameGen
        from variant in Gen.Int[0, 3]
        select BuildUndefinedEntityType(variant, key, name, badType);

    // The full space of invalid payloads, tagged with their category for diagnostics on shrink.
    private static readonly Gen<(string Category, string Json)> InvalidPayloadGen = Gen.OneOf(
        MalformedJsonGen.Select(j => ("malformed-json", j)),
        NonObjectRootGen.Select(j => ("non-object-root", j)),
        BadSchemaVersionGen.Select(j => ("bad-schema-version", j)),
        MissingTemplateKeyGen.Select(j => ("missing-template-key", j)),
        MissingDisplayNameGen.Select(j => ("missing-display-name", j)),
        NonObjectConfigGen.Select(j => ("non-object-config", j)),
        UndefinedEntityTypeGen.Select(j => ("undefined-entity-type", j)));

    [Fact]
    public void Property16_invalid_template_imports_are_rejected_without_side_effects()
    {
        InvalidPayloadGen.Sample(
            testCase =>
            {
                (string category, string json) = testCase;

                WithTempDatabase(path =>
                {
                    IBusinessTemplateService service = NewService(path);
                    Guid workspaceId = Guid.NewGuid();
                    SeedTemplates(service, workspaceId);

                    IReadOnlyList<TemplateSnapshot> before = Snapshot(service);

                    TemplateImportResult result = service.ImportAsync(json, workspaceId).GetAwaiter().GetResult();

                    // (1) Rejected with the invalid outcome, a specific error, and no created template.
                    Assert.True(result.IsInvalid, $"[{category}] expected an invalid outcome for payload: {json}");
                    Assert.Equal(TemplateImportOutcome.TemplateImportInvalid, result.Outcome);
                    Assert.False(result.IsImported);
                    Assert.Null(result.Template);
                    Assert.False(
                        string.IsNullOrWhiteSpace(result.Error),
                        $"[{category}] expected a specific validation error for payload: {json}");

                    // (2) Every existing template is unchanged (count + all fields + audit timestamps).
                    IReadOnlyList<TemplateSnapshot> after = Snapshot(service);
                    AssertUnchanged(category, before, after);

                    return true;
                });
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples covering each rejection rule and a valid control. ---

    [Fact]
    public void Malformed_json_is_rejected_and_leaves_templates_unchanged()
        => AssertRejectedWithoutSideEffects("{\"schemaVersion\":1, not json");

    [Fact]
    public void Non_object_root_is_rejected()
        => AssertRejectedWithoutSideEffects("[1, 2, 3]");

    [Fact]
    public void Unsupported_schema_version_is_rejected()
        => AssertRejectedWithoutSideEffects(
            "{\"schemaVersion\":2,\"templateKey\":\"k\",\"displayName\":\"名称\",\"config\":{}}");

    [Fact]
    public void Missing_template_key_is_rejected()
        => AssertRejectedWithoutSideEffects(
            "{\"schemaVersion\":1,\"displayName\":\"名称\",\"config\":{}}");

    [Fact]
    public void Missing_display_name_is_rejected()
        => AssertRejectedWithoutSideEffects(
            "{\"schemaVersion\":1,\"templateKey\":\"k\",\"config\":{}}");

    [Fact]
    public void Non_object_config_is_rejected()
        => AssertRejectedWithoutSideEffects(
            "{\"schemaVersion\":1,\"templateKey\":\"k\",\"displayName\":\"名称\",\"config\":[1,2,3]}");

    [Fact]
    public void Undefined_entity_type_is_rejected_and_names_the_reference()
    {
        WithService((service, workspaceId) =>
        {
            IReadOnlyList<TemplateSnapshot> before = Snapshot(service);

            const string json =
                "{\"schemaVersion\":1,\"templateKey\":\"k\",\"displayName\":\"名称\"," +
                "\"config\":{\"pages\":{\"detail\":{\"entityType\":\"Banana\"}}}}";
            TemplateImportResult result = service.ImportAsync(json, workspaceId).GetAwaiter().GetResult();

            Assert.True(result.IsInvalid);
            Assert.Equal(TemplateImportOutcome.TemplateImportInvalid, result.Outcome);
            Assert.Contains("Banana", result.Error);
            AssertUnchanged("undefined-entity-type", before, Snapshot(service));
        });
    }

    [Fact]
    public void Valid_payload_still_imports_and_adds_one_template()
    {
        WithService((service, workspaceId) =>
        {
            int countBefore = Snapshot(service).Count;

            const string json =
                "{\"schemaVersion\":1,\"templateKey\":\"imported-key\",\"displayName\":\"导入模板\"," +
                "\"config\":{\"pages\":{\"order\":{\"entityType\":\"Order\"}}}}";
            TemplateImportResult result = service.ImportAsync(json, workspaceId).GetAwaiter().GetResult();

            Assert.True(result.IsImported);
            Assert.NotNull(result.Template);
            Assert.Equal("imported-key", result.Template!.TemplateKey);
            Assert.Equal("导入模板", result.Template.DisplayName);
            Assert.Equal(workspaceId, result.Template.WorkspaceId);
            Assert.Equal(countBefore + 1, Snapshot(service).Count);
        });
    }

    // --- JSON builders (each category is constructed to be genuinely invalid). ---

    private static string BuildBadSchemaVersion(int variant, string key, string name)
    {
        var doc = new JsonObject
        {
            ["templateKey"] = key,
            ["displayName"] = name,
            ["config"] = new JsonObject(),
        };
        switch (variant)
        {
            case 0: break;                         // schemaVersion absent
            case 1: doc["schemaVersion"] = "1"; break;  // non-numeric (string)
            case 2: doc["schemaVersion"] = 0; break;    // unsupported number
            case 3: doc["schemaVersion"] = 2; break;    // unsupported number
            default: doc["schemaVersion"] = true; break; // non-numeric (bool)
        }

        return doc.ToJsonString();
    }

    private static string BuildMissingTemplateKey(int variant, string name)
    {
        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["displayName"] = name,
            ["config"] = new JsonObject(),
        };
        switch (variant)
        {
            case 0: break;                       // templateKey absent
            case 1: doc["templateKey"] = ""; break;
            case 2: doc["templateKey"] = "   "; break;
            default: doc["templateKey"] = 123; break; // non-string
        }

        return doc.ToJsonString();
    }

    private static string BuildMissingDisplayName(int variant, string key)
    {
        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["templateKey"] = key,
            ["config"] = new JsonObject(),
        };
        switch (variant)
        {
            case 0: break;                       // displayName absent
            case 1: doc["displayName"] = ""; break;
            case 2: doc["displayName"] = "   "; break;
            default: doc["displayName"] = 456; break; // non-string
        }

        return doc.ToJsonString();
    }

    private static string BuildNonObjectConfig(int variant, string key, string name)
    {
        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["templateKey"] = key,
            ["displayName"] = name,
        };
        switch (variant)
        {
            case 0: doc["config"] = new JsonArray(1, 2, 3); break;
            case 1: doc["config"] = 42; break;
            case 2: doc["config"] = "not-an-object"; break;
            default: doc["config"] = false; break;
        }

        return doc.ToJsonString();
    }

    private static string BuildUndefinedEntityType(int variant, string key, string name, string badType)
    {
        JsonObject config = variant switch
        {
            0 => new JsonObject { ["entityType"] = badType },
            1 => new JsonObject { ["targetEntityType"] = badType },
            2 => new JsonObject { ["fields"] = new JsonArray(new JsonObject { ["entityType"] = badType }) },
            _ => new JsonObject
            {
                ["pages"] = new JsonObject { ["detail"] = new JsonObject { ["targetEntityType"] = badType } },
            },
        };

        var doc = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["templateKey"] = key,
            ["displayName"] = name,
            ["config"] = config,
        };

        return doc.ToJsonString();
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

    private sealed record TemplateSnapshot(
        Guid Id,
        string TemplateKey,
        Guid? WorkspaceId,
        bool IsBuiltIn,
        string DisplayName,
        string? ConfigJson,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private static IBusinessTemplateService NewService(string databasePath)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();
        return new CommerceBusinessTemplateService(
            new BusinessTemplateRepository(factory),
            new BusinessWorkspaceRepository(factory));
    }

    private static void SeedTemplates(IBusinessTemplateService service, Guid workspaceId)
    {
        service.GetOrCreateBuiltInTemplateAsync().GetAwaiter().GetResult();
        service.CreateAsync(
            "workspace-template-1",
            "工作模板一",
            "{\"pages\":{\"order\":{\"entityType\":\"Order\"}}}",
            workspaceId).GetAwaiter().GetResult();
        service.CreateAsync(
            "workspace-template-2",
            "工作模板二",
            null,
            workspaceId).GetAwaiter().GetResult();
    }

    private static IReadOnlyList<TemplateSnapshot> Snapshot(IBusinessTemplateService service)
        => service.GetAllAsync().GetAwaiter().GetResult()
            .Select(t => new TemplateSnapshot(
                t.Id, t.TemplateKey, t.WorkspaceId, t.IsBuiltIn, t.DisplayName, t.ConfigJson, t.CreatedAt, t.UpdatedAt))
            .OrderBy(s => s.Id)
            .ToList();

    private static void AssertUnchanged(
        string category,
        IReadOnlyList<TemplateSnapshot> before,
        IReadOnlyList<TemplateSnapshot> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i], after[i]);
        }
    }

    private static void AssertRejectedWithoutSideEffects(string json)
    {
        WithService((service, workspaceId) =>
        {
            IReadOnlyList<TemplateSnapshot> before = Snapshot(service);

            TemplateImportResult result = service.ImportAsync(json, workspaceId).GetAwaiter().GetResult();

            Assert.True(result.IsInvalid);
            Assert.Equal(TemplateImportOutcome.TemplateImportInvalid, result.Outcome);
            Assert.Null(result.Template);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));

            AssertUnchanged("unit", before, Snapshot(service));
        });
    }

    private static void WithService(Action<IBusinessTemplateService, Guid> body)
    {
        WithTempDatabase(path =>
        {
            IBusinessTemplateService service = NewService(path);
            Guid workspaceId = Guid.NewGuid();
            SeedTemplates(service, workspaceId);
            body(service, workspaceId);
            return true;
        });
    }

    /// <summary>
    /// Creates a unique temp database file path, runs <paramref name="action"/>, then removes the temp
    /// file (and clears the SQLite connection pool) in a finally block so no temp artifacts leak.
    /// </summary>
    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"orderly-invalid-template-import-{Guid.NewGuid():N}.db");

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
