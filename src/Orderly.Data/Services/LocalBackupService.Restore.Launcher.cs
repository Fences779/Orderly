using Orderly.Core.Models;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
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
        command.Parameters.AddWithValue("$passwordHash", FromBase64(row.PasswordHash, "PasswordHash"));
        command.Parameters.AddWithValue("$passwordSalt", FromBase64(row.PasswordSalt, "PasswordSalt"));
        command.Parameters.AddWithValue("$passwordIterations", row.PasswordIterations);
        command.Parameters.AddWithValue("$pinHash", FromBase64(row.PinHash, "PinHash"));
        command.Parameters.AddWithValue("$pinSalt", FromBase64(row.PinSalt, "PinSalt"));
        command.Parameters.AddWithValue("$pinIterations", row.PinIterations);
        command.Parameters.AddWithValue("$recoveryKeyHash", ToDbBlobFromBase64(row.RecoveryKeyHash));
        command.Parameters.AddWithValue("$recoveryKeySalt", ToDbBlobFromBase64(row.RecoveryKeySalt));
        command.Parameters.AddWithValue("$recoveryKeyIterations", row.RecoveryKeyIterations is null ? DBNull.Value : row.RecoveryKeyIterations.Value);
        command.Parameters.AddWithValue("$recoveryEncryptedDataKey", ToDbBlobFromBase64(row.RecoveryEncryptedDataKey));
        command.Parameters.AddWithValue("$recoveryDataKeyNonce", ToDbBlobFromBase64(row.RecoveryDataKeyNonce));
        command.Parameters.AddWithValue("$recoveryDataKeyTag", ToDbBlobFromBase64(row.RecoveryDataKeyTag));
        command.Parameters.AddWithValue("$encryptedDataKey", FromBase64(row.EncryptedDataKey, "EncryptedDataKey"));
        command.Parameters.AddWithValue("$dataKeyNonce", FromBase64(row.DataKeyNonce, "DataKeyNonce"));
        command.Parameters.AddWithValue("$dataKeyTag", FromBase64(row.DataKeyTag, "DataKeyTag"));
        command.Parameters.AddWithValue("$adminOwnerAccountId", string.IsNullOrWhiteSpace(row.AdminOwnerAccountId) ? DBNull.Value : row.AdminOwnerAccountId);
        command.Parameters.AddWithValue("$adminEncryptedDataKey", ToDbBlobFromBase64(row.AdminEncryptedDataKey));
        command.Parameters.AddWithValue("$adminDataKeyNonce", ToDbBlobFromBase64(row.AdminDataKeyNonce));
        command.Parameters.AddWithValue("$adminDataKeyTag", ToDbBlobFromBase64(row.AdminDataKeyTag));
        command.Parameters.AddWithValue("$databasePath", restoredDatabasePath);
        command.Parameters.AddWithValue("$role", row.Role);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", row.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", row.UpdatedAt);
        command.Parameters.AddWithValue("$lastLoginAt", string.IsNullOrWhiteSpace(row.LastLoginAt) ? DBNull.Value : row.LastLoginAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateLauncherSnapshotRow(LauncherAccountBackupRow row)
    {
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
    }
}
