namespace Orderly.Data.Sqlite;

public static class DatabasePaths
{
    // Neutral product application-root directory name. All local data (launcher database,
    // per-account workspace databases, identity material) lives exclusively under this single
    // directory beneath %LocalAppData% (Req 1.5). The launcher database and multi-account
    // structure are derived relative to this root, so they continue to resolve unchanged.
    private const string AppRootDirectoryName = "Orderly";

    public static string GetAppRootPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppRootDirectoryName);

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(root, "应用数据目录");
        return root;
    }

    public static string GetLegacyDatabasePath()
    {
        return Path.Combine(GetAppRootPath(), "orderly.db");
    }

    /// <summary>
    /// Neutral label used whenever the application refers to recovering local data left behind by a
    /// previous installation directory (Req 1.9). Any such recovery is treated strictly as a
    /// "legacy local data migration": existing user data is never deleted or overwritten by this
    /// module (constraint C-6). This module owns only the current application root under
    /// <see cref="AppRootDirectoryName"/>; actual migration of prior-install data is handled by the
    /// data-layer migration pipeline.
    /// </summary>
    public const string LegacyLocalDataMigrationLabel = "legacy local data migration";

    public static string GetDefaultDatabasePath(bool allowQaOverride = false)
    {
        if (allowQaOverride
            && TryGetQaOverrideDatabasePath(out var qaOverridePath))
        {
            return qaOverridePath;
        }

        return GetLegacyDatabasePath();
    }

    private static bool TryGetQaOverrideDatabasePath(out string databasePath)
    {
        databasePath = string.Empty;

        var qaOverridePath = Environment.GetEnvironmentVariable("ORDERLY_QA_DB_PATH");
        if (string.IsNullOrWhiteSpace(qaOverridePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(qaOverridePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "QA 数据库目录");
        }

        LocalDataFileSecurity.EnsureFileIsNotLinked(fullPath, "QA 数据库文件");
        databasePath = fullPath;
        return true;
    }

    public static string GetIdentityDirectoryPath()
    {
        var identityPath = Path.Combine(GetAppRootPath(), "identity");
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(identityPath, "身份数据目录");
        return identityPath;
    }

    public static string GetLauncherDatabasePath()
    {
        return Path.Combine(GetIdentityDirectoryPath(), "launcher.db");
    }

    public static string GetAccountsDirectoryPath()
    {
        var accountsPath = Path.Combine(GetAppRootPath(), "accounts");
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(accountsPath, "账号数据目录");
        return accountsPath;
    }

    public static string GetAccountDirectoryPath(string accountId)
    {
        var accountPath = GetExpectedAccountDirectoryPath(accountId);
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(accountPath, "账号工作区目录");
        return accountPath;
    }

    public static string GetAccountDatabasePath(string accountId)
    {
        return Path.Combine(GetAccountDirectoryPath(accountId), "orderly.db");
    }

    public static string GetExpectedAccountDirectoryPath(string accountId)
    {
        var normalizedAccountId = NormalizeAccountIdSegment(accountId);
        return Path.Combine(GetAccountsDirectoryPath(), normalizedAccountId);
    }

    public static string GetExpectedAccountDatabasePath(string accountId)
    {
        return Path.Combine(GetExpectedAccountDirectoryPath(accountId), "orderly.db");
    }

    public static bool IsExpectedAccountDatabasePath(string accountId, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return false;
        }

        try
        {
            var expectedPath = Path.GetFullPath(GetExpectedAccountDatabasePath(accountId));
            var actualPath = Path.GetFullPath(databasePath);
            return string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeAccountIdSegment(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("Account id cannot be empty.", nameof(accountId));
        }

        var value = accountId.Trim();
        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new ArgumentException("Account id contains unsupported characters.", nameof(accountId));
            }
        }

        return value;
    }
}
