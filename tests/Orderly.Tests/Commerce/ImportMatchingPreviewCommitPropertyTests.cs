using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based test for the deterministic import matching, accurate preview counts, and idempotent
/// commit guarantees of <see cref="CommerceImportExportService"/> (Req 9.4–9.7).
///
/// <para><b>Property 18: Import matching is deterministic, preview counts are accurate, and commit is
/// idempotent.</b> For any generated dataset mixing new, existing-matching, ambiguous, and invalid
/// rows, the preview classifies each row as exactly one of Add / Update / Error / Conflict using the
/// deterministic per-entity match keys (here exercised against <see cref="Product"/>, whose key is
/// <c>Code</c> → <c>Name</c>), an ambiguous fallback match is classified as Conflict and never
/// silently updated, the reported Add/Update/Error/Conflict counts equal the true partition of the
/// dataset, committing applies only Add and Update rows while leaving pre-existing unreferenced
/// records unchanged, and re-importing the same file commits no duplicate records (idempotent
/// commit).</para>
///
/// <para>The property is exercised end-to-end against the real SQLCipher-backed Commerce repositories
/// (an unencrypted temp database, no mocks). Each generated case runs against its own freshly
/// initialized database so the asserted record counts are absolute.</para>
///
/// **Validates: Requirements 9.4, 9.5, 9.6, 9.7**
/// </summary>
public sealed class ImportMatchingPreviewCommitPropertyTests
{
    /// <summary>
    /// One generated import scenario. Each field bounds how many existing records and how many
    /// import rows of each intended classification the case builds.
    /// </summary>
    private readonly record struct Scenario(
        int ExistingUnique,
        int DupNameGroups,
        int AddRows,
        int UpdateRows,
        int ConflictRows,
        int ErrorRows);

    private static readonly Gen<Scenario> ScenarioGen =
        Gen.Select(
            Gen.Int[0, 6],  // existing records with a unique Code+Name (Update targets)
            Gen.Int[0, 3],  // groups of two existing records sharing a Name (Conflict targets)
            Gen.Int[0, 6],  // Add rows (new, unique Code)
            Gen.Int[0, 6],  // Update rows (reference an existing unique Code)
            Gen.Int[0, 5],  // Conflict rows (blank Code, Name matching a duplicate-name group)
            Gen.Int[0, 4],  // Error rows (blank Name -> validation error)
            (existing, dup, add, update, conflict, error) =>
                new Scenario(existing, dup, add, update, conflict, error));

    [Fact]
    public void Property18_import_matching_is_deterministic_preview_is_accurate_and_commit_is_idempotent()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                string path = Path.Combine(Path.GetTempPath(), $"orderly-import-{Guid.NewGuid():N}.db");
                try
                {
                    RunScenario(path, scenario);
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
                            // Best-effort cleanup of temp files.
                        }
                    }
                }
            },
            iter: PbtConfig.MinIterations);
    }

    private static void RunScenario(string path, Scenario scenario)
    {
        var factory = new SqliteConnectionFactory(path);
        new CommerceSchemaInitializer(factory).InitializeAsync().GetAwaiter().GetResult();

        var products = new ProductRepository(factory);
        var service = new CommerceImportExportService(
            factory,
            products,
            new InventoryItemRepository(factory),
            new CommerceCustomerRepository(factory),
            new CommerceOrderRepository(factory),
            new CashFlowEntryRepository(factory));

        Guid workspaceId = Guid.NewGuid();
        string workspaceText = workspaceId.ToString();

        // ----- Seed the pre-existing records -----
        // Unique-keyed records are the Update targets (matched on their unique Code).
        for (int i = 0; i < scenario.ExistingUnique; i++)
        {
            products.CreateAsync(new Product
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Code = $"EXIST-{i}",
                Name = $"existname-{i}",
            }).GetAwaiter().GetResult();
        }

        // Duplicate-name groups: two records sharing a Name so a blank-Code row that falls back to
        // Name matches more than one record and must be classified as a Conflict (Req 9.4).
        for (int g = 0; g < scenario.DupNameGroups; g++)
        {
            foreach (string suffix in new[] { "a", "b" })
            {
                products.CreateAsync(new Product
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    Code = $"DUP-{g}-{suffix}",
                    Name = $"dupname-{g}",
                }).GetAwaiter().GetResult();
            }
        }

        // Update / Conflict rows only realize their intended class when their target exists.
        int expectedAdd = scenario.AddRows;
        int expectedUpdate = scenario.ExistingUnique > 0 ? scenario.UpdateRows : 0;
        int expectedConflict = scenario.DupNameGroups > 0 ? scenario.ConflictRows : 0;
        int expectedError = scenario.ErrorRows;

        // ----- Build the import rows tagged with their intended classification -----
        var rows = new List<(string Ws, string Code, string Name, ImportRowClassification Expected)>();

        for (int k = 0; k < expectedAdd; k++)
        {
            // New, unique Code -> matches nothing -> Add. WorkspaceId is required to build a new entity.
            rows.Add((workspaceText, $"NEW-{k}", $"newname-{k}", ImportRowClassification.Add));
        }

        for (int j = 0; j < expectedUpdate; j++)
        {
            int idx = j % scenario.ExistingUnique;
            // Existing unique Code -> matches exactly one record -> Update (Name left identical).
            rows.Add((string.Empty, $"EXIST-{idx}", $"existname-{idx}", ImportRowClassification.Update));
        }

        for (int j = 0; j < expectedConflict; j++)
        {
            int g = j % scenario.DupNameGroups;
            // Blank Code falls back to Name, which matches two records -> ambiguous -> Conflict.
            rows.Add((string.Empty, string.Empty, $"dupname-{g}", ImportRowClassification.Conflict));
        }

        for (int j = 0; j < expectedError; j++)
        {
            // Blank Name fails validation -> Error (never written), regardless of any Code.
            rows.Add((workspaceText, $"ERR-{j}", string.Empty, ImportRowClassification.Error));
        }

        byte[] content = BuildCsv(rows);

        // ----- Preview: deterministic classification + accurate counts (Req 9.4) -----
        ImportPreview preview = service
            .PreviewImportAsync(ImportExportDataType.Product, ImportExportFormat.Csv, content)
            .GetAwaiter().GetResult();

        Assert.False(preview.IsRejected);
        Assert.Equal(expectedAdd, preview.AddCount);
        Assert.Equal(expectedUpdate, preview.UpdateCount);
        Assert.Equal(expectedConflict, preview.ConflictCount);
        Assert.Equal(expectedError, preview.ErrorCount);

        // The four counts partition the dataset exactly (every row classified as exactly one class).
        Assert.Equal(rows.Count, preview.Rows.Count);
        Assert.Equal(
            rows.Count,
            preview.AddCount + preview.UpdateCount + preview.ConflictCount + preview.ErrorCount);

        // Each row's classification matches its intended class, in file order.
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.Equal(rows[i].Expected, preview.Rows[i].Classification);
        }

        // Conflicts surface ImportRowConflict; otherwise the preview is Success.
        Assert.Equal(
            expectedConflict > 0 ? ImportResultCode.ImportRowConflict : ImportResultCode.Success,
            preview.Code);

        // Determinism: previewing the identical bytes again yields the identical classification sequence.
        ImportPreview previewAgain = service
            .PreviewImportAsync(ImportExportDataType.Product, ImportExportFormat.Csv, content)
            .GetAwaiter().GetResult();
        Assert.Equal(preview.AddCount, previewAgain.AddCount);
        Assert.Equal(preview.UpdateCount, previewAgain.UpdateCount);
        Assert.Equal(preview.ConflictCount, previewAgain.ConflictCount);
        Assert.Equal(preview.ErrorCount, previewAgain.ErrorCount);
        Assert.Equal(
            preview.Rows.Select(r => r.Classification),
            previewAgain.Rows.Select(r => r.Classification));

        // ----- Commit: only Add/Update applied; unreferenced records unchanged (Req 9.5, 9.6) -----
        int preCommitCount = scenario.ExistingUnique + (scenario.DupNameGroups * 2);
        Assert.Equal(preCommitCount, products.GetAllAsync().GetAwaiter().GetResult().Count);

        ImportCommitResult commit = service.CommitImportAsync(preview).GetAwaiter().GetResult();

        Assert.True(commit.Committed);
        Assert.Equal(ImportResultCode.Success, commit.Code);
        Assert.Equal(expectedAdd, commit.AddedCount);
        Assert.Equal(expectedUpdate, commit.UpdatedCount);
        Assert.Empty(commit.Failures);

        // Only the Add rows added records; Update rows mutate in place; Error/Conflict rows write nothing.
        int afterCommitCount = products.GetAllAsync().GetAwaiter().GetResult().Count;
        Assert.Equal(preCommitCount + expectedAdd, afterCommitCount);

        // Pre-existing duplicate-name records are never referenced by Add/Update and remain unchanged.
        int dupRemaining = products.GetAllAsync().GetAwaiter().GetResult()
            .Count(p => p.Name.StartsWith("dupname-", StringComparison.Ordinal));
        Assert.Equal(scenario.DupNameGroups * 2, dupRemaining);

        // ----- Idempotent commit: re-importing the same file creates no duplicates (Req 9.5) -----
        ImportPreview rePreview = service
            .PreviewImportAsync(ImportExportDataType.Product, ImportExportFormat.Csv, content)
            .GetAwaiter().GetResult();

        // Because matching is deterministic, every row previously added now resolves to Update.
        Assert.Equal(0, rePreview.AddCount);
        Assert.Equal(expectedAdd + expectedUpdate, rePreview.UpdateCount);
        Assert.Equal(expectedConflict, rePreview.ConflictCount);
        Assert.Equal(expectedError, rePreview.ErrorCount);

        ImportCommitResult reCommit = service.CommitImportAsync(rePreview).GetAwaiter().GetResult();

        Assert.True(reCommit.Committed);
        Assert.Equal(0, reCommit.AddedCount);
        Assert.Equal(expectedAdd + expectedUpdate, reCommit.UpdatedCount);

        // The decisive idempotency assertion: re-import produced no new records.
        int finalCount = products.GetAllAsync().GetAwaiter().GetResult().Count;
        Assert.Equal(afterCommitCount, finalCount);
    }

    /// <summary>
    /// Builds a UTF-8 CSV file with a <c>WorkspaceId,Code,Name</c> header and one line per row. All
    /// generated values are simple identifiers free of CSV metacharacters, so plain joining is safe.
    /// </summary>
    private static byte[] BuildCsv(IReadOnlyList<(string Ws, string Code, string Name, ImportRowClassification Expected)> rows)
    {
        var builder = new StringBuilder();
        builder.Append("WorkspaceId,Code,Name\r\n");
        foreach ((string ws, string code, string name, _) in rows)
        {
            builder.Append(ws).Append(',').Append(code).Append(',').Append(name).Append("\r\n");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
    }
}
