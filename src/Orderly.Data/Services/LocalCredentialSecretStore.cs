using System.Security.Cryptography;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

internal static class LocalCredentialSecretStore
{
    private const string SecretFileName = "credential-local-secret.bin";
    private const int SecretByteLength = 32;

    private static readonly object SyncRoot = new();

    internal static byte[] GetOrCreateSecret()
    {
        lock (SyncRoot)
        {
            var path = GetSecretPath();
            LocalDataFileSecurity.EnsureFileIsNotLinked(path, "本地凭据密钥文件");

            if (File.Exists(path))
            {
                return ReadSecret(path);
            }

            return CreateSecret(path);
        }
    }

    private static byte[] ReadSecret(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("本地凭据密钥保护需要 Windows DPAPI。");
        }

        try
        {
            var protectedSecret = File.ReadAllBytes(path);
            var secret = ProtectedData.Unprotect(
                protectedSecret,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            if (secret.Length != SecretByteLength)
            {
                CryptographicOperations.ZeroMemory(secret);
                throw new InvalidOperationException("本地凭据密钥长度无效。");
            }

            return secret;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法读取本地凭据密钥。", ex);
        }
    }

    private static byte[] CreateSecret(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("本地凭据密钥保护需要 Windows DPAPI。");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "身份数据目录");
        }

        var secret = RandomNumberGenerator.GetBytes(SecretByteLength);
        try
        {
            var protectedSecret = ProtectedData.Protect(
                secret,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            File.WriteAllBytes(path, protectedSecret);
            LocalDataFileSecurity.HardenFile(path);
            return secret.ToArray();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法创建本地凭据密钥。", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static string GetSecretPath()
    {
        return Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), SecretFileName);
    }
}
