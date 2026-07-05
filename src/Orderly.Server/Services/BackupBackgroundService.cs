using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class BackupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _dailyAt;

    public BackupBackgroundService(IServiceProvider serviceProvider, TimeSpan? dailyAt = null)
    {
        _serviceProvider = serviceProvider;
        _dailyAt = dailyAt ?? TimeSpan.FromHours(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.Add(_dailyAt);
            if (nextRun <= now)
            {
                nextRun = nextRun.AddDays(1);
            }

            var delay = nextRun - now;
            await Task.Delay(delay, stoppingToken);

            await RunBackupAsync(stoppingToken);
        }
    }

    private async Task RunBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<ServerOptions>();
            var backupService = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            var blobStorage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var fileName = $"orderly_{timestamp}.dump";
            var localDir = options.LocalBackupDirectory;
            Directory.CreateDirectory(localDir);
            var localPath = Path.Combine(localDir, fileName);

            await backupService.BackupAsync(localPath, cancellationToken);

            var ossUploaded = false;
            string? ossKey = null;
            if (blobStorage.IsEnabled)
            {
                var key = $"{NormalizePrefix(options.OssBackupPrefix)}{DateTime.UtcNow:yyyy/MM/dd}/{fileName}";

                await using var uploadStream = File.OpenRead(localPath);
                await blobStorage.UploadAsync(key, uploadStream, cancellationToken);
                ossUploaded = true;
                ossKey = key;

                await CleanupOldBackupsAsync(blobStorage, options, cancellationToken);
            }

            CleanupOldLocalBackups(localDir, options.BackupRetentionDays);
            BackupHealthState.Update(options, snapshot =>
            {
                snapshot.LastBackupAtUtc = DateTime.UtcNow;
                snapshot.LastBackupFileName = fileName;
                snapshot.LocalBackupPath = localPath;
                snapshot.OssUploaded = ossUploaded;
                snapshot.OssKey = ossKey;
                snapshot.LastError = null;
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database backup failed: {ex.Message}");
            try
            {
                await using var scope = _serviceProvider.CreateAsyncScope();
                var options = scope.ServiceProvider.GetRequiredService<ServerOptions>();
                BackupHealthState.Update(options, snapshot => snapshot.LastError = ex.Message);
            }
            catch
            {
            }
        }
    }

    private static async Task CleanupOldBackupsAsync(IBlobStorage blobStorage, ServerOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-options.BackupRetentionDays);
            var keys = await blobStorage.ListAsync(options.OssBackupPrefix, cancellationToken);
            foreach (var key in keys)
            {
                // Heuristic: delete keys that contain a timestamp older than the cutoff.
                // Backup keys look like: backups/2025/01/15/orderly_20250115020000.dump
                var fileName = Path.GetFileName(key);
                if (TryParseBackupTimestamp(fileName, out var backupTime) && backupTime < cutoff)
                {
                    await blobStorage.DeleteAsync(key, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Backup cleanup failed: {ex.Message}");
        }
    }

    private static void CleanupOldLocalBackups(string localDir, int retentionDays)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var path in Directory.EnumerateFiles(localDir, "orderly_*.*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (TryParseBackupTimestamp(fileName, out var backupTime) && backupTime < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Local backup cleanup failed: {ex.Message}");
        }
    }

    private static bool TryParseBackupTimestamp(string fileName, out DateTime backupTime)
    {
        backupTime = default;
        if (!fileName.StartsWith("orderly_", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = fileName.EndsWith(".dump", StringComparison.Ordinal)
            ? ".dump"
            : fileName.EndsWith(".sql.gz", StringComparison.Ordinal)
                ? ".sql.gz"
                : null;
        if (suffix is null)
        {
            return false;
        }

        var timestamp = fileName.Substring("orderly_".Length, fileName.Length - "orderly_".Length - suffix.Length);
        return DateTime.TryParseExact(
            timestamp,
            "yyyyMMddHHmmss",
            null,
            System.Globalization.DateTimeStyles.None,
            out backupTime);
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        var normalized = prefix.Trim().TrimStart('/');
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }
}
