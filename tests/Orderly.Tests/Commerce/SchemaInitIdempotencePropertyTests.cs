using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.Data.Sqlite;
using Orderly.Data.Sqlite;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for the idempotence of the Commerce schema-initialization routine
/// (<see cref="CommerceSchemaInitializer"/>).
///
/// Property 5: Schema initialization is idempotent.
/// For ANY repeat count N ≥ 1, running the Commerce schema-initialization routine N times against
/// the same database leaves the schema in a final state IDENTICAL to running it once, and raises
/// no error.
///
/// The test runs init once against one fresh database and N times against a second fresh database,
/// captures a canonical schema snapshot from each (the <c>sqlite_master</c> type/name/sql for every
/// <c>Commerce%</c> object plus the full <c>PRAGMA table_info</c> column layout of every Commerce
/// table, ordered deterministically), and asserts the two snapshots are byte-for-byte equal. Both
/// databases use an UNENCRYPTED <see cref="SqliteConnectionFactory"/> against a temp file so no
/// SQLCipher key setup is required. Temp database files are removed in a <c>finally</c> block.
///
/// **Validates: Requirements 3.3**
/// </summary>
public class SchemaInitIdempotencePropertyTests
{
    /// <summary>The 18 Commerce tables the routine must create (used to assert completeness).</summary>
    private static readonly string[] ExpectedCommerceTables =
    {
        "CommerceBusinessWorkspaces",
        "CommerceBusinessTemplates",
        "CommerceCustomFieldDefinitions",
        "CommerceUnitDefinitions",
        "CommerceProducts",
        "CommerceProductVariants",
        "CommerceInventoryItems",
        "CommerceInventoryMovements",
        "CommerceCustomers",
        "CommerceCustomerContacts",
        "CommerceOrders",
        "CommerceOrderItems",
        "CommercePaymentRecords",
        "CommerceCashFlowEntries",
        "CommerceSuppliers",
        "CommerceBusinessTasks",
        "CommerceBusinessInsights",
        "CommerceBusinessMetricSnapshots",
    };

    // Repeat count N in [1, 8]: kept small so each iteration (which creates two temp databases and
    // runs init up to N times) stays fast across the 100-iteration minimum.
    private static readonly Gen<int> RepeatCountGen = Gen.Int[1, 8];

    [Fact]
    public void Property5_schema_initialization_is_idempotent()
    {
        RepeatCountGen.Sample(
            repeatCount =>
            {
                // Run init exactly once against a fresh database.
                string onceSnapshot = WithTempDatabase(path =>
                {
                    RunInit(path, times: 1);
                    return CaptureSchemaSnapshot(path);
                });

                // Run init N times against a separate fresh database.
                string repeatedSnapshot = WithTempDatabase(path =>
                {
                    RunInit(path, times: repeatCount);
                    return CaptureSchemaSnapshot(path);
                });

                // Running N times leaves an identical final schema to running once.
                Assert.Equal(onceSnapshot, repeatedSnapshot);

                // Sanity: the snapshot actually contains every expected Commerce table, so an
                // empty/no-op snapshot cannot make the equality trivially pass.
                foreach (string table in ExpectedCommerceTables)
                {
                    Assert.Contains("table|" + table + "|", repeatedSnapshot, StringComparison.Ordinal);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Initializing_once_creates_all_eighteen_commerce_tables()
    {
        WithTempDatabase(path =>
        {
            RunInit(path, times: 1);
            IReadOnlyList<string> tables = CaptureTableNames(path);

            Assert.Equal(ExpectedCommerceTables.Length, tables.Count);
            foreach (string expected in ExpectedCommerceTables)
            {
                Assert.Contains(expected, tables);
            }

            return true;
        });
    }

    [Fact]
    public void Initializing_repeatedly_does_not_throw_and_matches_single_run()
    {
        string once = WithTempDatabase(path =>
        {
            RunInit(path, times: 1);
            return CaptureSchemaSnapshot(path);
        });

        string fiveTimes = WithTempDatabase(path =>
        {
            RunInit(path, times: 5);
            return CaptureSchemaSnapshot(path);
        });

        Assert.Equal(once, fiveTimes);
    }

    // --- Helpers ---

    /// <summary>
    /// Runs the Commerce schema initializer <paramref name="times"/> times against the database at
    /// <paramref name="databasePath"/> using an unencrypted connection factory. Each invocation opens
    /// its own connection (matching production usage), so this exercises the repeated-run path.
    /// </summary>
    private static void RunInit(string databasePath, int times)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        var initializer = new CommerceSchemaInitializer(factory);

        for (int i = 0; i < times; i++)
        {
            // Synchronously wait so any exception surfaces directly as a test failure.
            initializer.InitializeAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Builds a deterministic, canonical text snapshot of the Commerce schema: every
    /// <c>sqlite_master</c> object whose name starts with <c>Commerce</c> (type/name/sql), followed
    /// by the ordered <c>PRAGMA table_info</c> column layout of every Commerce table. Ordering is
    /// fixed so the snapshot depends only on schema content, not on row/insertion order.
    /// </summary>
    private static string CaptureSchemaSnapshot(string databasePath)
    {
        var builder = new StringBuilder();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();

        // 1) sqlite_master entries for all Commerce* objects (tables and any indexes), ordered.
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT type, name, COALESCE(sql, '') FROM sqlite_master " +
                "WHERE name LIKE 'Commerce%' ORDER BY type, name;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string type = reader.GetString(0);
                string name = reader.GetString(1);
                string sql = reader.GetString(2);

                // Normalize whitespace in the stored CREATE statement so trivially-different
                // formatting never matters; the column-level snapshot below is authoritative.
                string normalizedSql = NormalizeWhitespace(sql);
                builder.Append(type).Append('|').Append(name).Append('|').Append(normalizedSql).Append('\n');
            }
        }

        // 2) Column layout for every Commerce table, ordered by table then column id.
        foreach (string table in CaptureTableNames(databasePath, connection))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int cid = reader.GetInt32(0);
                string columnName = reader.GetString(1);
                string columnType = reader.GetString(2);
                bool notNull = reader.GetInt32(3) != 0;
                string defaultValue = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                int primaryKey = reader.GetInt32(5);

                builder.Append("col|").Append(table).Append('|')
                    .Append(cid).Append('|')
                    .Append(columnName).Append('|')
                    .Append(columnType).Append('|')
                    .Append(notNull ? "NN" : "NULL").Append('|')
                    .Append(defaultValue).Append('|')
                    .Append("pk").Append(primaryKey).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> CaptureTableNames(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        return CaptureTableNames(databasePath, connection);
    }

    private static IReadOnlyList<string> CaptureTableNames(string databasePath, SqliteConnection connection)
    {
        var tables = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'Commerce%' ORDER BY name;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool previousWasSpace = false;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(c);
                previousWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Creates a unique temp database file path, runs <paramref name="action"/>, then removes the
    /// temp file (and the SQLite connection pool) in a finally block so no temp artifacts leak.
    /// </summary>
    private static T WithTempDatabase<T>(Func<string, T> action)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"orderly-schema-idempotence-{Guid.NewGuid():N}.db");

        try
        {
            return action(path);
        }
        finally
        {
            // Release any pooled connections to the file before deleting it.
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
