using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Orderly.Data.Sqlite;

public static class LocalDataFileSecurity
{
    private const string SharedDataUsersGroupName = "OrderlyDataUsers";
    private const string SharedDataAccountsEnvironmentVariableName = "ORDERLY_SHARED_DATA_ACCOUNTS";
    private static readonly string[] DefaultSharedDataAccounts = ["26911", "XinglanOps", "Xinglan"];

    public static void HardenDirectory(string directoryPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        HardenDirectoryWindows(directoryPath);
    }

    public static void EnsureDirectoryIsNotLinked(string directoryPath, string description)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            if (current.Exists && IsReparsePoint(current.FullName))
            {
                throw new InvalidOperationException($"{description}不能是链接目录，也不能位于链接目录下。");
            }

            current = current.Parent;
        }
    }

    public static void EnsureDirectoryExistsAndIsNotLinked(string directoryPath, string description)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        EnsureDirectoryIsNotLinked(directoryPath, description);
        Directory.CreateDirectory(directoryPath);
        EnsureDirectoryIsNotLinked(directoryPath, description);
        HardenDirectory(directoryPath);
    }

    public static void EnsureFileIsNotLinked(string filePath, string description)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (IsReparsePoint(filePath))
        {
            throw new InvalidOperationException($"{description}不能是链接文件。");
        }
    }

    public static void HardenSqliteDatabaseFiles(string databasePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        HardenFileWindows(databasePath);
        HardenFileWindows(databasePath + "-journal");
        HardenFileWindows(databasePath + "-wal");
        HardenFileWindows(databasePath + "-shm");
    }

    public static void HardenFile(string filePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        HardenFileWindows(filePath);
    }

    public static bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenDirectoryWindows(string directoryPath)
    {
        try
        {
            if (HasReparsePoint(directoryPath))
            {
                throw new InvalidOperationException("本地数据目录不能是链接目录。");
            }

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                throw new InvalidOperationException("无法确定当前 Windows 用户，不能加固本地数据目录。");
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleAll(rule);
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddDirectoryRule(security, currentUser);
            foreach (var sharedDataPrincipal in ResolveSharedDataPrincipals())
            {
                AddDirectoryRule(security, sharedDataPrincipal);
            }

            AddDirectoryRule(
                security,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null));
            directoryInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("无法加固本地数据目录权限。", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenFileWindows(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            if (HasReparsePoint(filePath))
            {
                throw new InvalidOperationException("本地数据文件不能是链接文件。");
            }

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                throw new InvalidOperationException("无法确定当前 Windows 用户，不能加固本地数据文件。");
            }

            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleAll(rule);
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            foreach (var sharedDataPrincipal in ResolveSharedDataPrincipals())
            {
                security.AddAccessRule(new FileSystemAccessRule(sharedDataPrincipal, FileSystemRights.FullControl, AccessControlType.Allow));
            }

            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("无法加固本地数据文件权限。", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddDirectoryRule(DirectorySecurity security, SecurityIdentifier securityIdentifier)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            securityIdentifier,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<SecurityIdentifier> ResolveSharedDataPrincipals()
    {
        var resolved = new List<SecurityIdentifier>();
        TryAddResolvedPrincipal(resolved, SharedDataUsersGroupName);

        foreach (var accountName in GetSharedDataAccountNames())
        {
            TryAddResolvedPrincipal(resolved, accountName);
        }

        return resolved;
    }

    private static IEnumerable<string> GetSharedDataAccountNames()
    {
        var configuredAccounts = Environment.GetEnvironmentVariable(SharedDataAccountsEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredAccounts))
        {
            return DefaultSharedDataAccounts;
        }

        return configuredAccounts
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [SupportedOSPlatform("windows")]
    private static void TryAddResolvedPrincipal(ICollection<SecurityIdentifier> resolved, string accountName)
    {
        foreach (var candidate in GetAccountCandidates(accountName))
        {
            try
            {
                var securityIdentifier = (SecurityIdentifier)candidate.Translate(typeof(SecurityIdentifier));
                if (!resolved.Contains(securityIdentifier))
                {
                    resolved.Add(securityIdentifier);
                }

                return;
            }
            catch (Exception ex) when (
                ex is IdentityNotMappedException
                    or SystemException)
            {
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<NTAccount> GetAccountCandidates(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            yield break;
        }

        yield return new NTAccount(Environment.MachineName, accountName);
        yield return new NTAccount(accountName);
    }

    private static bool HasReparsePoint(string path)
    {
        return IsReparsePoint(path);
    }
}
