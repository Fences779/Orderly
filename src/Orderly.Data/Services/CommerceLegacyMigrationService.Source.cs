using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Orderly.Core.Commerce.Migration;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

/// <summary>
/// Source reading, target-schema initialization, and outcome logging for
/// <see cref="CommerceLegacyMigrationService"/>. The reads target only the generic CRM tables and
/// only active (non-soft-deleted) rows; the legacy <c>ActivityLogs</c> table is never read so it is
/// retained unchanged (Req 3.4), and no customer-specific / industry-specific remote columns
/// (e.g. <c>RawPayload</c>, <c>ExternalId</c>) are read (Req 3.5).
/// </summary>
public sealed partial class CommerceLegacyMigrationService
{
    private const string MigrationLogTable = "CommerceLegacyMigrationLog";

    private async Task<LegacySnapshot> ReadSourceAsync(CancellationToken cancellationToken)
    {
        var snapshot = new LegacySnapshot();

        await using SqliteConnection connection = _sourceConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        snapshot.Customers = await ReadCustomersAsync(connection, cancellationToken);
        snapshot.Orders = await ReadOrdersAsync(connection, cancellationToken);
        snapshot.Deals = await ReadDealsAsync(connection, cancellationToken);
        snapshot.FollowUps = await ReadFollowUpsAsync(connection, cancellationToken);
        snapshot.CustomerNotes = await ReadCustomerNotesAsync(connection, cancellationToken);

        return snapshot;
    }

    private async Task<List<LegacyCustomer>> ReadCustomersAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyCustomer>();
        if (!await TableExistsAsync(connection, "Customers", cancellationToken))
        {
            return rows;
        }

        HashSet<string> columns = await GetColumnsAsync(connection, "Customers", cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, " + CipherOrNull(columns, "NameCiphertext") + ", " +
            "Phone, " + CipherOrNull(columns, "PhoneCiphertext") + ", " +
            "ContactHandle, " + CipherOrNull(columns, "ContactHandleCiphertext") + ", " +
            "Remark, " + CipherOrNull(columns, "RemarkCiphertext") + ", " +
            "CreatedAt, UpdatedAt " +
            "FROM Customers WHERE DeletedAt IS NULL ORDER BY Id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            rows.Add(new LegacyCustomer
            {
                Id = id,
                Name = ResolveString(reader, 1, 2, "Customers", "NameCiphertext", id),
                Phone = ResolveString(reader, 3, 4, "Customers", "PhoneCiphertext", id),
                ContactHandle = ResolveString(reader, 5, 6, "Customers", "ContactHandleCiphertext", id),
                Remark = ResolveString(reader, 7, 8, "Customers", "RemarkCiphertext", id),
                CreatedAt = ReadNullableString(reader, 9),
                UpdatedAt = ReadNullableString(reader, 10),
            });
        }

        return rows;
    }

    private async Task<List<LegacyOrder>> ReadOrdersAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyOrder>();
        if (!await TableExistsAsync(connection, "Orders", cancellationToken))
        {
            return rows;
        }

        HashSet<string> columns = await GetColumnsAsync(connection, "Orders", cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, CustomerId, Title, " + CipherOrNull(columns, "TitleCiphertext") + ", " +
            "Status, Amount, " + CipherOrNull(columns, "AmountCiphertext") + ", " +
            "Requirement, " + CipherOrNull(columns, "RequirementCiphertext") + ", " +
            "CreatedAt, UpdatedAt " +
            "FROM Orders WHERE DeletedAt IS NULL ORDER BY Id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            rows.Add(new LegacyOrder
            {
                Id = id,
                CustomerId = reader.GetInt64(1),
                Title = ResolveString(reader, 2, 3, "Orders", "TitleCiphertext", id),
                Status = (int)reader.GetInt64(4),
                Amount = ResolveDouble(reader, 5, 6, "Orders", "AmountCiphertext", id),
                Requirement = ResolveString(reader, 7, 8, "Orders", "RequirementCiphertext", id),
                CreatedAt = ReadNullableString(reader, 9),
                UpdatedAt = ReadNullableString(reader, 10),
            });
        }

        return rows;
    }

    private async Task<List<LegacyDeal>> ReadDealsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyDeal>();
        if (!await TableExistsAsync(connection, "Deals", cancellationToken))
        {
            return rows;
        }

        HashSet<string> columns = await GetColumnsAsync(connection, "Deals", cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, CustomerId, Title, " + CipherOrNull(columns, "TitleCiphertext") + ", " +
            "Stage, EstimatedAmount, " + CipherOrNull(columns, "EstimatedAmountCiphertext") + ", " +
            "Requirement, " + CipherOrNull(columns, "RequirementCiphertext") + ", " +
            "ExpectedCloseAt, " + CipherOrNull(columns, "ExpectedCloseAtCiphertext") + ", " +
            "ClosedAt, " + CipherOrNull(columns, "ClosedAtCiphertext") + ", " +
            "CreatedAt, UpdatedAt " +
            "FROM Deals WHERE DeletedAt IS NULL ORDER BY Id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            rows.Add(new LegacyDeal
            {
                Id = id,
                CustomerId = reader.GetInt64(1),
                Title = ResolveString(reader, 2, 3, "Deals", "TitleCiphertext", id),
                Stage = (int)reader.GetInt64(4),
                EstimatedAmount = ResolveDouble(reader, 5, 6, "Deals", "EstimatedAmountCiphertext", id),
                Requirement = ResolveString(reader, 7, 8, "Deals", "RequirementCiphertext", id),
                ExpectedCloseAt = ResolveNullableString(reader, 9, 10, "Deals", "ExpectedCloseAtCiphertext", id),
                ClosedAt = ResolveNullableString(reader, 11, 12, "Deals", "ClosedAtCiphertext", id),
                CreatedAt = ReadNullableString(reader, 13),
                UpdatedAt = ReadNullableString(reader, 14),
            });
        }

        return rows;
    }

    private async Task<List<LegacyFollowUp>> ReadFollowUpsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyFollowUp>();
        if (!await TableExistsAsync(connection, "FollowUps", cancellationToken))
        {
            return rows;
        }

        HashSet<string> columns = await GetColumnsAsync(connection, "FollowUps", cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, CustomerId, OrderId, Title, " + CipherOrNull(columns, "TitleCiphertext") + ", " +
            "Content, " + CipherOrNull(columns, "ContentCiphertext") + ", " +
            "Status, ScheduledAt, " + CipherOrNull(columns, "ScheduledAtCiphertext") + ", " +
            "CompletedAt, " + CipherOrNull(columns, "CompletedAtCiphertext") + ", " +
            "CreatedAt, UpdatedAt " +
            "FROM FollowUps WHERE DeletedAt IS NULL ORDER BY Id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            rows.Add(new LegacyFollowUp
            {
                Id = id,
                CustomerId = reader.GetInt64(1),
                OrderId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                Title = ResolveString(reader, 3, 4, "FollowUps", "TitleCiphertext", id),
                Content = ResolveString(reader, 5, 6, "FollowUps", "ContentCiphertext", id),
                Status = (int)reader.GetInt64(7),
                ScheduledAt = ResolveNullableString(reader, 8, 9, "FollowUps", "ScheduledAtCiphertext", id),
                CompletedAt = ResolveNullableString(reader, 10, 11, "FollowUps", "CompletedAtCiphertext", id),
                CreatedAt = ReadNullableString(reader, 12),
                UpdatedAt = ReadNullableString(reader, 13),
            });
        }

        return rows;
    }

    private async Task<List<LegacyCustomerNote>> ReadCustomerNotesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<LegacyCustomerNote>();
        if (!await TableExistsAsync(connection, "CustomerNotes", cancellationToken))
        {
            return rows;
        }

        HashSet<string> columns = await GetColumnsAsync(connection, "CustomerNotes", cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, CustomerId, OrderId, Type, Content, " + CipherOrNull(columns, "ContentCiphertext") + ", " +
            "IsPinned, CreatedAt, UpdatedAt " +
            "FROM CustomerNotes WHERE DeletedAt IS NULL ORDER BY Id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long id = reader.GetInt64(0);
            rows.Add(new LegacyCustomerNote
            {
                Id = id,
                CustomerId = reader.GetInt64(1),
                OrderId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                Type = (int)reader.GetInt64(3),
                Content = ResolveString(reader, 4, 5, "CustomerNotes", "ContentCiphertext", id),
                IsPinned = reader.GetInt64(6) != 0,
                CreatedAt = ReadNullableString(reader, 7),
                UpdatedAt = ReadNullableString(reader, 8),
            });
        }

        return rows;
    }

    /// <summary>Returns the set of column names defined on <paramref name="table"/> (case-insensitive).</summary>
    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    /// <summary>
    /// Selects the named ciphertext column when it exists on the table, otherwise selects a NULL slot
    /// so the read keeps a fixed column layout regardless of whether the source database predates the
    /// P0 field-encryption columns.
    /// </summary>
    private static string CipherOrNull(HashSet<string> columns, string cipherColumn)
        => columns.Contains(cipherColumn) ? $"\"{cipherColumn}\"" : "NULL";

    /// <summary>
    /// Resolves a sensitive string field: decrypts the <c>*Ciphertext</c> value when present and a
    /// field-encryption service is available (P0-encrypted source), otherwise falls back to the legacy
    /// plaintext column (pre-encryption source). Decryption failures fall back to the plaintext value
    /// so a single unreadable field can never abort the whole migration.
    /// </summary>
    private string ResolveString(SqliteDataReader reader, int plainIndex, int cipherIndex, string table, string cipherColumn, long rowId)
    {
        string? decrypted = TryDecryptField(reader, cipherIndex, table, cipherColumn, rowId);
        return decrypted ?? ReadString(reader, plainIndex);
    }

    /// <summary>As <see cref="ResolveString"/> but returns null when neither source carries a value.</summary>
    private string? ResolveNullableString(SqliteDataReader reader, int plainIndex, int cipherIndex, string table, string cipherColumn, long rowId)
    {
        string? decrypted = TryDecryptField(reader, cipherIndex, table, cipherColumn, rowId);
        if (!string.IsNullOrEmpty(decrypted))
        {
            return decrypted;
        }

        return ReadNullableString(reader, plainIndex);
    }

    /// <summary>Resolves a sensitive numeric field (stored encrypted as invariant text, or plaintext REAL).</summary>
    private double ResolveDouble(SqliteDataReader reader, int plainIndex, int cipherIndex, string table, string cipherColumn, long rowId)
    {
        string? decrypted = TryDecryptField(reader, cipherIndex, table, cipherColumn, rowId);
        if (!string.IsNullOrWhiteSpace(decrypted)
            && double.TryParse(decrypted, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value))
        {
            return value;
        }

        return ReadDouble(reader, plainIndex);
    }

    /// <summary>
    /// Attempts to decrypt the ciphertext at <paramref name="cipherIndex"/> using the field scope the
    /// repositories use (<c>"{table}.{cipherColumn}|row:{rowId}"</c>). Returns null when there is no
    /// service, the slot is null/empty, or decryption fails (so the caller falls back to plaintext).
    /// </summary>
    private string? TryDecryptField(SqliteDataReader reader, int cipherIndex, string table, string cipherColumn, long rowId)
    {
        if (_fieldEncryption is null || rowId <= 0 || reader.IsDBNull(cipherIndex))
        {
            return null;
        }

        string cipher = reader.GetString(cipherIndex);
        if (string.IsNullOrWhiteSpace(cipher))
        {
            return null;
        }

        try
        {
            return _fieldEncryption.Decrypt(cipher, EncryptedFieldScope.Build(table, cipherColumn, rowId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or FormatException)
        {
            return null;
        }
    }

    private async Task EnsureTargetSchemaAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = _targetConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await CommerceSchemaInitializer.InitializeSchemaAsync(connection, cancellationToken);
        await EnsureMigrationLogTableAsync(connection, cancellationToken);
    }

    private static async Task EnsureMigrationLogTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"CREATE TABLE IF NOT EXISTS \"{MigrationLogTable}\" (" +
            "Id TEXT NOT NULL PRIMARY KEY, " +
            "OutcomeToken TEXT NOT NULL, " +
            "MigratedRecordCount INTEGER NOT NULL, " +
            "Reason TEXT NOT NULL DEFAULT '', " +
            "BackupPath TEXT NULL, " +
            "CompletedAt TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a log entry recording the migration outcome and migrated record count (Req 3.9).
    /// Logging failures must never mask the migration result, so any error here is swallowed.
    /// </summary>
    private async Task TryWriteLogAsync(CommerceLegacyMigrationResult result, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = _targetConnectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await EnsureMigrationLogTableAsync(connection, cancellationToken);

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO \"{MigrationLogTable}\" " +
                "(Id, OutcomeToken, MigratedRecordCount, Reason, BackupPath, CompletedAt) " +
                "VALUES ($id, $token, $count, $reason, $backup, $completedAt);";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$token", result.OutcomeToken);
            command.Parameters.AddWithValue("$count", result.MigratedRecordCount);
            command.Parameters.AddWithValue("$reason", result.Reason ?? string.Empty);
            command.Parameters.AddWithValue("$backup", (object?)result.BackupPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$completedAt", result.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is SqliteException
                or InvalidOperationException
                or IOException)
        {
            // Logging is best-effort; never let a log write change the migration outcome.
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static string ReadString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static double ReadDouble(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0d : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------------------------
    // Materialized legacy source rows (only the generic, non-remote columns are read).
    // ---------------------------------------------------------------------------------------------

    private sealed class LegacySnapshot
    {
        public List<LegacyCustomer> Customers { get; set; } = new();
        public List<LegacyOrder> Orders { get; set; } = new();
        public List<LegacyDeal> Deals { get; set; } = new();
        public List<LegacyFollowUp> FollowUps { get; set; } = new();
        public List<LegacyCustomerNote> CustomerNotes { get; set; } = new();
    }

    private sealed class LegacyCustomer
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string ContactHandle { get; init; } = string.Empty;
        public string Remark { get; init; } = string.Empty;
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
    }

    private sealed class LegacyOrder
    {
        public long Id { get; init; }
        public long CustomerId { get; init; }
        public string Title { get; init; } = string.Empty;
        public int Status { get; init; }
        public double Amount { get; init; }
        public string Requirement { get; init; } = string.Empty;
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
    }

    private sealed class LegacyDeal
    {
        public long Id { get; init; }
        public long CustomerId { get; init; }
        public string Title { get; init; } = string.Empty;
        public int Stage { get; init; }
        public double EstimatedAmount { get; init; }
        public string Requirement { get; init; } = string.Empty;
        public string? ExpectedCloseAt { get; init; }
        public string? ClosedAt { get; init; }
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
    }

    private sealed class LegacyFollowUp
    {
        public long Id { get; init; }
        public long CustomerId { get; init; }
        public long? OrderId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public int Status { get; init; }
        public string? ScheduledAt { get; init; }
        public string? CompletedAt { get; init; }
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
    }

    private sealed class LegacyCustomerNote
    {
        public long Id { get; init; }
        public long CustomerId { get; init; }
        public long? OrderId { get; init; }
        public int Type { get; init; }
        public string Content { get; init; } = string.Empty;
        public bool IsPinned { get; init; }
        public string? CreatedAt { get; init; }
        public string? UpdatedAt { get; init; }
    }
}
