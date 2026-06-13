using System;
using System.Text.Json.Nodes;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Templates;

/// <summary>
/// Property-based tests for the JSON import/export round-trip of
/// <see cref="CommerceBusinessTemplateService"/> (Req 5.1).
///
/// Property 15: Business template JSON round-trip preserves the template.
/// For ANY valid Business_Template, exporting it to JSON and importing the result produces an
/// equivalent template, preserving its template key, display name, and configuration (page
/// configuration, workflow configuration, and custom-field definitions carried in
/// <see cref="BusinessTemplate.ConfigJson"/>).
///
/// The test generates templates with a non-empty key/display name and a configuration that is
/// either absent (<c>null</c>) or a structured JSON object containing page config, workflow config,
/// and custom-field definitions (the latter referencing only defined
/// <see cref="BusinessEntityType"/> names, so a well-formed payload is never rejected). Each
/// iteration exports the template, imports the produced JSON through the real Commerce repository
/// (SQLCipher-backed, against an unencrypted temp database — no mocks), and asserts the imported
/// template is equivalent to the source: the key and display name match exactly, the configuration
/// is JSON-equivalent, and re-exporting the imported template reproduces the exact same document
/// (a stable round-trip). Temp database files are removed in a <c>finally</c> block.
///
/// **Validates: Requirements 5.1**
/// </summary>
public sealed class TemplateJsonRoundTripPropertyTests
{
    /// <summary>
    /// Mirrors the structured configuration a real template carries; serialized with the default
    /// serializer so the generated JSON is well-formed input for the service's import/export.
    /// </summary>

    // --- Generators ---

    // Lowercase token, length 1..8, used for stage names and custom-field keys.
    private static readonly Gen<string> TokenGen =
        Gen.Char['a', 'z'].Array[1, 8].Select(chars => new string(chars));

    // Non-empty template key: lowercase letters and digits (never whitespace -> always valid, Req 5.2).
    private static readonly Gen<string> TemplateKeyGen =
        Gen.OneOf(Gen.Char['a', 'z'], Gen.Char['0', '9']).Array[1, 12].Select(chars => new string(chars));

    // Non-empty display name mixing ASCII letters and representative Simplified Chinese characters (Req 5.8).
    private static readonly Gen<string> DisplayNameGen =
        Gen.OneOf(
                Gen.Char['a', 'z'],
                Gen.Char['A', 'Z'],
                Gen.OneOfConst('默', '认', '经', '营', '模', '板', '客', '户', '商', '品'))
            .Array[1, 12]
            .Select(chars => new string(chars));

    // Only defined Universal_Domain_Model entity-type names, so import never rejects on an undefined type.
    private static readonly Gen<string> EntityTypeGen = Gen.OneOfConst(Enum.GetNames<BusinessEntityType>());

    private static readonly Gen<string> CurrencyGen = Gen.OneOfConst("CNY", "USD", "EUR", "JPY");
    private static readonly Gen<string> DataTypeGen = Gen.OneOfConst("Text", "Number", "Date", "Boolean");

    // A single custom-field definition entry: (entity type, key, label, data type).
    private static readonly Gen<FieldSpec> FieldGen =
        Gen.Select(EntityTypeGen, TokenGen, DisplayNameGen, DataTypeGen,
            (entityType, key, label, dataType) => new FieldSpec(entityType, key, label, dataType));

    // A structured template configuration: page config + workflow config + custom-field definitions.
    private static readonly Gen<JsonObject> ConfigObjectGen =
        from currency in CurrencyGen
        from cards in Gen.Bool.Array[0, 4]
        from stages in TokenGen.Array[0, 3]
        from fields in FieldGen.Array[0, 3]
        select BuildConfig(currency, cards, stages, fields);

    // Configuration JSON string, with null injected on a fraction of cases (a template with no config).
    private static readonly Gen<string?> ConfigJsonGen =
        ConfigObjectGen.Select(config => config.ToJsonString()).Null();

    private static readonly Gen<TemplateSpec> TemplateSpecGen =
        Gen.Select(TemplateKeyGen, DisplayNameGen, ConfigJsonGen,
            (key, name, config) => new TemplateSpec(key, name, config));

    [Fact]
    public void Property15_template_json_round_trip_preserves_the_template()
    {
        TemplateSpecGen.Sample(
            spec =>
            {
                BusinessTemplate source = NewTemplate(spec);
                (BusinessTemplate imported, string exported) = ExportThenImport(source);

                // The round-trip preserves the template's identity and display name.
                Assert.Equal(source.TemplateKey, imported.TemplateKey);
                Assert.Equal(source.DisplayName, imported.DisplayName);

                // The round-trip preserves the configuration (page/workflow/custom-field definitions).
                AssertConfigEquivalent(source.ConfigJson, imported.ConfigJson);

                // Export is stable: re-exporting the imported template reproduces the same document.
                Assert.Equal(exported, Export(imported));
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Round_trip_with_null_config_preserves_key_and_name_and_yields_null_config()
    {
        var source = new BusinessTemplate
        {
            TemplateKey = "minimal",
            WorkspaceId = Guid.NewGuid(),
            IsBuiltIn = false,
            DisplayName = "极简模板",
            ConfigJson = null
        };

        (BusinessTemplate imported, _) = ExportThenImport(source);

        Assert.Equal("minimal", imported.TemplateKey);
        Assert.Equal("极简模板", imported.DisplayName);
        Assert.Null(imported.ConfigJson);
    }

    [Fact]
    public void Round_trip_preserves_config_with_valid_entity_type_references()
    {
        string config = BuildConfig(
            currency: "CNY",
            cards: new[] { true, false, true },
            stages: new[] { "draft", "quoted", "won" },
            fields: new[]
            {
                new FieldSpec(nameof(BusinessEntityType.Customer), "level", "客户等级", "Text"),
                new FieldSpec(nameof(BusinessEntityType.Order), "channel", "销售渠道", "Text"),
            }).ToJsonString();

        var source = new BusinessTemplate
        {
            TemplateKey = "retail",
            WorkspaceId = Guid.NewGuid(),
            IsBuiltIn = false,
            DisplayName = "零售模板",
            ConfigJson = config
        };

        (BusinessTemplate imported, string exported) = ExportThenImport(source);

        Assert.Equal("retail", imported.TemplateKey);
        Assert.Equal("零售模板", imported.DisplayName);
        AssertConfigEquivalent(config, imported.ConfigJson);
        Assert.Equal(exported, Export(imported));
    }

    [Fact]
    public void Round_trip_of_built_in_like_template_preserves_chinese_display_name()
    {
        var source = new BusinessTemplate
        {
            TemplateKey = BuiltInBusinessTemplate.Key,
            WorkspaceId = Guid.NewGuid(),
            IsBuiltIn = false,
            DisplayName = BuiltInBusinessTemplate.DisplayName,
            ConfigJson = "{\"pageConfig\":{\"currency\":\"CNY\"}}"
        };

        (BusinessTemplate imported, _) = ExportThenImport(source);

        Assert.Equal(BuiltInBusinessTemplate.Key, imported.TemplateKey);
        Assert.Equal(BuiltInBusinessTemplate.DisplayName, imported.DisplayName);
        AssertConfigEquivalent(source.ConfigJson, imported.ConfigJson);
    }

    [Fact]
    public void Re_exporting_imported_template_is_stable()
    {
        var source = new BusinessTemplate
        {
            TemplateKey = "wholesale",
            WorkspaceId = Guid.NewGuid(),
            IsBuiltIn = false,
            DisplayName = "批发模板",
            ConfigJson = BuildConfig(
                currency: "USD",
                cards: new[] { false },
                stages: new[] { "open" },
                fields: new[] { new FieldSpec(nameof(BusinessEntityType.Supplier), "tier", "供应商分层", "Number") })
                .ToJsonString()
        };

        (BusinessTemplate imported, string exported) = ExportThenImport(source);

        Assert.Equal(exported, Export(imported));
    }

    // --- Helpers ---

    private static BusinessTemplate NewTemplate(TemplateSpec spec) => new()
    {
        TemplateKey = spec.TemplateKey,
        WorkspaceId = Guid.NewGuid(),
        IsBuiltIn = false,
        DisplayName = spec.DisplayName,
        ConfigJson = spec.ConfigJson
    };

    /// <summary>
    /// Exports <paramref name="source"/> to JSON and imports the produced document through the real
    /// Commerce template service over a fresh, unencrypted temp database. Returns the imported
    /// template and the exact exported JSON document.
    /// </summary>
    private static (BusinessTemplate Imported, string Exported) ExportThenImport(BusinessTemplate source)
    {
        return WithTempDatabase(path =>
        {
            var factory = new SqliteConnectionFactory(path);
            new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

            var service = new CommerceBusinessTemplateService(
                new BusinessTemplateRepository(factory),
                new BusinessWorkspaceRepository(factory));

            string exported = service.Export(source);

            TemplateImportResult result = service
                .ImportAsync(exported, Guid.NewGuid())
                .GetAwaiter()
                .GetResult();

            Assert.True(result.IsImported, result.Error ?? "import was unexpectedly rejected");
            return (result.Template!, exported);
        });
    }

    /// <summary>Serializes a template via a throw-away service instance (no persistence required).</summary>
    private static string Export(BusinessTemplate template)
        => new CommerceBusinessTemplateService(
                new InMemoryTemplateRepository(),
                new InMemoryWorkspaceRepository())
            .Export(template);

    private static void AssertConfigEquivalent(string? expected, string? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.True(
                expected is null && actual is null,
                $"Config nullness diverged: expected-null={expected is null}, actual-null={actual is null}.");
            return;
        }

        JsonNode? expectedNode = JsonNode.Parse(expected);
        JsonNode? actualNode = JsonNode.Parse(actual);
        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Config JSON diverged.\nExpected: {expected}\nActual:   {actual}");
    }

    private static JsonObject BuildConfig(string currency, bool[] cards, string[] stages, FieldSpec[] fields)
    {
        var cardArray = new JsonArray();
        foreach (bool card in cards)
        {
            cardArray.Add(card);
        }

        var pageConfig = new JsonObject
        {
            ["currency"] = currency,
            ["metricCards"] = cardArray
        };

        var stageArray = new JsonArray();
        foreach (string stage in stages)
        {
            stageArray.Add(stage);
        }

        var workflowConfig = new JsonObject
        {
            ["salesStages"] = stageArray
        };

        var fieldArray = new JsonArray();
        foreach (FieldSpec field in fields)
        {
            fieldArray.Add(new JsonObject
            {
                ["entityType"] = field.EntityType,
                ["key"] = field.Key,
                ["label"] = field.Label,
                ["dataType"] = field.DataType
            });
        }

        return new JsonObject
        {
            ["pageConfig"] = pageConfig,
            ["workflowConfig"] = workflowConfig,
            ["customFieldDefinitions"] = fieldArray
        };
    }

    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"orderly-template-roundtrip-{Guid.NewGuid():N}.db");

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
                    if (System.IO.File.Exists(file))
                    {
                        System.IO.File.Delete(file);
                    }
                }
                catch (System.IO.IOException)
                {
                    // Best-effort cleanup of temp files.
                }
            }
        }
    }

    private sealed record TemplateSpec(string TemplateKey, string DisplayName, string? ConfigJson);

    private sealed record FieldSpec(string EntityType, string Key, string Label, string DataType);

    // Minimal in-memory repositories used only by the persistence-free Export helper above.
    private sealed class InMemoryTemplateRepository : IBusinessTemplateRepository
    {
        public Task<BusinessTemplate> CreateAsync(BusinessTemplate entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public Task<BusinessTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessTemplate?>(null);

        public Task<IReadOnlyList<BusinessTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessTemplate>>(Array.Empty<BusinessTemplate>());

        public Task<BusinessTemplate?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessTemplate?>(null);

        public Task<IReadOnlyList<BusinessTemplate>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessTemplate>>(Array.Empty<BusinessTemplate>());

        public Task UpdateAsync(BusinessTemplate entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryWorkspaceRepository : IBusinessWorkspaceRepository
    {
        public Task<BusinessWorkspace> CreateAsync(BusinessWorkspace entity, CancellationToken cancellationToken = default)
            => Task.FromResult(entity);

        public Task<BusinessWorkspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessWorkspace?>(null);

        public Task<IReadOnlyList<BusinessWorkspace>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessWorkspace>>(Array.Empty<BusinessWorkspace>());

        public Task<BusinessWorkspace?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<BusinessWorkspace?>(null);

        public Task<IReadOnlyList<BusinessWorkspace>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessWorkspace>>(Array.Empty<BusinessWorkspace>());

        public Task UpdateAsync(BusinessWorkspace entity, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
