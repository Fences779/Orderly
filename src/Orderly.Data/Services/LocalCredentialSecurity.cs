using System.Security.Cryptography;

namespace Orderly.Data.Services;

internal static class LocalCredentialSecurity
{
    internal const int DefaultPasswordIterations = 200000;
    internal const int DefaultPinIterations = 200000;
    internal const int DefaultRecoveryIterations = 200000;

    private const int SaltByteLength = 16;
    private const int HashByteLength = 32;
    private const int DataKeyByteLength = 32;
    private const int NonceByteLength = 12;
    private const int TagByteLength = 16;

    internal static bool IsValidPin(string pin)
    {
        return pin.Length == 6 && pin.All(char.IsDigit);
    }

    internal static string GenerateRecoveryKey()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return string.Join("-", Enumerable.Range(0, raw.Length / 4).Select(index => raw.Substring(index * 4, 4)));
    }

    internal static string NormalizeRecoveryKey(string recoveryKey)
    {
        return recoveryKey.Trim().ToUpperInvariant();
    }

    internal static byte[] ComputeHash(string value, byte[] salt, int iterations)
    {
        EnsureUsableHashParameters(salt, iterations, expectedHash: null);
        return Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, HashByteLength);
    }

    internal static bool VerifyHash(string value, byte[] salt, int iterations, byte[] expectedHash)
    {
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
        return salt is { Length: >= SaltByteLength }
            && iterations >= DefaultPasswordIterations
            && (expectedHash is null || expectedHash.Length == HashByteLength);
    }

    internal static bool HasUsableWrappedDataKey(byte[]? ciphertext, byte[]? nonce, byte[]? tag)
    {
        return ciphertext is { Length: DataKeyByteLength }
            && nonce is { Length: NonceByteLength }
            && tag is { Length: TagByteLength };
    }

    internal static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKey(string secret, byte[] salt, int iterations, byte[] dataKey)
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

    internal static byte[] UnwrapDataKey(string secret, byte[] salt, int iterations, byte[] ciphertext, byte[] nonce, byte[] tag)
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
