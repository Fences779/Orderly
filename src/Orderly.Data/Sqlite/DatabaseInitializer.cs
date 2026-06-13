using Microsoft.Data.Sqlite;
using Orderly.Core.Models;

namespace Orderly.Data.Sqlite;

public sealed partial class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "数据库目录");
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(_connectionFactory.DatabasePath, "数据库文件");
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        LocalDataFileSecurity.EnsureFileIsNotLinked(_connectionFactory.DatabasePath, "数据库文件");
        LocalDataFileSecurity.HardenSqliteDatabaseFiles(_connectionFactory.DatabasePath);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 1,
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ContactHandle TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Remark TEXT NOT NULL DEFAULT '',
                ExternalId TEXT NOT NULL DEFAULT '',
                RawPayload TEXT NOT NULL DEFAULT '',
                LastContactAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Deals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Stage INTEGER NOT NULL,
                EstimatedAmount REAL NOT NULL DEFAULT 0,
                Requirement TEXT NOT NULL DEFAULT '',
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ExpectedCloseAt TEXT NULL,
                ClosedAt TEXT NULL,
                LostReason TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                Title TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Amount REAL NOT NULL DEFAULT 0,
                Requirement TEXT NOT NULL DEFAULT '',
                SourcePlatform TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT '',
                ExternalId TEXT NOT NULL DEFAULT '',
                RawPayload TEXT NOT NULL DEFAULT '',
                NextFollowUpAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS FollowUps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                ScheduledAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ReminderAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS CustomerNotes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Type INTEGER NOT NULL,
                Content TEXT NOT NULL DEFAULT '',
                IsPinned INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ActivityLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Type INTEGER NOT NULL,
                CustomerId INTEGER NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                Title TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                Operator TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE SET NULL,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ConversationMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                DealId INTEGER NULL,
                Direction INTEGER NOT NULL,
                Channel INTEGER NOT NULL,
                SenderName TEXT NOT NULL DEFAULT '',
                Content TEXT NOT NULL DEFAULT '',
                MessageTime TEXT NOT NULL,
                SourceMessageId TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AiSuggestions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                OrderId INTEGER NULL,
                MessageId INTEGER NULL,
                SuggestionText TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',
                Confidence REAL NULL,
                Status INTEGER NOT NULL,
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL,
                FOREIGN KEY (MessageId) REFERENCES ConversationMessages(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS OcrResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NULL,
                OrderId INTEGER NULL,
                SourcePath TEXT NOT NULL DEFAULT '',
                SourceName TEXT NOT NULL DEFAULT '',
                ExtractedText TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SyncRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EntityType TEXT NOT NULL DEFAULT '',
                EntityId INTEGER NOT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                SyncStatus INTEGER NOT NULL,
                LastSyncedAt TEXT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT '',
                ErrorMessageCiphertext TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '',
                MetadataJsonCiphertext TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS PriceAdjustments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                DealId INTEGER NULL,
                OrderId INTEGER NULL,
                OriginalAmount REAL NOT NULL DEFAULT 0,
                AdjustedAmount REAL NOT NULL DEFAULT 0,
                Reason TEXT NOT NULL DEFAULT '',
                Status INTEGER NOT NULL,
                RequestedBy TEXT NOT NULL DEFAULT '',
                ApprovedBy TEXT NOT NULL DEFAULT '',
                ApprovedAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                DeletedAt TEXT NULL,
                RemoteId TEXT NOT NULL DEFAULT '',
                IsSynced INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE,
                FOREIGN KEY (DealId) REFERENCES Deals(Id) ON DELETE SET NULL,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE SET NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS ReplyTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Scene TEXT NOT NULL DEFAULT '',
                Content TEXT NOT NULL,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                SourcePlatform TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """, cancellationToken);

        // 安全审计存储（BC-6 / 任务 11.3）。写入会话数据密钥加密的本账号库（全库 SQLCipher 加密），
        // 追加式 + 链式完整性哈希保持防篡改；仅保存事件类型 / 时间 / 账号标签 / 脱敏 detail，绝不存明文凭证。
        // Sequence 为单调递增主键固定记录顺序；PreviousHash/RecordHash 构成防篡改哈希链；全量保留不截断。
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SecurityAuditEntries (
                Sequence INTEGER PRIMARY KEY,
                Kind INTEGER NOT NULL,
                OccurredAt TEXT NOT NULL,
                AccountLabel TEXT NOT NULL DEFAULT '',
                Detail TEXT NOT NULL DEFAULT '',
                PreviousHash TEXT NOT NULL,
                RecordHash TEXT NOT NULL
            );
            """, cancellationToken);

        await EnsureSchemaAsync(connection, cancellationToken);
        await SeedAsync(connection, cancellationToken);

        // Initialize the Universal_Domain_Model (Commerce) schema in the same per-workspace
        // database. This is additive and idempotent (Req 3.3) and leaves the legacy CRM tables,
        // the launcher DB, and the multi-account structure unchanged (Req 1.5, C-2).
        await CommerceSchemaInitializer.InitializeSchemaAsync(connection, cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "Customers", "Status", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "Priority", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "LastContactAt", "TEXT NULL", cancellationToken);
        await EnsureEntityColumnsAsync(connection, "Customers", cancellationToken);

        await EnsureColumnAsync(connection, "Orders", "DealId", "INTEGER NULL", cancellationToken);
        await EnsureEntityColumnsAsync(connection, "Orders", cancellationToken);

        await EnsureSensitiveCipherColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureSensitiveCipherColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, "Customers", "NameCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "ContactHandleCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "PhoneCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "RemarkCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "ExternalIdCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "RawPayloadCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Customers", "LastContactAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "Deals", "TitleCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Deals", "EstimatedAmountCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Deals", "RequirementCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Deals", "ExpectedCloseAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Deals", "ClosedAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Deals", "LostReasonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "Orders", "TitleCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Orders", "AmountCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Orders", "RequirementCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Orders", "ExternalIdCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Orders", "RawPayloadCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Orders", "NextFollowUpAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "FollowUps", "TitleCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "FollowUps", "ContentCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "FollowUps", "ScheduledAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "FollowUps", "CompletedAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "FollowUps", "ReminderAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "CustomerNotes", "ContentCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "ActivityLogs", "TitleCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ActivityLogs", "DescriptionCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ActivityLogs", "OperatorCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ActivityLogs", "MetadataJsonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "ConversationMessages", "SenderNameCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ConversationMessages", "ContentCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ConversationMessages", "MessageTimeCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ConversationMessages", "SourceMessageIdCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "ConversationMessages", "MetadataJsonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "AiSuggestions", "SuggestionTextCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "AiSuggestions", "ReasonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "AiSuggestions", "ConfidenceCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "AiSuggestions", "MetadataJsonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "OcrResults", "SourcePathCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "OcrResults", "SourceNameCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "OcrResults", "ExtractedTextCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "OcrResults", "ErrorMessageCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "OcrResults", "MetadataJsonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "PriceAdjustments", "OriginalAmountCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "PriceAdjustments", "AdjustedAmountCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "PriceAdjustments", "ReasonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "PriceAdjustments", "RequestedByCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "PriceAdjustments", "ApprovedByCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "PriceAdjustments", "ApprovedAtCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "ReplyTemplates", "ContentCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);

        await EnsureColumnAsync(connection, "SyncRecords", "ErrorMessageCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "SyncRecords", "MetadataJsonCiphertext", "TEXT NOT NULL DEFAULT ''", cancellationToken);
    }

    private static async Task EnsureEntityColumnsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, table, "DeletedAt", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, table, "RemoteId", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, table, "IsSynced", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, table, "Version", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, table, column, cancellationToken))
        {
            return;
        }

        var safeTable = QuoteSqlIdentifier(table);
        var safeColumn = QuoteSqlIdentifier(column);
        await ExecuteAsync(connection, $"ALTER TABLE {safeTable} ADD COLUMN {safeColumn} {definition};", cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        var safeTable = QuoteSqlIdentifier(table);
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

        for (var index = 0; index < identifier.Length; index++)
        {
            var character = identifier[index];
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
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

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSettingAsync(SqliteConnection connection, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
