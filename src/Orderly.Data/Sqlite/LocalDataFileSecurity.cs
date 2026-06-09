using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Orderly.Data.Sqlite;

public static class LocalDataFileSecurity
{
    public static void HardenDirectory(string directoryPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        HardenDirectoryWindows(directoryPath);
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
                or DirectoryNotFoundException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void HardenDirectoryWindows(string directoryPath)
    {
        try
        {
            if (HasReparsePoint(directoryPath))
            {
                return;
            }

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return;
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            var security = directoryInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleAll(rule);
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            AddDirectoryRule(security, currentUser);
            AddDirectoryRule(
                security,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null));
            directoryInfo.SetAccessControl(security);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SystemException)
        {
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
                return;
            }

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, targetType: typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleAll(rule);
            }

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SystemException)
        {
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

    private static bool HasReparsePoint(string path)
    {
        return IsReparsePoint(path);
    }
}
