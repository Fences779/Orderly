using System.Security.Cryptography;

namespace Orderly.Data.Services;

internal static class LocalCredentialSecurity
{
    internal const int DefaultPasswordIterations = 200000;
    internal const int DefaultPinIterations = 200000;
    internal const int DefaultRecoveryIterations = 200000;
    internal const int MaxCredentialIterations = 2_000_000;

    private const int SaltByteLength = 16;
    private const int MaxSaltByteLength = 64;
    private const int HashByteLength = 32;
    private const int DataKeyByteLength = 32;
    private const int NonceByteLength = 12;
    private const int TagByteLength = 16;
    private const int MaxSecretCharLength = 512;
    private const int MaxAccountUsernameCharLength = 64;
    private const int MaxAccountDisplayNameCharLength = 64;
    private const int MaxRecoveryKeyCharLength = 128;

    internal static bool IsValidPin(string? pin)
    {
        return pin is { Length: 6 } && pin.All(static ch => ch is >= '0' and <= '9');
    }

    internal static string NormalizeAccountUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("用户名不能为空。");
        }

        var normalized = username.Trim();
        if (normalized.Length > MaxAccountUsernameCharLength || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException($"用户名不能超过 {MaxAccountUsernameCharLength} 个字符，且不能包含控制字符。");
        }

        return normalized;
    }

    internal static string NormalizeAccountId(string? accountId)
    {
        var normalized = string.IsNullOrWhiteSpace(accountId) ? string.Empty : accountId.Trim();
        if (!Guid.TryParseExact(normalized, "N", out _))
        {
            throw new InvalidOperationException("账号标识无效。");
        }

        return normalized.ToLowerInvariant();
    }

    internal static bool TryNormalizeAccountId(string? accountId, out string normalized)
    {
        try
        {
            normalized = NormalizeAccountId(accountId);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    internal static bool TryNormalizeAccountUsername(string? username, out string normalized)
    {
        try
        {
            normalized = NormalizeAccountUsername(username);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    internal static string NormalizeAccountDisplayName(string? displayName, string fallbackUsername)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return fallbackUsername;
        }

        var normalized = displayName.Trim();
        if (normalized.Length > MaxAccountDisplayNameCharLength || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException($"显示名不能超过 {MaxAccountDisplayNameCharLength} 个字符，且不能包含控制字符。");
        }

        return normalized;
    }

    internal static string GenerateRecoveryKey()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return string.Join("-", Enumerable.Range(0, raw.Length / 4).Select(index => raw.Substring(index * 4, 4)));
    }

    internal static string NormalizeRecoveryKey(string? recoveryKey)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            throw new InvalidOperationException("Recovery Key 格式无效。");
        }

        var trimmed = recoveryKey.Trim();
        if (trimmed.Length > MaxRecoveryKeyCharLength || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException("Recovery Key 格式无效。");
        }

        var normalized = trimmed.ToUpperInvariant();
        foreach (var ch in normalized)
        {
            var isHex = ch is >= '0' and <= '9' or >= 'A' and <= 'F';
            if (!isHex && ch != '-')
            {
                throw new InvalidOperationException("Recovery Key 格式无效。");
            }
        }

        return normalized;
    }

    internal static bool TryNormalizeRecoveryKey(string? recoveryKey, out string normalized)
    {
        try
        {
            normalized = NormalizeRecoveryKey(recoveryKey);
            return true;
        }
        catch (InvalidOperationException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    internal static byte[] ComputeHash(string? value, byte[] salt, int iterations)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxSecretCharLength)
        {
            throw new InvalidOperationException("账号凭据长度无效。");
        }

        EnsureUsableHashParameters(salt, iterations, expectedHash: null);
        return Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, HashByteLength);
    }

    internal static bool VerifyHash(string? value, byte[] salt, int iterations, byte[] expectedHash)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxSecretCharLength)
        {
            return false;
        }

        if (!HasUsableHashParameters(salt, iterations, expectedHash))
        {
            return false;
        }

        var actualHash = ComputeHash(value, salt, iterations);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    internal static bool HasUsableHashParameters(byte[]? salt, int iterations, byte[]? expectedHash)
    {
        return salt is { Length: >= SaltByteLength and <= MaxSaltByteLength }
            && iterations is >= DefaultPasswordIterations and <= MaxCredentialIterations
            && (expectedHash is null || expectedHash.Length == HashByteLength);
    }

    internal static bool HasUsableWrappedDataKey(byte[]? ciphertext, byte[]? nonce, byte[]? tag)
    {
        return ciphertext is { Length: DataKeyByteLength }
            && nonce is { Length: NonceByteLength }
            && tag is { Length: TagByteLength };
    }

    internal static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKey(string? secret, byte[] salt, int iterations, byte[] dataKey)
    {
        if (dataKey.Length != DataKeyByteLength)
        {
            throw new InvalidOperationException("账号数据密钥长度无效。");
        }

        var key = ComputeHash(secret, salt, iterations);
        try
        {
            return WrapDataKeyWithKey(key, dataKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKeyWithKey(byte[] key, byte[] dataKey)
    {
        if (key.Length != HashByteLength || dataKey.Length != DataKeyByteLength)
        {
            throw new InvalidOperationException("账号密钥包裹参数无效。");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceByteLength);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagByteLength];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, dataKey, ciphertext, tag);
        return (ciphertext, nonce, tag);
    }

    internal static byte[] UnwrapDataKey(string? secret, byte[] salt, int iterations, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var key = ComputeHash(secret, salt, iterations);
        try
        {
            return UnwrapDataKeyWithKey(key, ciphertext, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static byte[] UnwrapDataKeyWithKey(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        if (key.Length != HashByteLength || !HasUsableWrappedDataKey(ciphertext, nonce, tag))
        {
            throw new InvalidOperationException("账号缺少可用的数据密钥包裹信息。");
        }

        var dataKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, dataKey);
        return dataKey;
    }

    private static void EnsureUsableHashParameters(byte[] salt, int iterations, byte[]? expectedHash)
    {
        if (!HasUsableHashParameters(salt, iterations, expectedHash))
        {
            throw new InvalidOperationException("账号凭据哈希参数无效。");
        }
    }
}
