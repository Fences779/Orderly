using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class LocalAccountMetadataSecurity
{
    private const int MacByteLength = 32;

    internal static byte[] ComputeMac(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var localSecret = LocalCredentialSecretStore.GetOrCreateSecret();
        try
        {
            using var hmac = new HMACSHA256(localSecret);
            var payload = BuildCanonicalPayload(account);
            try
            {
                return hmac.ComputeHash(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localSecret);
        }
    }

    internal static void VerifyOrThrow(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.MetadataMac.Length == 0)
        {
            if (account.PasswordKeyVersion <= LocalCredentialSecurity.LegacyCredentialFormatVersion
                && account.PinHashVersion <= LocalCredentialSecurity.LegacyCredentialFormatVersion
                && account.RecoveryKeyVersion <= LocalCredentialSecurity.LegacyCredentialFormatVersion)
            {
                return;
            }

            throw new InvalidOperationException("账号安全元数据缺少完整性校验。");
        }

        if (account.MetadataMac.Length != MacByteLength)
        {
            throw new InvalidOperationException("账号安全元数据完整性校验长度无效。");
        }

        var expected = ComputeMac(account);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, account.MetadataMac)
                && !VerifyLegacyMac(account))
            {
                throw new InvalidOperationException("账号安全元数据完整性校验失败。");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static bool VerifyLegacyMac(LocalAccount account)
    {
        var localSecret = LocalCredentialSecretStore.GetOrCreateSecret();
        try
        {
            using var hmac = new HMACSHA256(localSecret);
            var payload = BuildCanonicalPayload(account, includeQuickLogin: false);
            var legacy = hmac.ComputeHash(payload);
            try
            {
                return CryptographicOperations.FixedTimeEquals(legacy, account.MetadataMac);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
                CryptographicOperations.ZeroMemory(legacy);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localSecret);
        }
    }

    private static byte[] BuildCanonicalPayload(LocalAccount account, bool includeQuickLogin = true)
    {
        using var stream = new MemoryStream();

        WriteString(stream, account.AccountId);
        WriteString(stream, account.Username);
        WriteString(stream, account.DisplayName);
        WriteBytes(stream, account.PasswordHash);
        WriteBytes(stream, account.PasswordSalt);
        WriteInt32(stream, account.PasswordIterations);
        WriteInt32(stream, account.PasswordKeyVersion);
        WriteBytes(stream, account.PinHash);
        WriteBytes(stream, account.PinSalt);
        WriteInt32(stream, account.PinIterations);
        WriteInt32(stream, account.PinHashVersion);
        WriteBytes(stream, account.RecoveryKeyHash);
        WriteBytes(stream, account.RecoveryKeySalt);
        WriteInt32(stream, account.RecoveryKeyIterations);
        WriteInt32(stream, account.RecoveryKeyVersion);
        WriteBytes(stream, account.RecoveryEncryptedDataKey);
        WriteBytes(stream, account.RecoveryDataKeyNonce);
        WriteBytes(stream, account.RecoveryDataKeyTag);
        WriteBytes(stream, account.EncryptedDataKey);
        WriteBytes(stream, account.DataKeyNonce);
        WriteBytes(stream, account.DataKeyTag);
        WriteString(stream, account.AdminOwnerAccountId);
        WriteBytes(stream, account.AdminEncryptedDataKey);
        WriteBytes(stream, account.AdminDataKeyNonce);
        WriteBytes(stream, account.AdminDataKeyTag);
        WriteString(stream, account.DatabasePath);
        WriteInt32(stream, (int)account.Role);
        WriteBool(stream, account.IsEnabled);
        if (includeQuickLogin)
        {
            WriteBool(stream, account.QuickLoginEnabled);
        }
        WriteString(stream, account.CreatedAt.ToString("O"));
        WriteString(stream, account.UpdatedAt.ToString("O"));
        WriteString(stream, account.LastLoginAt?.ToString("O") ?? string.Empty);

        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteBytes(stream, bytes);
        CryptographicOperations.ZeroMemory(bytes);
    }

    private static void WriteBytes(Stream stream, byte[]? value)
    {
        value ??= [];
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        stream.Write(length);
        stream.Write(value);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteBool(Stream stream, bool value)
    {
        stream.WriteByte(value ? (byte)1 : (byte)0);
    }
}
