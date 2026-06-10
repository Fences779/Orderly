using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

public sealed class LauncherDatabaseInitializer
{
    private readonly LauncherConnectionFactory _connectionFactory;

    public LauncherDatabaseInitializer(LauncherConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "启动器数据库目录");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        LocalDataFileSecurity.HardenSqliteDatabaseFiles(_connectionFactory.DatabasePath);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS LocalAccounts (
                AccountId TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                PasswordHash BLOB NOT NULL,
                PasswordSalt BLOB NOT NULL,
                PasswordIterations INTEGER NOT NULL,
                PinHash BLOB NOT NULL,
                PinSalt BLOB NOT NULL,
                PinIterations INTEGER NOT NULL,
                RecoveryKeyHash BLOB NULL,
                RecoveryKeySalt BLOB NULL,
                RecoveryKeyIterations INTEGER NULL,
                RecoveryEncryptedDataKey BLOB NULL,
                RecoveryDataKeyNonce BLOB NULL,
                RecoveryDataKeyTag BLOB NULL,
                EncryptedDataKey BLOB NOT NULL,
                DataKeyNonce BLOB NOT NULL,
                DataKeyTag BLOB NOT NULL,
                AdminOwnerAccountId TEXT NULL,
                AdminEncryptedDataKey BLOB NULL,
                AdminDataKeyNonce BLOB NULL,
                AdminDataKeyTag BLOB NULL,
                DatabasePath TEXT NOT NULL,
                Role INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                LastLoginAt TEXT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS IX_LocalAccounts_Username
            ON LocalAccounts(Username);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS IX_LocalAccounts_IsEnabled
            ON LocalAccounts(IsEnabled);
            """, cancellationToken);

        await EnsureColumnAsync(connection, "LocalAccounts", "RecoveryEncryptedDataKey", "BLOB NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "RecoveryDataKeyNonce", "BLOB NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "RecoveryDataKeyTag", "BLOB NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "AdminOwnerAccountId", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "AdminEncryptedDataKey", "BLOB NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "AdminDataKeyNonce", "BLOB NULL", cancellationToken);
        await EnsureColumnAsync(connection, "LocalAccounts", "AdminDataKeyTag", "BLOB NULL", cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
}
