using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

/// <summary>
/// Idempotent schema initialization for the Universal_Domain_Model (Commerce) entities
/// (Requirements 3.2, 3.3). One SQLite/SQLCipher table is created per Commerce entity inside the
/// per-workspace encrypted database under <c>%LocalAppData%\Orderly</c>.
///
/// <para>
/// This routine is intentionally SEPARATE from <see cref="DatabaseInitializer"/> so the legacy
/// CRM tables (Customers/Deals/Orders/FollowUps/…), the launcher database, and the multi-account
/// structure of the P0_Security_System are left completely unchanged (Requirement 1.5, C-2). No
/// legacy table is created, altered, or dropped here.
/// </para>
///
/// <para><b>Table-naming scheme.</b> Every Commerce table is named <c>Commerce</c> + the entity's
/// plural noun (for example <c>CommerceCustomers</c>, <c>CommerceOrders</c>). The <c>Commerce</c>
/// prefix guarantees the new tables never collide with the legacy <c>Customers</c>/<c>Orders</c>
/// tables in the same database.</para>
///
/// <para><b>Money / decimal storage.</b> Monetary values (CommerceMoney) and other exact decimal
/// quantities are stored as <c>TEXT</c> using an invariant-culture round-trip rather than
/// <c>REAL</c>, so the scale-2 decimal exactness required by CommerceMoney (Requirement 2.6) and
/// the precision of inventory/quantity decimals are preserved without binary floating-point drift.
/// Identities (<c>Guid</c>) are stored as <c>TEXT</c>; UTC timestamps as <c>TEXT</c> via the round-trip
/// ("O") format; enums and booleans as <c>INTEGER</c>; <c>CustomFieldsJson</c> as nullable <c>TEXT</c>.</para>
///
/// <para><b>Idempotence (Requirement 3.3 / Property 5).</b> Each table is created with
/// <c>CREATE TABLE IF NOT EXISTS</c> and every declared column is additionally ensured via an
/// <c>EnsureColumnAsync</c> / <c>PRAGMA table_info</c> check, mirroring
/// <see cref="DatabaseInitializer"/>. Running this routine any number of times leaves the schema in
/// an identical final state and raises no error.</para>
/// </summary>
public sealed partial class CommerceSchemaInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public CommerceSchemaInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Opens a connection from the factory (applying the SQLCipher key on open) and initializes the
    /// Commerce schema idempotently.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await InitializeSchemaAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Initializes the Commerce schema against an already-open connection. Used both by the
    /// workspace database initialization flow and directly by tests so the same routine can run
    /// repeatedly against a single connection.
    /// </summary>
    public static async Task InitializeSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        // Use write-ahead logging for the per-workspace Commerce database so a Core_Write_Transaction
        // (Req 18.1) can hold the database for the duration of an atomic write while other short-lived
        // repository connections keep reading concurrently, instead of contending for an exclusive
        // rollback-journal lock and failing with "database is locked". journal_mode is persisted in the
        // database file, so setting it once here applies to every later connection. This affects only
        // the separate Commerce database and leaves the P0_Security_System databases untouched (C-2).
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

        foreach (CommerceTableDefinition table in Tables)
        {
            await CreateTableAsync(connection, table, cancellationToken);

            // Additive, idempotent column reconciliation so the final schema is identical on every
            // run and any future column additions are applied without error (Req 3.3 / Property 5).
            foreach (CommerceColumn column in table.Columns)
            {
                await EnsureColumnAsync(connection, table.TableName, column.Name, column.Definition, cancellationToken);
            }
        }

        // Business_Key uniqueness at the storage layer (Req 4.20 / 18.6): a partial UNIQUE index per
        // idempotency-bearing table closes the concurrency gap in the "find-then-insert" helper so a
        // duplicate keyed record can never be persisted even under concurrent core writes.
        await EnsureBusinessKeyUniqueIndexesAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Tables whose generated records carry a Business_Key and must not be duplicated per workspace
    /// (Req 4.20 / 18.6): payments, cash-flow entries, inventory movements, generated insights, and
    /// metric snapshots. Each gets a partial UNIQUE index over <c>(WorkspaceId, BusinessKey)</c>.
    /// </summary>
    private static readonly IReadOnlyList<string> BusinessKeyScopedTables = new[]
    {
        "CommercePaymentRecords",
        "CommerceCashFlowEntries",
        "CommerceInventoryMovements",
        "CommerceBusinessInsights",
        "CommerceBusinessMetricSnapshots",
    };

    /// <summary>
    /// Idempotently creates a partial UNIQUE index over <c>(WorkspaceId, BusinessKey)</c> for every
    /// <see cref="BusinessKeyScopedTables"/> entry. The index is partial so it constrains only active
    /// (non-soft-deleted) records that actually carry a non-empty Business_Key — null/empty keys are
    /// unconstrained (ad-hoc records can repeat), and the same key is allowed across different
    /// workspaces because the workspace id is the leading index column.
    ///
    /// <para><b>Existing-duplicate safety.</b> Creating a UNIQUE index over a table that already
    /// contains duplicates would throw and abort schema init. To remain idempotent and
    /// <i>non-destructive</i> (Req 3.3), each table is first scanned for pre-existing active duplicate
    /// keys; if any are found the condition is recorded in <c>OrderlySchemaDiagnostics</c> and the
    /// index is skipped rather than silently deleting data, so the situation is surfaced for operator
    /// follow-up. A clean table gets its index created with <c>IF NOT EXISTS</c>, so repeat runs are
    /// no-ops.</para>
    /// </summary>
    private static async Task EnsureBusinessKeyUniqueIndexesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureDiagnosticsTableAsync(connection, cancellationToken);

        foreach (string table in BusinessKeyScopedTables)
        {
            string safeTable = QuoteSqlIdentifier(table);
            string indexName = QuoteSqlIdentifier($"UX_{table}_WorkspaceId_BusinessKey");
            const string keyedActiveFilter = "\"BusinessKey\" IS NOT NULL AND \"BusinessKey\" <> '' AND \"DeletedAt\" IS NULL";

            int duplicateGroups = await CountDuplicateBusinessKeyGroupsAsync(connection, safeTable, keyedActiveFilter, cancellationToken);
            if (duplicateGroups > 0)
            {
                await RecordDiagnosticAsync(
                    connection,
                    code: "BusinessKeyDuplicatesDetected",
                    detail: $"表 {table} 存在 {duplicateGroups} 组重复 (WorkspaceId, BusinessKey) 活动记录；已跳过唯一索引创建，未删除任何数据。",
                    cancellationToken);
                continue;
            }

            await ExecuteAsync(
                connection,
                $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {safeTable} (\"WorkspaceId\", \"BusinessKey\") WHERE {keyedActiveFilter};",
                cancellationToken);
        }
    }

    private static async Task<int> CountDuplicateBusinessKeyGroupsAsync(SqliteConnection connection, string safeTable, string keyedActiveFilter, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM (SELECT 1 FROM {safeTable} WHERE {keyedActiveFilter} " +
            "GROUP BY \"WorkspaceId\", \"BusinessKey\" HAVING COUNT(*) > 1);";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task EnsureDiagnosticsTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            "CREATE TABLE IF NOT EXISTS \"OrderlySchemaDiagnostics\" (" +
            "Id TEXT NOT NULL PRIMARY KEY, " +
            "Code TEXT NOT NULL, " +
            "Detail TEXT NOT NULL DEFAULT '', " +
            "DetectedAt TEXT NOT NULL);",
            cancellationToken);
    }

    private static async Task RecordDiagnosticAsync(SqliteConnection connection, string code, string detail, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"OrderlySchemaDiagnostics\" (Id, Code, Detail, DetectedAt) " +
            "VALUES ($id, $code, $detail, $detectedAt);";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$detectedAt", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateTableAsync(SqliteConnection connection, CommerceTableDefinition table, CancellationToken cancellationToken)
    {
        string columnList = string.Join(
            ",\n    ",
            table.Columns.Select(column => $"{QuoteSqlIdentifier(column.Name)} {column.Definition}"));

        string commandText =
            $"CREATE TABLE IF NOT EXISTS {QuoteSqlIdentifier(table.TableName)} (\n    {columnList}\n);";

        await ExecuteAsync(connection, commandText, cancellationToken);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, table, column, cancellationToken))
        {
            return;
        }

        // The primary-key column is always present after CREATE TABLE; never attempt to ALTER it in.
        if (definition.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string safeTable = QuoteSqlIdentifier(table);
        string safeColumn = QuoteSqlIdentifier(column);
        await ExecuteAsync(connection, $"ALTER TABLE {safeTable} ADD COLUMN {safeColumn} {definition};", cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        string safeTable = QuoteSqlIdentifier(table);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({safeTable});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        if (!IsSafeSqlIdentifier(identifier))
        {
            throw new InvalidOperationException("SQLite identifier is invalid.");
        }

        return "\"" + identifier + "\"";
    }

    private static bool IsSafeSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        for (int index = 0; index < identifier.Length; index++)
        {
            char character = identifier[index];
            bool isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            if (index == 0)
            {
                if (!isAsciiLetter && character != '_')
                {
                    return false;
                }
            }
            else if (!isAsciiLetter && !isDigit && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
