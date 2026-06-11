using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
    private const int MaxLauncherCredentialHashBytes = 32;
    private const int MaxLauncherCredentialSaltBytes = 64;
    private const int MaxLauncherWrappedKeyBytes = 32;
    private const int MaxLauncherNonceBytes = 12;
    private const int MaxLauncherTagBytes = 16;

    private async Task RestoreLauncherSnapshotAsync(BackupManifest manifest, CancellationToken cancellationToken)
    {
        if (_launcherConnectionFactory is null)
        {
            return;
        }

        if (!manifest.Tables.TryGetValue(LauncherLocalAccountsTableName, out var launcherTableElement))
        {
            return;
        }

        if (launcherTableElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 的备份结构无效。");
        }

        List<LauncherAccountBackupRow>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<LauncherAccountBackupRow>>(launcherTableElement.GetRawText());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 解析失败：{ex.Message}");
        }

        if (rows is null || rows.Count == 0)
        {
            return;
        }

        if (rows.Count > 1)
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 包含多条账号快照，不允许恢复。");
        }

        var row = rows[0];
        ValidateLauncherSnapshotRow(row);

        var currentSessionAccountId = _sessionContextService?.Current?.AccountId;
        if (!string.IsNullOrWhiteSpace(currentSessionAccountId)
            && !string.Equals(currentSessionAccountId, row.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份中的账号标识与当前会话账号不一致，已拒绝恢复。");
        }

        var restoredDatabasePath = _connectionFactory.DatabasePath;

        await using var connection = _launcherConnectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
            )
            ON CONFLICT(AccountId) DO UPDATE SET
                Username = excluded.Username,
                DisplayName = excluded.DisplayName,
                PasswordHash = excluded.PasswordHash,
                PasswordSalt = excluded.PasswordSalt,
                PasswordIterations = excluded.PasswordIterations,
                PinHash = excluded.PinHash,
                PinSalt = excluded.PinSalt,
                PinIterations = excluded.PinIterations,
                RecoveryKeyHash = excluded.RecoveryKeyHash,
                RecoveryKeySalt = excluded.RecoveryKeySalt,
                RecoveryKeyIterations = excluded.RecoveryKeyIterations,
                RecoveryEncryptedDataKey = excluded.RecoveryEncryptedDataKey,
                RecoveryDataKeyNonce = excluded.RecoveryDataKeyNonce,
                RecoveryDataKeyTag = excluded.RecoveryDataKeyTag,
                EncryptedDataKey = excluded.EncryptedDataKey,
                DataKeyNonce = excluded.DataKeyNonce,
                DataKeyTag = excluded.DataKeyTag,
                AdminOwnerAccountId = excluded.AdminOwnerAccountId,
                AdminEncryptedDataKey = excluded.AdminEncryptedDataKey,
                AdminDataKeyNonce = excluded.AdminDataKeyNonce,
                AdminDataKeyTag = excluded.AdminDataKeyTag,
                DatabasePath = excluded.DatabasePath,
                Role = excluded.Role,
                IsEnabled = excluded.IsEnabled,
                CreatedAt = excluded.CreatedAt,
                UpdatedAt = excluded.UpdatedAt,
                LastLoginAt = excluded.LastLoginAt;
            """;
        command.Parameters.AddWithValue("$accountId", row.AccountId);
        command.Parameters.AddWithValue("$username", row.Username);
        command.Parameters.AddWithValue("$displayName", row.DisplayName);
        var passwordHash = FromBase64(row.PasswordHash, "PasswordHash", MaxLauncherCredentialHashBytes);
        var passwordSalt = FromBase64(row.PasswordSalt, "PasswordSalt", MaxLauncherCredentialSaltBytes);
        var pinHash = FromBase64(row.PinHash, "PinHash", MaxLauncherCredentialHashBytes);
        var pinSalt = FromBase64(row.PinSalt, "PinSalt", MaxLauncherCredentialSaltBytes);
        var recoveryKeyHash = FromBase64OrEmpty(row.RecoveryKeyHash, "RecoveryKeyHash", MaxLauncherCredentialHashBytes);
        var recoveryKeySalt = FromBase64OrEmpty(row.RecoveryKeySalt, "RecoveryKeySalt", MaxLauncherCredentialSaltBytes);
        var recoveryEncryptedDataKey = FromBase64OrEmpty(row.RecoveryEncryptedDataKey, "RecoveryEncryptedDataKey", MaxLauncherWrappedKeyBytes);
        var recoveryDataKeyNonce = FromBase64OrEmpty(row.RecoveryDataKeyNonce, "RecoveryDataKeyNonce", MaxLauncherNonceBytes);
        var recoveryDataKeyTag = FromBase64OrEmpty(row.RecoveryDataKeyTag, "RecoveryDataKeyTag", MaxLauncherTagBytes);
        var encryptedDataKey = FromBase64(row.EncryptedDataKey, "EncryptedDataKey", MaxLauncherWrappedKeyBytes);
        var dataKeyNonce = FromBase64(row.DataKeyNonce, "DataKeyNonce", MaxLauncherNonceBytes);
        var dataKeyTag = FromBase64(row.DataKeyTag, "DataKeyTag", MaxLauncherTagBytes);
        var adminEncryptedDataKey = FromBase64OrEmpty(row.AdminEncryptedDataKey, "AdminEncryptedDataKey", MaxLauncherWrappedKeyBytes);
        var adminDataKeyNonce = FromBase64OrEmpty(row.AdminDataKeyNonce, "AdminDataKeyNonce", MaxLauncherNonceBytes);
        var adminDataKeyTag = FromBase64OrEmpty(row.AdminDataKeyTag, "AdminDataKeyTag", MaxLauncherTagBytes);
        ValidateLauncherSnapshotCredentialFields(
            row,
            passwordHash,
            passwordSalt,
            pinHash,
            pinSalt,
            recoveryKeyHash,
            recoveryKeySalt,
            recoveryEncryptedDataKey,
            recoveryDataKeyNonce,
            recoveryDataKeyTag,
            encryptedDataKey,
            dataKeyNonce,
            dataKeyTag,
            adminEncryptedDataKey,
            adminDataKeyNonce,
            adminDataKeyTag);

        try
        {
            command.Parameters.AddWithValue("$passwordHash", passwordHash);
            command.Parameters.AddWithValue("$passwordSalt", passwordSalt);
            command.Parameters.AddWithValue("$passwordIterations", row.PasswordIterations);
            command.Parameters.AddWithValue("$pinHash", pinHash);
            command.Parameters.AddWithValue("$pinSalt", pinSalt);
            command.Parameters.AddWithValue("$pinIterations", row.PinIterations);
            command.Parameters.AddWithValue("$recoveryKeyHash", ToDbBlob(recoveryKeyHash));
            command.Parameters.AddWithValue("$recoveryKeySalt", ToDbBlob(recoveryKeySalt));
            command.Parameters.AddWithValue("$recoveryKeyIterations", row.RecoveryKeyIterations is null ? DBNull.Value : row.RecoveryKeyIterations.Value);
            command.Parameters.AddWithValue("$recoveryEncryptedDataKey", ToDbBlob(recoveryEncryptedDataKey));
            command.Parameters.AddWithValue("$recoveryDataKeyNonce", ToDbBlob(recoveryDataKeyNonce));
            command.Parameters.AddWithValue("$recoveryDataKeyTag", ToDbBlob(recoveryDataKeyTag));
            command.Parameters.AddWithValue("$encryptedDataKey", encryptedDataKey);
            command.Parameters.AddWithValue("$dataKeyNonce", dataKeyNonce);
            command.Parameters.AddWithValue("$dataKeyTag", dataKeyTag);
            command.Parameters.AddWithValue("$adminOwnerAccountId", string.IsNullOrWhiteSpace(row.AdminOwnerAccountId) ? DBNull.Value : row.AdminOwnerAccountId);
            command.Parameters.AddWithValue("$adminEncryptedDataKey", ToDbBlob(adminEncryptedDataKey));
            command.Parameters.AddWithValue("$adminDataKeyNonce", ToDbBlob(adminDataKeyNonce));
            command.Parameters.AddWithValue("$adminDataKeyTag", ToDbBlob(adminDataKeyTag));
            command.Parameters.AddWithValue("$databasePath", restoredDatabasePath);
            command.Parameters.AddWithValue("$role", row.Role);
            command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", row.CreatedAt);
            command.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
            command.Parameters.AddWithValue("$lastLoginAt", string.IsNullOrWhiteSpace(row.LastLoginAt) ? DBNull.Value : row.LastLoginAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(
                passwordHash,
                passwordSalt,
                pinHash,
                pinSalt,
                recoveryKeyHash,
                recoveryKeySalt,
                recoveryEncryptedDataKey,
                recoveryDataKeyNonce,
                recoveryDataKeyTag,
                encryptedDataKey,
                dataKeyNonce,
                dataKeyTag,
                adminEncryptedDataKey,
                adminDataKeyNonce,
                adminDataKeyTag);
        }
    }

    private static void ValidateLauncherSnapshotRow(LauncherAccountBackupRow row)
    {
        row.AccountId = LocalCredentialSecurity.NormalizeAccountId(row.AccountId);
        row.Username = LocalCredentialSecurity.NormalizeAccountUsername(row.Username);
        row.DisplayName = LocalCredentialSecurity.NormalizeAccountDisplayName(row.DisplayName, row.Username);
        if (!string.IsNullOrWhiteSpace(row.AdminOwnerAccountId))
        {
            row.AdminOwnerAccountId = LocalCredentialSecurity.NormalizeAccountId(row.AdminOwnerAccountId);
        }

        if (string.IsNullOrWhiteSpace(row.AccountId)
            || string.IsNullOrWhiteSpace(row.Username)
            || string.IsNullOrWhiteSpace(row.DisplayName)
            || string.IsNullOrWhiteSpace(row.PasswordHash)
            || string.IsNullOrWhiteSpace(row.PasswordSalt)
            || string.IsNullOrWhiteSpace(row.PinHash)
            || string.IsNullOrWhiteSpace(row.PinSalt)
            || string.IsNullOrWhiteSpace(row.EncryptedDataKey)
            || string.IsNullOrWhiteSpace(row.DataKeyNonce)
            || string.IsNullOrWhiteSpace(row.DataKeyTag)
            || string.IsNullOrWhiteSpace(row.CreatedAt)
            || string.IsNullOrWhiteSpace(row.UpdatedAt))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在缺失关键字段的账号快照。");
        }

        ValidateLauncherSnapshotDate(row.CreatedAt, "CreatedAt");
        ValidateLauncherSnapshotDate(row.UpdatedAt, "UpdatedAt");
        if (!string.IsNullOrWhiteSpace(row.LastLoginAt))
        {
            ValidateLauncherSnapshotDate(row.LastLoginAt, "LastLoginAt");
        }
    }

    private static void ValidateLauncherSnapshotDate(string value, string fieldName)
    {
        if (!DateTimeOffset.TryParse(value, out _))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 字段 {fieldName} 的时间格式无效。");
        }
    }

    private static void ValidateLauncherSnapshotCredentialFields(
        LauncherAccountBackupRow row,
        byte[] passwordHash,
        byte[] passwordSalt,
        byte[] pinHash,
        byte[] pinSalt,
        byte[] recoveryKeyHash,
        byte[] recoveryKeySalt,
        byte[] recoveryEncryptedDataKey,
        byte[] recoveryDataKeyNonce,
        byte[] recoveryDataKeyTag,
        byte[] encryptedDataKey,
        byte[] dataKeyNonce,
        byte[] dataKeyTag,
        byte[] adminEncryptedDataKey,
        byte[] adminDataKeyNonce,
        byte[] adminDataKeyTag)
    {
        if (!LocalCredentialSecurity.HasUsableHashParameters(passwordSalt, row.PasswordIterations, passwordHash)
            || !LocalCredentialSecurity.HasUsableHashParameters(pinSalt, row.PinIterations, pinHash))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在不安全或无效的账号凭据哈希参数。");
        }

        if (!LocalCredentialSecurity.HasUsableWrappedDataKey(encryptedDataKey, dataKeyNonce, dataKeyTag))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在无效的数据密钥包裹字段。");
        }

        if (!Enum.IsDefined(typeof(LocalAccountRole), row.Role))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在无效的账号角色。");
        }

        ValidateLauncherSnapshotRecoveryFields(
            row,
            recoveryKeyHash,
            recoveryKeySalt,
            recoveryEncryptedDataKey,
            recoveryDataKeyNonce,
            recoveryDataKeyTag);

        ValidateLauncherSnapshotAdminKeyFields(
            row,
            adminEncryptedDataKey,
            adminDataKeyNonce,
            adminDataKeyTag);
    }

    private static void ValidateLauncherSnapshotRecoveryFields(
        LauncherAccountBackupRow row,
        byte[] recoveryKeyHash,
        byte[] recoveryKeySalt,
        byte[] recoveryEncryptedDataKey,
        byte[] recoveryDataKeyNonce,
        byte[] recoveryDataKeyTag)
    {
        var hasAnyRecoveryField = row.RecoveryKeyIterations is not null
            || recoveryKeyHash.Length > 0
            || recoveryKeySalt.Length > 0
            || recoveryEncryptedDataKey.Length > 0
            || recoveryDataKeyNonce.Length > 0
            || recoveryDataKeyTag.Length > 0;
        if (!hasAnyRecoveryField)
        {
            return;
        }

        if (row.RecoveryKeyIterations is not { } recoveryIterations
            || !LocalCredentialSecurity.HasUsableHashParameters(recoveryKeySalt, recoveryIterations, recoveryKeyHash)
            || !LocalCredentialSecurity.HasUsableWrappedDataKey(recoveryEncryptedDataKey, recoveryDataKeyNonce, recoveryDataKeyTag))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 存在无效的恢复密钥凭据字段。");
        }
    }

    private static void ValidateLauncherSnapshotAdminKeyFields(
        LauncherAccountBackupRow row,
        byte[] adminEncryptedDataKey,
        byte[] adminDataKeyNonce,
        byte[] adminDataKeyTag)
    {
        var role = (LocalAccountRole)row.Role;
        var hasAnyAdminKeyField = !string.IsNullOrWhiteSpace(row.AdminOwnerAccountId)
            || adminEncryptedDataKey.Length > 0
            || adminDataKeyNonce.Length > 0
            || adminDataKeyTag.Length > 0;

        if (role == LocalAccountRole.Owner)
        {
            if (hasAnyAdminKeyField)
            {
                throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 的 Owner 账号不允许包含管理员包裹密钥字段。");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(row.AdminOwnerAccountId)
            || !LocalCredentialSecurity.HasUsableWrappedDataKey(adminEncryptedDataKey, adminDataKeyNonce, adminDataKeyTag))
        {
            throw new InvalidOperationException($"表 {LauncherLocalAccountsTableName} 的 Member 账号缺少可用的管理员包裹密钥字段。");
        }
    }

    private static byte[] FromBase64OrEmpty(string? base64, string fieldName, int maxDecodedBytes)
    {
        return string.IsNullOrWhiteSpace(base64) ? [] : FromBase64(base64, fieldName, maxDecodedBytes);
    }

    private static object ToDbBlob(byte[] value)
    {
        return value.Length == 0 ? DBNull.Value : value;
    }
}
