using System.Text.Json;

namespace Orderly.Data.Sqlite;

/// <summary>
/// Copies legacy business data from the old Velopack installation root (%LocalAppData%\Orderly)
/// into the fixed independent data root (D:\OrderlyData by default) without deleting the source.
/// The migration is whitelist-based so installer files like current/Update.exe/sq.version are
/// never touched, and it is safe to rerun after interruption.
/// </summary>
public static class LegacyAppDataMigrationService
{
    private const string MigrationStateFileName = ".legacy-data-migration-state.json";
    private static readonly string[] DirectoryEntries =
    [
        "identity",
        "accounts",
        "avatars",
        "Diagnostics",
        "logs",
        "cache"
    ];

    private static readonly string[] FileEntries =
    [
        "orderly.db",
        "orderly.db-journal",
        "orderly.db-wal",
        "orderly.db-shm"
    ];

    public static void EnsureMigrated()
    {
        var legacyRoot = DatabasePaths.GetLegacyAppRootPath();
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        var currentRoot = DatabasePaths.GetAppRootPath();
        if (string.Equals(
                Path.GetFullPath(legacyRoot),
                Path.GetFullPath(currentRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(legacyRoot, "legacy local data migration 源目录");
        if (!HasMigratableEntries(legacyRoot))
        {
            return;
        }

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(currentRoot, "应用数据目录");
        var state = LoadState(currentRoot);

        foreach (var directoryName in DirectoryEntries)
        {
            var sourceDirectory = Path.Combine(legacyRoot, directoryName);
            if (!Directory.Exists(sourceDirectory))
            {
                continue;
            }

            var destinationDirectory = Path.Combine(currentRoot, directoryName);
            CopyDirectoryRecursively(sourceDirectory, destinationDirectory);
            state.MarkCompleted(directoryName);
            SaveState(currentRoot, state);
        }

        foreach (var fileName in FileEntries)
        {
            var sourceFile = Path.Combine(legacyRoot, fileName);
            if (!File.Exists(sourceFile))
            {
                continue;
            }

            var destinationFile = Path.Combine(currentRoot, fileName);
            CopyFileIfMissing(sourceFile, destinationFile);
            state.MarkCompleted(fileName);
            SaveState(currentRoot, state);
        }

        state.MarkFinished();
        SaveState(currentRoot, state);
    }

    private static bool HasMigratableEntries(string legacyRoot)
    {
        foreach (var directoryName in DirectoryEntries)
        {
            if (Directory.Exists(Path.Combine(legacyRoot, directoryName)))
            {
                return true;
            }
        }

        foreach (var fileName in FileEntries)
        {
            if (File.Exists(Path.Combine(legacyRoot, fileName)))
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyDirectoryRecursively(string sourceDirectory, string destinationDirectory)
    {
        LocalDataFileSecurity.EnsureDirectoryIsNotLinked(sourceDirectory, "legacy local data migration 源目录");
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(destinationDirectory, "legacy local data migration 目标目录");

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            LocalDataFileSecurity.EnsureDirectoryIsNotLinked(directoryPath, "legacy local data migration 源目录");
            var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            var targetDirectory = Path.Combine(destinationDirectory, relativePath);
            LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(targetDirectory, "legacy local data migration 目标目录");
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var targetPath = Path.Combine(destinationDirectory, relativePath);
            CopyFileIfMissing(filePath, targetPath);
        }
    }

    private static void CopyFileIfMissing(string sourceFilePath, string destinationFilePath)
    {
        LocalDataFileSecurity.EnsureFileIsNotLinked(sourceFilePath, "legacy local data migration 源文件");
        var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("legacy local data migration 目标路径无效。");
        }

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(destinationDirectory, "legacy local data migration 目标目录");
        if (File.Exists(destinationFilePath))
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(destinationFilePath, "legacy local data migration 目标文件");
            return;
        }

        var tempPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(tempPath, "legacy local data migration 临时文件");
            using (var source = new FileStream(
                       sourceFilePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: 81920,
                       FileOptions.SequentialScan))
            using (var target = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.WriteThrough))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }

            LocalDataFileSecurity.HardenFile(tempPath);
            File.Move(tempPath, destinationFilePath, overwrite: false);
            LocalDataFileSecurity.EnsureFileIsNotLinked(destinationFilePath, "legacy local data migration 目标文件");
            LocalDataFileSecurity.HardenFile(destinationFilePath);
        }
        catch (IOException) when (File.Exists(destinationFilePath))
        {
            // Another migration run or previous successful copy won the race; keep the existing file.
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static MigrationState LoadState(string currentRoot)
    {
        var statePath = Path.Combine(currentRoot, MigrationStateFileName);
        if (!File.Exists(statePath))
        {
            return new MigrationState();
        }

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(statePath, "legacy local data migration 状态文件");
            var json = File.ReadAllText(statePath);
            return JsonSerializer.Deserialize<MigrationState>(json) ?? new MigrationState();
        }
        catch
        {
            return new MigrationState();
        }
    }

    private static void SaveState(string currentRoot, MigrationState state)
    {
        var statePath = Path.Combine(currentRoot, MigrationStateFileName);
        var tempPath = statePath + $".{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(tempPath, "legacy local data migration 临时状态文件");
            File.WriteAllText(tempPath, json);
            LocalDataFileSecurity.HardenFile(tempPath);
            File.Move(tempPath, statePath, overwrite: true);
            LocalDataFileSecurity.EnsureFileIsNotLinked(statePath, "legacy local data migration 状态文件");
            LocalDataFileSecurity.HardenFile(statePath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class MigrationState
    {
        public List<string> CompletedEntries { get; set; } = [];
        public DateTimeOffset? LastCompletedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public void MarkCompleted(string entry)
        {
            if (!CompletedEntries.Contains(entry, StringComparer.OrdinalIgnoreCase))
            {
                CompletedEntries.Add(entry);
            }

            LastCompletedAt = DateTimeOffset.Now;
        }

        public void MarkFinished()
        {
            FinishedAt = DateTimeOffset.Now;
            LastCompletedAt = FinishedAt;
        }
    }
}
