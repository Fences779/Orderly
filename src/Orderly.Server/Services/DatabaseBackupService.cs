using System.Diagnostics;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IDatabaseBackupService
{
    Task<string> BackupAsync(string outputPath, CancellationToken cancellationToken = default);
}

public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private readonly ServerOptions _options;

    public DatabaseBackupService(ServerOptions options)
    {
        _options = options;
    }

    public async Task<string> BackupAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var pgHost = _options.PostgresHost;
        var pgPort = _options.PostgresPort;
        var pgDatabase = _options.PostgresDatabase;
        var pgUser = _options.PostgresUser;
        var pgPassword = _options.PostgresPassword;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = "pg_dump",
            Arguments = $"-h \"{pgHost}\" -p {pgPort} -U \"{pgUser}\" -F c -f \"{outputPath}\" \"{pgDatabase}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PGPASSWORD"] = pgPassword;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pg_dump process.");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"pg_dump failed with exit code {process.ExitCode}: {error}");
        }

        return outputPath;
    }
}
