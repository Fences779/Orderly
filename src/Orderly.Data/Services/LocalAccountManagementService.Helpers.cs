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
        var normalizedAccountId = LocalCredentialSecurity.NormalizeAccountId(accountId);
        var account = await _accountRepository.GetByAccountIdAsync(normalizedAccountId, cancellationToken);
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

    private static string? ResolveDeletableAccountWorkspaceDirectory(string accountId, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return null;
        }

        var accountsRoot = Path.GetFullPath(DatabasePaths.GetAccountsDirectoryPath());
        if (!DatabasePaths.IsExpectedAccountDatabasePath(accountId, databasePath))
        {
            throw new InvalidOperationException("账号数据路径与账号标识不匹配，已拒绝删除工作区。");
        }

        var expectedDatabasePath = Path.GetFullPath(DatabasePaths.GetExpectedAccountDatabasePath(accountId));
        var targetDirectory = Path.GetFullPath(Path.GetDirectoryName(expectedDatabasePath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(targetDirectory)
            || !IsPathInsideDirectory(targetDirectory, accountsRoot)
            || !Directory.Exists(targetDirectory))
        {
            return null;
        }

        if (ContainsReparsePoint(targetDirectory))
        {
            throw new InvalidOperationException("账号工作区包含链接文件，已拒绝删除。");
        }

        return targetDirectory;
    }

    private static void DeleteAccountWorkspace(string? targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
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
        return LocalCredentialSecurity.WrapDataKey(secret, salt, iterations, dataKey);
    }

    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) WrapDataKeyWithKey(byte[] key, byte[] dataKey)
    {
        return LocalCredentialSecurity.WrapDataKeyWithKey(key, dataKey);
    }

    private static byte[] UnwrapDataKey(string secret, byte[] salt, int iterations, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        return LocalCredentialSecurity.UnwrapDataKey(secret, salt, iterations, ciphertext, nonce, tag);
    }

    private static byte[] UnwrapDataKeyWithKey(byte[] key, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        return LocalCredentialSecurity.UnwrapDataKeyWithKey(key, ciphertext, nonce, tag);
    }
}
