namespace Orderly.Core.Models;

public sealed class LocalAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte[] PasswordHash { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public int PasswordIterations { get; set; }
    public int PasswordKeyVersion { get; set; } = 1;
    public byte[] PinHash { get; set; } = [];
    public byte[] PinSalt { get; set; } = [];
    public int PinIterations { get; set; }
    public int PinHashVersion { get; set; } = 1;
    public byte[] RecoveryKeyHash { get; set; } = [];
    public byte[] RecoveryKeySalt { get; set; } = [];
    public int RecoveryKeyIterations { get; set; }
    public int RecoveryKeyVersion { get; set; } = 1;
    public byte[] RecoveryEncryptedDataKey { get; set; } = [];
    public byte[] RecoveryDataKeyNonce { get; set; } = [];
    public byte[] RecoveryDataKeyTag { get; set; } = [];
    public byte[] EncryptedDataKey { get; set; } = [];
    public byte[] DataKeyNonce { get; set; } = [];
    public byte[] DataKeyTag { get; set; } = [];
    public string AdminOwnerAccountId { get; set; } = string.Empty;
    public byte[] AdminEncryptedDataKey { get; set; } = [];
    public byte[] AdminDataKeyNonce { get; set; } = [];
    public byte[] AdminDataKeyTag { get; set; } = [];
    public string DatabasePath { get; set; } = string.Empty;
    public LocalAccountRole Role { get; set; } = LocalAccountRole.Member;
    public bool IsEnabled { get; set; } = true;
    public bool QuickLoginEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
    public byte[] MetadataMac { get; set; } = [];
}
