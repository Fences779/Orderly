using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Services;
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
        if (!LocalCredentialSecurity.TryNormalizeAccountUsername(username, out var normalizedUsername))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE Username = $username LIMIT 1;";
        command.Parameters.AddWithValue("$username", normalizedUsername);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<LocalAccount?> GetByAccountIdAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (!LocalCredentialSecurity.TryNormalizeAccountId(accountId, out var normalizedAccountId))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} WHERE AccountId = $accountId LIMIT 1;";
        command.Parameters.AddWithValue("$accountId", normalizedAccountId);

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
                PasswordKeyVersion,
                PinHash,
                PinSalt,
                PinIterations,
                PinHashVersion,
                RecoveryKeyHash,
                RecoveryKeySalt,
                RecoveryKeyIterations,
                RecoveryKeyVersion,
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
                LastLoginAt,
                MetadataMac
            )
            VALUES (
                $accountId,
                $username,
                $displayName,
                $passwordHash,
                $passwordSalt,
                $passwordIterations,
                $passwordKeyVersion,
                $pinHash,
                $pinSalt,
                $pinIterations,
                $pinHashVersion,
                $recoveryKeyHash,
                $recoveryKeySalt,
                $recoveryKeyIterations,
                $recoveryKeyVersion,
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
                $lastLoginAt,
                $metadataMac
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
                PasswordKeyVersion = $passwordKeyVersion,
                PinHash = $pinHash,
                PinSalt = $pinSalt,
                PinIterations = $pinIterations,
                PinHashVersion = $pinHashVersion,
                RecoveryKeyHash = $recoveryKeyHash,
                RecoveryKeySalt = $recoveryKeySalt,
                RecoveryKeyIterations = $recoveryKeyIterations,
                RecoveryKeyVersion = $recoveryKeyVersion,
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
                LastLoginAt = $lastLoginAt,
                MetadataMac = $metadataMac
            WHERE AccountId = $accountId;
            """;
        AddParameters(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var normalizedAccountId = LocalCredentialSecurity.NormalizeAccountId(accountId);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM LocalAccounts
            WHERE AccountId = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", normalizedAccountId);
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
            PasswordKeyVersion,
            PinHash,
            PinSalt,
            PinIterations,
            PinHashVersion,
            RecoveryKeyHash,
            RecoveryKeySalt,
            RecoveryKeyIterations,
            RecoveryKeyVersion,
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
            LastLoginAt,
            MetadataMac
        FROM LocalAccounts
        """;

    private static LocalAccount Map(SqliteDataReader reader)
    {
        var account = new LocalAccount
        {
            AccountId = reader.GetString(0),
            Username = reader.GetString(1),
            DisplayName = reader.GetString(2),
            PasswordHash = (byte[])reader[3],
            PasswordSalt = (byte[])reader[4],
            PasswordIterations = reader.GetInt32(5),
            PasswordKeyVersion = reader.GetInt32(6),
            PinHash = (byte[])reader[7],
            PinSalt = (byte[])reader[8],
            PinIterations = reader.GetInt32(9),
            PinHashVersion = reader.GetInt32(10),
            RecoveryKeyHash = reader.IsDBNull(11) ? [] : (byte[])reader[11],
            RecoveryKeySalt = reader.IsDBNull(12) ? [] : (byte[])reader[12],
            RecoveryKeyIterations = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
            RecoveryKeyVersion = reader.GetInt32(14),
            RecoveryEncryptedDataKey = reader.IsDBNull(15) ? [] : (byte[])reader[15],
            RecoveryDataKeyNonce = reader.IsDBNull(16) ? [] : (byte[])reader[16],
            RecoveryDataKeyTag = reader.IsDBNull(17) ? [] : (byte[])reader[17],
            EncryptedDataKey = (byte[])reader[18],
            DataKeyNonce = (byte[])reader[19],
            DataKeyTag = (byte[])reader[20],
            AdminOwnerAccountId = reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
            AdminEncryptedDataKey = reader.IsDBNull(22) ? [] : (byte[])reader[22],
            AdminDataKeyNonce = reader.IsDBNull(23) ? [] : (byte[])reader[23],
            AdminDataKeyTag = reader.IsDBNull(24) ? [] : (byte[])reader[24],
            DatabasePath = reader.GetString(25),
            Role = (LocalAccountRole)reader.GetInt32(26),
            IsEnabled = reader.GetInt32(27) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(28), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(29), null, DateTimeStyles.RoundtripKind),
            LastLoginAt = reader.IsDBNull(30) ? null : DateTime.Parse(reader.GetString(30), null, DateTimeStyles.RoundtripKind),
            MetadataMac = reader.IsDBNull(31) ? [] : (byte[])reader[31]
        };

        LocalAccountMetadataSecurity.VerifyOrThrow(account);
        return account;
    }

    private void AddParameters(SqliteCommand command, LocalAccount account)
    {
        var accountId = LocalCredentialSecurity.NormalizeAccountId(account.AccountId);
        var username = LocalCredentialSecurity.NormalizeAccountUsername(account.Username);
        var displayName = LocalCredentialSecurity.NormalizeAccountDisplayName(account.DisplayName, username);
        var adminOwnerAccountId = string.IsNullOrWhiteSpace(account.AdminOwnerAccountId)
            ? string.Empty
            : LocalCredentialSecurity.NormalizeAccountId(account.AdminOwnerAccountId);
        var databasePath = NormalizeAccountDatabasePath(accountId, account.DatabasePath);
        NormalizeSecurityVersions(account);
        account.AccountId = accountId;
        account.Username = username;
        account.DisplayName = displayName;
        account.AdminOwnerAccountId = adminOwnerAccountId;
        account.DatabasePath = databasePath;
        ValidateAccountSecurityMaterial(account, accountId, adminOwnerAccountId);
        account.MetadataMac = LocalAccountMetadataSecurity.ComputeMac(account);

        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$displayName", displayName);
        command.Parameters.AddWithValue("$passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("$passwordSalt", account.PasswordSalt);
        command.Parameters.AddWithValue("$passwordIterations", account.PasswordIterations);
        command.Parameters.AddWithValue("$passwordKeyVersion", account.PasswordKeyVersion);
        command.Parameters.AddWithValue("$pinHash", account.PinHash);
        command.Parameters.AddWithValue("$pinSalt", account.PinSalt);
        command.Parameters.AddWithValue("$pinIterations", account.PinIterations);
        command.Parameters.AddWithValue("$pinHashVersion", account.PinHashVersion);
        command.Parameters.AddWithValue("$recoveryKeyHash", ToDbBlob(account.RecoveryKeyHash));
        command.Parameters.AddWithValue("$recoveryKeySalt", ToDbBlob(account.RecoveryKeySalt));
        command.Parameters.AddWithValue("$recoveryKeyIterations", ToDbInt(account.RecoveryKeyIterations));
        command.Parameters.AddWithValue("$recoveryKeyVersion", account.RecoveryKeyVersion);
        command.Parameters.AddWithValue("$recoveryEncryptedDataKey", ToDbBlob(account.RecoveryEncryptedDataKey));
        command.Parameters.AddWithValue("$recoveryDataKeyNonce", ToDbBlob(account.RecoveryDataKeyNonce));
        command.Parameters.AddWithValue("$recoveryDataKeyTag", ToDbBlob(account.RecoveryDataKeyTag));
        command.Parameters.AddWithValue("$encryptedDataKey", account.EncryptedDataKey);
        command.Parameters.AddWithValue("$dataKeyNonce", account.DataKeyNonce);
        command.Parameters.AddWithValue("$dataKeyTag", account.DataKeyTag);
        command.Parameters.AddWithValue("$adminOwnerAccountId", ToDbText(adminOwnerAccountId));
        command.Parameters.AddWithValue("$adminEncryptedDataKey", ToDbBlob(account.AdminEncryptedDataKey));
        command.Parameters.AddWithValue("$adminDataKeyNonce", ToDbBlob(account.AdminDataKeyNonce));
        command.Parameters.AddWithValue("$adminDataKeyTag", ToDbBlob(account.AdminDataKeyTag));
        command.Parameters.AddWithValue("$databasePath", databasePath);
        command.Parameters.AddWithValue("$role", (int)account.Role);
        command.Parameters.AddWithValue("$isEnabled", account.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", account.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", account.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastLoginAt", ToDbDate(account.LastLoginAt));
        command.Parameters.AddWithValue("$metadataMac", account.MetadataMac);
    }

    private static void ValidateAccountSecurityMaterial(LocalAccount account, string accountId, string adminOwnerAccountId)
    {
        if (!Enum.IsDefined(account.Role))
        {
            throw new InvalidOperationException("账号角色无效。");
        }

        ValidateHashParameters(account.PasswordHash, account.PasswordSalt, account.PasswordIterations, "主密码");
        ValidateHashParameters(account.PinHash, account.PinSalt, account.PinIterations, "PIN");
        ValidateCredentialFormatVersion(account.PasswordKeyVersion, "主密码");
        ValidateCredentialFormatVersion(account.PinHashVersion, "PIN");
        ValidateCredentialFormatVersion(account.RecoveryKeyVersion, "Recovery Key");
        ValidateWrappedDataKey(account.EncryptedDataKey, account.DataKeyNonce, account.DataKeyTag, "主密码");

        var hasRecoveryMaterial = HasBytes(account.RecoveryKeyHash)
            || HasBytes(account.RecoveryKeySalt)
            || account.RecoveryKeyIterations > 0
            || HasBytes(account.RecoveryEncryptedDataKey)
            || HasBytes(account.RecoveryDataKeyNonce)
            || HasBytes(account.RecoveryDataKeyTag);
        if (hasRecoveryMaterial)
        {
            ValidateHashParameters(account.RecoveryKeyHash, account.RecoveryKeySalt, account.RecoveryKeyIterations, "Recovery Key");
            ValidateWrappedDataKey(
                account.RecoveryEncryptedDataKey,
                account.RecoveryDataKeyNonce,
                account.RecoveryDataKeyTag,
                "Recovery Key");
        }

        var hasAdminWrappedKey = HasBytes(account.AdminEncryptedDataKey)
            || HasBytes(account.AdminDataKeyNonce)
            || HasBytes(account.AdminDataKeyTag);
        if (account.Role == LocalAccountRole.Owner)
        {
            if (!string.IsNullOrEmpty(adminOwnerAccountId) || hasAdminWrappedKey)
            {
                throw new InvalidOperationException("所有者账号不能包含成员管理密钥。");
            }

            return;
        }

        if (string.IsNullOrEmpty(adminOwnerAccountId) || string.Equals(adminOwnerAccountId, accountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("成员账号缺少有效的所有者标识。");
        }

        ValidateWrappedDataKey(
            account.AdminEncryptedDataKey,
            account.AdminDataKeyNonce,
            account.AdminDataKeyTag,
            "成员管理");
    }

    private string NormalizeAccountDatabasePath(string accountId, string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)
            || databasePath.Length > 4096
            || databasePath.Any(char.IsControl)
            || !Path.IsPathFullyQualified(databasePath))
        {
            throw new InvalidOperationException("账号数据库路径无效。");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(databasePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("账号数据库路径无效。", ex);
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("账号数据库路径必须使用 .db 扩展名。");
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(normalizedPath, "账号数据库文件");

        var launcherPath = Path.GetFullPath(_connectionFactory.DatabasePath);
        var expectedLauncherPath = Path.GetFullPath(DatabasePaths.GetLauncherDatabasePath());
        if (string.Equals(launcherPath, expectedLauncherPath, StringComparison.OrdinalIgnoreCase)
            && !DatabasePaths.IsExpectedAccountDatabasePath(accountId, normalizedPath))
        {
            throw new InvalidOperationException("账号数据库路径不属于当前账号。");
        }

        return normalizedPath;
    }

    private static void ValidateHashParameters(byte[]? hash, byte[]? salt, int iterations, string fieldName)
    {
        if (!LocalCredentialSecurity.HasUsableHashParameters(salt, iterations, hash))
        {
            throw new InvalidOperationException($"{fieldName}哈希参数无效。");
        }
    }

    private static void ValidateWrappedDataKey(byte[]? ciphertext, byte[]? nonce, byte[]? tag, string fieldName)
    {
        if (!LocalCredentialSecurity.HasUsableWrappedDataKey(ciphertext, nonce, tag))
        {
            throw new InvalidOperationException($"{fieldName}数据密钥包裹信息无效。");
        }
    }

    private static void ValidateCredentialFormatVersion(int version, string fieldName)
    {
        if (version is < LocalCredentialSecurity.LegacyCredentialFormatVersion or > LocalCredentialSecurity.CurrentCredentialFormatVersion)
        {
            throw new InvalidOperationException($"{fieldName}凭据版本无效。");
        }
    }

    private static void NormalizeSecurityVersions(LocalAccount account)
    {
        account.PasswordKeyVersion = NormalizeCredentialFormatVersion(account.PasswordKeyVersion);
        account.PinHashVersion = NormalizeCredentialFormatVersion(account.PinHashVersion);
        account.RecoveryKeyVersion = NormalizeCredentialFormatVersion(account.RecoveryKeyVersion);
    }

    private static int NormalizeCredentialFormatVersion(int version)
    {
        return version <= 0 ? LocalCredentialSecurity.LegacyCredentialFormatVersion : version;
    }

    private static bool HasBytes(byte[]? value)
    {
        return value is { Length: > 0 };
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }

    private static object ToDbBlob(byte[]? value)
    {
        return value is null or { Length: 0 } ? DBNull.Value : value;
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
