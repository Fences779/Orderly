namespace Orderly.Data.Sqlite;

public static class DatabasePaths
{
    public static string GetAppRootPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orderly-SN");

        Directory.CreateDirectory(root);
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
            LocalDataFileSecurity.HardenDirectory(directory);
        }

        databasePath = fullPath;
        return true;
    }

    public static string GetIdentityDirectoryPath()
    {
        var identityPath = Path.Combine(GetAppRootPath(), "identity");
        Directory.CreateDirectory(identityPath);
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
        LocalDataFileSecurity.HardenDirectory(accountsPath);
        return accountsPath;
    }

    public static string GetAccountDirectoryPath(string accountId)
    {
        var normalizedAccountId = NormalizeAccountIdSegment(accountId);
        var accountPath = Path.Combine(GetAccountsDirectoryPath(), normalizedAccountId);
        Directory.CreateDirectory(accountPath);
        LocalDataFileSecurity.HardenDirectory(accountPath);
        return accountPath;
    }

    public static string GetAccountDatabasePath(string accountId)
    {
        return Path.Combine(GetAccountDirectoryPath(accountId), "orderly.db");
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
