namespace Orderly.Data.Sqlite;

public static class DatabasePaths
{
    public static string GetAppRootPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orderly-SN");

        Directory.CreateDirectory(root);
        return root;
    }

    public static string GetLegacyDatabasePath()
    {
        return Path.Combine(GetAppRootPath(), "orderly.db");
    }

    public static string GetDefaultDatabasePath()
    {
        var qaOverridePath = Environment.GetEnvironmentVariable("ORDERLY_QA_DB_PATH");
        if (!string.IsNullOrWhiteSpace(qaOverridePath))
        {
            var fullPath = Path.GetFullPath(qaOverridePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }

        return GetLegacyDatabasePath();
    }

    public static string GetIdentityDirectoryPath()
    {
        var identityPath = Path.Combine(GetAppRootPath(), "identity");
        Directory.CreateDirectory(identityPath);
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
        return accountsPath;
    }

    public static string GetAccountDirectoryPath(string accountId)
    {
        var normalizedAccountId = NormalizeAccountIdSegment(accountId);
        var accountPath = Path.Combine(GetAccountsDirectoryPath(), normalizedAccountId);
        Directory.CreateDirectory(accountPath);
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
