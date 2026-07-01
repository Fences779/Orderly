using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

internal static class LocalAccountDatabasePathRepair
{
    public static bool IsSafe(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            if (!DatabasePaths.IsExpectedAccountDatabasePath(account.AccountId, account.DatabasePath)
                || LocalDataFileSecurity.IsReparsePoint(account.DatabasePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(account.DatabasePath));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "账号数据目录");
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task<bool> TryRepairAsync(
        ILocalAccountRepository repository,
        LocalAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(account);

        if (IsSafe(account))
        {
            return true;
        }

        if (!TryGetRepairablePath(account.AccountId, account.DatabasePath, out var repairedPath))
        {
            return false;
        }

        var originalPath = account.DatabasePath;
        account.DatabasePath = repairedPath;
        try
        {
            await repository.UpdateAsync(account, cancellationToken);
            return IsSafe(account);
        }
        catch
        {
            account.DatabasePath = originalPath;
            throw;
        }
    }

    private static bool TryGetRepairablePath(string accountId, string? databasePath, out string repairedPath)
    {
        repairedPath = string.Empty;
        if (!TryNormalizeFullPath(databasePath, out var normalizedDatabasePath))
        {
            return false;
        }

        if (!TryNormalizeFullPath(DatabasePaths.GetLegacyDatabasePath(), out var normalizedLegacyPath)
            || !string.Equals(normalizedDatabasePath, normalizedLegacyPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryNormalizeFullPath(DatabasePaths.GetExpectedAccountDatabasePath(accountId), out repairedPath)
            || !File.Exists(repairedPath)
            || LocalDataFileSecurity.IsReparsePoint(repairedPath))
        {
            repairedPath = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeFullPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }
}
