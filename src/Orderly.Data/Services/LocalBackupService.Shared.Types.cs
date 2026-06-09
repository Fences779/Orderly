using Orderly.Core.Models;
using System.Text.Json.Serialization;

namespace Orderly.Data.Services;

public sealed partial class LocalBackupService
{
    private sealed class LauncherAccountBackupRow
    {
        [JsonPropertyName("accountId")]
        public string AccountId { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [JsonPropertyName("passwordSalt")]
        public string PasswordSalt { get; set; } = string.Empty;

        [JsonPropertyName("passwordIterations")]
        public int PasswordIterations { get; set; }

        [JsonPropertyName("pinHash")]
        public string PinHash { get; set; } = string.Empty;

        [JsonPropertyName("pinSalt")]
        public string PinSalt { get; set; } = string.Empty;

        [JsonPropertyName("pinIterations")]
        public int PinIterations { get; set; }

        [JsonPropertyName("recoveryKeyHash")]
        public string? RecoveryKeyHash { get; set; }

        [JsonPropertyName("recoveryKeySalt")]
        public string? RecoveryKeySalt { get; set; }

        [JsonPropertyName("recoveryKeyIterations")]
        public int? RecoveryKeyIterations { get; set; }

        [JsonPropertyName("recoveryEncryptedDataKey")]
        public string? RecoveryEncryptedDataKey { get; set; }

        [JsonPropertyName("recoveryDataKeyNonce")]
        public string? RecoveryDataKeyNonce { get; set; }

        [JsonPropertyName("recoveryDataKeyTag")]
        public string? RecoveryDataKeyTag { get; set; }

        [JsonPropertyName("encryptedDataKey")]
        public string EncryptedDataKey { get; set; } = string.Empty;

        [JsonPropertyName("dataKeyNonce")]
        public string DataKeyNonce { get; set; } = string.Empty;

        [JsonPropertyName("dataKeyTag")]
        public string DataKeyTag { get; set; } = string.Empty;

        [JsonPropertyName("adminOwnerAccountId")]
        public string? AdminOwnerAccountId { get; set; }

        [JsonPropertyName("adminEncryptedDataKey")]
        public string? AdminEncryptedDataKey { get; set; }

        [JsonPropertyName("adminDataKeyNonce")]
        public string? AdminDataKeyNonce { get; set; }

        [JsonPropertyName("adminDataKeyTag")]
        public string? AdminDataKeyTag { get; set; }

        [JsonPropertyName("databasePath")]
        public string DatabasePath { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public int Role { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("lastLoginAt")]
        public string? LastLoginAt { get; set; }
    }

    private sealed record TargetInspectionResult(
        BackupRestoreTargetState TargetState,
        IReadOnlyDictionary<string, int> Counts,
        IReadOnlyDictionary<string, int> QaScopedCounts)
    {
        public static TargetInspectionResult Empty()
        {
            return new(
                BackupRestoreTargetState.Unknown,
                new Dictionary<string, int>(StringComparer.Ordinal),
                new Dictionary<string, int>(StringComparer.Ordinal));
        }
    }

    private sealed record BackupIntegrityVerificationResult(
        bool HasTag,
        bool IsValid,
        string ActualTag)
    {
        public static BackupIntegrityVerificationResult Missing()
        {
            return new(false, false, string.Empty);
        }

        public static BackupIntegrityVerificationResult Invalid(string actualTag)
        {
            return new(true, false, actualTag);
        }

        public static BackupIntegrityVerificationResult FromComparison(string actualTag, bool isValid)
        {
            return new(true, isValid, actualTag);
        }
    }
}
