using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class LocalAccountManagementService
{
    private LocalSessionContext RequireCurrentSession()
    {
        return _sessionContextService.Current ?? throw new InvalidOperationException("当前没有已登录会话。");
    }

    private LocalSessionContext RequireOwnerSession()
    {
        var session = RequireCurrentSession();
        if (session.Role != LocalAccountRole.Owner)
        {
            throw new UnauthorizedAccessException("仅 Owner 允许执行此操作。");
        }

        return session;
    }

    private async Task<LocalAccount> GetAccountRequiredAsync(string accountId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new InvalidOperationException("账号标识不能为空。");
        }

        var account = await _accountRepository.GetByAccountIdAsync(accountId.Trim(), cancellationToken);
        return account ?? throw new InvalidOperationException("目标账号不存在。");
    }

    private static LocalAccountSummary MapSummary(LocalAccount account)
    {
        return MapSummary(account, isMostRecentlyLoggedIn: false);
    }

    private static LocalAccountSummary MapSummary(LocalAccount account, bool isMostRecentlyLoggedIn)
    {
        return new LocalAccountSummary
        {
            AccountId = account.AccountId,
            Username = account.Username,
            DisplayName = account.DisplayName,
            Role = account.Role,
            IsEnabled = account.IsEnabled,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
            IsMostRecentlyLoggedIn = isMostRecentlyLoggedIn
        };
    }

    private static IReadOnlyList<LocalAccountSummary> MapSummaries(IEnumerable<LocalAccount> accounts)
    {
        var orderedAccounts = accounts
            .OrderBy(account => account.CreatedAt)
            .ToList();
        var mostRecentlyLoggedInAccountId = orderedAccounts
            .Where(account => account.LastLoginAt.HasValue)
            .OrderByDescending(account => account.LastLoginAt)
            .ThenByDescending(account => account.UpdatedAt)
            .Select(account => account.AccountId)
            .FirstOrDefault();

        return orderedAccounts
            .Select(account => MapSummary(
                account,
                !string.IsNullOrWhiteSpace(mostRecentlyLoggedInAccountId)
                && string.Equals(account.AccountId, mostRecentlyLoggedInAccountId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static bool IsValidPin(string pin)
    {
        return pin.Length == 6 && pin.All(char.IsDigit);
    }

    private static byte[] ComputeHash(string value, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(value, salt, iterations, HashAlgorithmName.SHA256, 32);
    }

    private static bool VerifyHash(string value, byte[] salt, int iterations, byte[] expectedHash)
    {
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

    private static void DeleteAccountWorkspace(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        var accountsRoot = Path.GetFullPath(DatabasePaths.GetAccountsDirectoryPath());
        var targetDirectory = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(targetDirectory)
            || !IsPathInsideDirectory(targetDirectory, accountsRoot)
            || ContainsReparsePoint(targetDirectory)
            || !Directory.Exists(targetDirectory))
        {
            return;
        }

        Directory.Delete(targetDirectory, recursive: true);
    }

    private static bool IsPathInsideDirectory(string targetDirectory, string rootDirectory)
    {
        var relative = Path.GetRelativePath(rootDirectory, targetDirectory);
        return !string.IsNullOrWhiteSpace(relative)
            && relative != "."
            && !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static bool ContainsReparsePoint(string directory)
    {
        try
        {
            if (HasReparsePoint(directory))
            {
                return true;
            }

            var options = new EnumerationOptions
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
                RecurseSubdirectories = false
            };

            foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", options))
            {
                if (ContainsReparsePoint(subdirectory))
                {
                    return true;
                }
            }

            return Directory.EnumerateFiles(directory, "*", options).Any(HasReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool HasReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKey(string secret, byte[] salt, int iterations, byte[] dataKey)
    {
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

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKeyWithKey(byte[] key, byte[] dataKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, dataKey, ciphertext, tag);
        return (ciphertext, nonce, tag);
    }

    private static byte[] UnwrapDataKey(string secret, byte[] salt, int iterations, byte[] ciphertext, byte[] nonce, byte[] tag)
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

    private static byte[] UnwrapDataKeyWithKey(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        if (ciphertext.Length == 0 || nonce.Length == 0 || tag.Length == 0)
        {
            throw new InvalidOperationException("账号缺少可用的数据密钥包裹信息。");
        }

        var dataKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, dataKey);
        return dataKey;
    }
}
