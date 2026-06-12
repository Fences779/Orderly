using System.Security.Cryptography;
using System.Runtime.Versioning;

namespace Orderly.Data.Sqlite;

/// <summary>
/// 启动器数据库（launcher.db）的 SQLCipher 主密钥存储。
/// 启动器库必须在登录前打开，无法使用会话数据密钥，因此使用 Windows DPAPI（CurrentUser）
/// 保护的本机 32 字节随机密钥。密钥文件经反链接校验与 ACL 加固。
/// </summary>
public static class LauncherDatabaseKeyStore
{
    private const string KeyFileName = "launcher-db.key";
    private const string LegacyRawKeyFileName = "launcher-db.key.raw";
    private const int KeyByteLength = 32;
    private const int MaxProtectedKeyBytes = 4096;
    private static readonly byte[] ProtectionEntropy =
        SHA256.HashData("Orderly.LauncherDatabase.SqlCipherKey.v1"u8);

    private static readonly object SyncRoot = new();

    /// <summary>
    /// 返回启动器数据库密钥的「副本」（调用方/使用方负责清零）。不存在则创建。
    /// </summary>
    public static byte[] GetOrCreateKeyCopy()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("启动器数据库加密密钥保护需要 Windows DPAPI。");
        }

        lock (SyncRoot)
        {
            var path = GetKeyPath();
            LocalDataFileSecurity.EnsureFileIsNotLinked(path, "启动器数据库密钥文件");
            DeleteLegacyRawKeyFile();

            return File.Exists(path) ? ReadKey(path) : CreateKey(path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ReadKey(string path)
    {
        try
        {
            var protectedKey = File.ReadAllBytes(path);
            if (protectedKey.Length == 0 || protectedKey.Length > MaxProtectedKeyBytes)
            {
                throw new InvalidOperationException("启动器数据库密钥文件长度无效。");
            }

            var key = ProtectedData.Unprotect(protectedKey, ProtectionEntropy, DataProtectionScope.CurrentUser);
            if (key.Length != KeyByteLength)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidOperationException("启动器数据库密钥长度无效。");
            }

            LocalDataFileSecurity.HardenFile(path);
            return key;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法读取启动器数据库密钥。", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] CreateKey(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "身份数据目录");
        }

        var key = RandomNumberGenerator.GetBytes(KeyByteLength);
        try
        {
            var protectedKey = ProtectedData.Protect(key, ProtectionEntropy, DataProtectionScope.CurrentUser);
            try
            {
                LocalDataFileSecurity.EnsureFileIsNotLinked(path, "启动器数据库密钥文件");
                File.WriteAllBytes(path, protectedKey);
                LocalDataFileSecurity.HardenFile(path);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }

            return key.ToArray();
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new InvalidOperationException("无法创建启动器数据库密钥。", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void DeleteLegacyRawKeyFile()
    {
        var legacyPath = Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), LegacyRawKeyFileName);
        try
        {
            if (File.Exists(legacyPath) && !LocalDataFileSecurity.IsReparsePoint(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("旧版裸启动器数据库密钥文件清理失败。", ex);
        }
    }

    private static string GetKeyPath()
    {
        return Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), KeyFileName);
    }
}
