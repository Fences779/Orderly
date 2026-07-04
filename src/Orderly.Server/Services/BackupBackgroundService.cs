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
            var fileName = $"orderly_{timestamp}.sql.gz";
            var localDir = Path.Combine(Path.GetTempPath(), "orderly-backups");
            Directory.CreateDirectory(localDir);
            var localPath = Path.Combine(localDir, fileName);

            await backupService.BackupAsync(localPath, cancellationToken);

            if (blobStorage.IsEnabled)
            {
                var key = $"{options.OssBackupPrefix}{DateTime.UtcNow:yyyy/MM/dd}/{fileName}".Replace("//", "/");
                if (!key.EndsWith('/'))
                {
                    key = key.TrimStart('/');
                }

                await using var uploadStream = File.OpenRead(localPath);
                await blobStorage.UploadAsync(key, uploadStream, cancellationToken);

                await CleanupOldBackupsAsync(blobStorage, options, cancellationToken);
            }

            File.Delete(localPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Database backup failed: {ex.Message}");
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
                // Backup keys look like: backups/2025/01/15/orderly_20250115020000.sql.gz
                var fileName = Path.GetFileName(key);
                if (fileName.StartsWith("orderly_", StringComparison.Ordinal)
                    && fileName.EndsWith(".sql.gz", StringComparison.Ordinal)
                    && DateTime.TryParseExact(
                        fileName[8..^7],
                        "yyyyMMddHHmmss",
                        null,
                        System.Globalization.DateTimeStyles.None,
                        out var backupTime)
                    && backupTime < cutoff)
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
}
