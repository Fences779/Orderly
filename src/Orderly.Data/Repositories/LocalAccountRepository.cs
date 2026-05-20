using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class LocalAccountRepository : ILocalAccountRepository
{
    private readonly LauncherConnectionFactory _connectionFactory;

    public LocalAccountRepository(LauncherConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM LocalAccounts;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    public async Task<IReadOnlyList<LocalAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} ORDER BY CreatedAt ASC;";

        var list = new List<LocalAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public async Task<LocalAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE Username = $username LIMIT 1;";
        command.Parameters.AddWithValue("$username", username.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<LocalAccount?> GetByAccountIdAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE AccountId = $accountId LIMIT 1;";
        command.Parameters.AddWithValue("$accountId", accountId.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task CreateAsync(LocalAccount account, CancellationToken cancellationToken = default)
    {
        if (account is null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LocalAccounts (
                AccountId,
                Username,
                DisplayName,
                PasswordHash,
                PasswordSalt,
                PasswordIterations,
                PinHash,
                PinSalt,
                PinIterations,
                RecoveryKeyHash,
                RecoveryKeySalt,
                RecoveryKeyIterations,
                RecoveryEncryptedDataKey,
                RecoveryDataKeyNonce,
                RecoveryDataKeyTag,
                EncryptedDataKey,
                DataKeyNonce,
                DataKeyTag,
                AdminOwnerAccountId,
                AdminEncryptedDataKey,
                AdminDataKeyNonce,
                AdminDataKeyTag,
                DatabasePath,
                Role,
                IsEnabled,
                CreatedAt,
                UpdatedAt,
                LastLoginAt
            )
            VALUES (
                $accountId,
                $username,
                $displayName,
                $passwordHash,
                $passwordSalt,
                $passwordIterations,
                $pinHash,
                $pinSalt,
                $pinIterations,
                $recoveryKeyHash,
                $recoveryKeySalt,
                $recoveryKeyIterations,
                $recoveryEncryptedDataKey,
                $recoveryDataKeyNonce,
                $recoveryDataKeyTag,
                $encryptedDataKey,
                $dataKeyNonce,
                $dataKeyTag,
                $adminOwnerAccountId,
                $adminEncryptedDataKey,
                $adminDataKeyNonce,
                $adminDataKeyTag,
                $databasePath,
                $role,
                $isEnabled,
                $createdAt,
                $updatedAt,
                $lastLoginAt
            );
            """;
        AddParameters(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(LocalAccount account, CancellationToken cancellationToken = default)
    {
        if (account is null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE LocalAccounts
            SET
                Username = $username,
                DisplayName = $displayName,
                PasswordHash = $passwordHash,
                PasswordSalt = $passwordSalt,
                PasswordIterations = $passwordIterations,
                PinHash = $pinHash,
                PinSalt = $pinSalt,
                PinIterations = $pinIterations,
                RecoveryKeyHash = $recoveryKeyHash,
                RecoveryKeySalt = $recoveryKeySalt,
                RecoveryKeyIterations = $recoveryKeyIterations,
                RecoveryEncryptedDataKey = $recoveryEncryptedDataKey,
                RecoveryDataKeyNonce = $recoveryDataKeyNonce,
                RecoveryDataKeyTag = $recoveryDataKeyTag,
                EncryptedDataKey = $encryptedDataKey,
                DataKeyNonce = $dataKeyNonce,
                DataKeyTag = $dataKeyTag,
                AdminOwnerAccountId = $adminOwnerAccountId,
                AdminEncryptedDataKey = $adminEncryptedDataKey,
                AdminDataKeyNonce = $adminDataKeyNonce,
                AdminDataKeyTag = $adminDataKeyTag,
                DatabasePath = $databasePath,
                Role = $role,
                IsEnabled = $isEnabled,
                CreatedAt = $createdAt,
                UpdatedAt = $updatedAt,
                LastLoginAt = $lastLoginAt
            WHERE AccountId = $accountId;
            """;
        AddParameters(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("Account id cannot be empty.", nameof(accountId));
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM LocalAccounts
            WHERE AccountId = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SelectColumns = """
        SELECT
            AccountId,
            Username,
            DisplayName,
            PasswordHash,
            PasswordSalt,
            PasswordIterations,
            PinHash,
            PinSalt,
            PinIterations,
            RecoveryKeyHash,
            RecoveryKeySalt,
            RecoveryKeyIterations,
            RecoveryEncryptedDataKey,
            RecoveryDataKeyNonce,
            RecoveryDataKeyTag,
            EncryptedDataKey,
            DataKeyNonce,
            DataKeyTag,
            AdminOwnerAccountId,
            AdminEncryptedDataKey,
            AdminDataKeyNonce,
            AdminDataKeyTag,
            DatabasePath,
            Role,
            IsEnabled,
            CreatedAt,
            UpdatedAt,
            LastLoginAt
        FROM LocalAccounts
        """;

    private static LocalAccount Map(SqliteDataReader reader)
    {
        return new LocalAccount
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            DisplayName = reader.GetString(2),
            PasswordHash = (byte[])reader[3],
            PasswordSalt = (byte[])reader[4],
            PasswordIterations = reader.GetInt32(5),
            PinHash = (byte[])reader[6],
            PinSalt = (byte[])reader[7],
            PinIterations = reader.GetInt32(8),
            RecoveryKeyHash = reader.IsDBNull(9) ? [] : (byte[])reader[9],
            RecoveryKeySalt = reader.IsDBNull(10) ? [] : (byte[])reader[10],
            RecoveryKeyIterations = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            RecoveryEncryptedDataKey = reader.IsDBNull(12) ? [] : (byte[])reader[12],
            RecoveryDataKeyNonce = reader.IsDBNull(13) ? [] : (byte[])reader[13],
            RecoveryDataKeyTag = reader.IsDBNull(14) ? [] : (byte[])reader[14],
            EncryptedDataKey = (byte[])reader[15],
            DataKeyNonce = (byte[])reader[16],
            DataKeyTag = (byte[])reader[17],
            AdminOwnerAccountId = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
            AdminEncryptedDataKey = reader.IsDBNull(19) ? [] : (byte[])reader[19],
            AdminDataKeyNonce = reader.IsDBNull(20) ? [] : (byte[])reader[20],
            AdminDataKeyTag = reader.IsDBNull(21) ? [] : (byte[])reader[21],
            DatabasePath = reader.GetString(22),
            Role = (LocalAccountRole)reader.GetInt32(23),
            IsEnabled = reader.GetInt32(24) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(25), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(26), null, DateTimeStyles.RoundtripKind),
            LastLoginAt = reader.IsDBNull(27) ? null : DateTime.Parse(reader.GetString(27), null, DateTimeStyles.RoundtripKind)
        };
    }

    private static void AddParameters(SqliteCommand command, LocalAccount account)
    {
        command.Parameters.AddWithValue("$accountId", account.AccountId);
        command.Parameters.AddWithValue("$username", account.Username);
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("$passwordSalt", account.PasswordSalt);
        command.Parameters.AddWithValue("$passwordIterations", account.PasswordIterations);
        command.Parameters.AddWithValue("$pinHash", account.PinHash);
        command.Parameters.AddWithValue("$pinSalt", account.PinSalt);
        command.Parameters.AddWithValue("$pinIterations", account.PinIterations);
        command.Parameters.AddWithValue("$recoveryKeyHash", ToDbBlob(account.RecoveryKeyHash));
        command.Parameters.AddWithValue("$recoveryKeySalt", ToDbBlob(account.RecoveryKeySalt));
        command.Parameters.AddWithValue("$recoveryKeyIterations", ToDbInt(account.RecoveryKeyIterations));
        command.Parameters.AddWithValue("$recoveryEncryptedDataKey", ToDbBlob(account.RecoveryEncryptedDataKey));
        command.Parameters.AddWithValue("$recoveryDataKeyNonce", ToDbBlob(account.RecoveryDataKeyNonce));
        command.Parameters.AddWithValue("$recoveryDataKeyTag", ToDbBlob(account.RecoveryDataKeyTag));
        command.Parameters.AddWithValue("$encryptedDataKey", account.EncryptedDataKey);
        command.Parameters.AddWithValue("$dataKeyNonce", account.DataKeyNonce);
        command.Parameters.AddWithValue("$dataKeyTag", account.DataKeyTag);
        command.Parameters.AddWithValue("$adminOwnerAccountId", ToDbText(account.AdminOwnerAccountId));
        command.Parameters.AddWithValue("$adminEncryptedDataKey", ToDbBlob(account.AdminEncryptedDataKey));
        command.Parameters.AddWithValue("$adminDataKeyNonce", ToDbBlob(account.AdminDataKeyNonce));
        command.Parameters.AddWithValue("$adminDataKeyTag", ToDbBlob(account.AdminDataKeyTag));
        command.Parameters.AddWithValue("$databasePath", account.DatabasePath);
        command.Parameters.AddWithValue("$role", (int)account.Role);
        command.Parameters.AddWithValue("$isEnabled", account.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", account.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", account.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastLoginAt", ToDbDate(account.LastLoginAt));
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }

    private static object ToDbBlob(byte[] value)
    {
        return value.Length == 0 ? DBNull.Value : value;
    }

    private static object ToDbText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static object ToDbInt(int value)
    {
        return value <= 0 ? DBNull.Value : value;
    }
}
