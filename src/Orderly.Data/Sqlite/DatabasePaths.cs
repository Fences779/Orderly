namespace Orderly.Data.Sqlite;

public static class DatabasePaths
{
    public static string GetAppRootPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orderly-SN");

        Directory.CreateDirectory(root);
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(root, "应用数据目录");
        LocalDataFileSecurity.HardenDirectory(root);
        return root;
    }

    public static string GetLegacyDatabasePath()
    {
        return Path.Combine(GetAppRootPath(), "orderly.db");
    }

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
            Directory.CreateDirectory(directory);
            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directory, "QA 数据库目录");
            LocalDataFileSecurity.HardenDirectory(directory);
        }

        databasePath = fullPath;
        return true;
    }

    public static string GetIdentityDirectoryPath()
    {
        var identityPath = Path.Combine(GetAppRootPath(), "identity");
        Directory.CreateDirectory(identityPath);
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(identityPath, "身份数据目录");
        LocalDataFileSecurity.HardenDirectory(identityPath);
        return identityPath;
    }

    public static string GetLauncherDatabasePath()
    {
        return Path.Combine(GetIdentityDirectoryPath(), "launcher.db");
    }

    public static string GetAccountsDirectoryPath()
    {
        var accountsPath = Path.Combine(GetAppRootPath(), "accounts");
        Directory.CreateDirectory(accountsPath);
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(accountsPath, "账号数据目录");
        LocalDataFileSecurity.HardenDirectory(accountsPath);
        return accountsPath;
    }

    public static string GetAccountDirectoryPath(string accountId)
    {
        var accountPath = GetExpectedAccountDirectoryPath(accountId);
        Directory.CreateDirectory(accountPath);
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(accountPath, "账号工作区目录");
        LocalDataFileSecurity.HardenDirectory(accountPath);
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
