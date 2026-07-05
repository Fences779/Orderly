using System.Diagnostics;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IRestoreDrillService
{
    Task<RestoreDrillResult> RunAsync(string dumpPath, CancellationToken cancellationToken = default);
}

public sealed record RestoreDrillResult(
    string DatabaseName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc);

public sealed class DatabaseRestoreDrillService : IRestoreDrillService
{
    private readonly ServerOptions _options;

    public DatabaseRestoreDrillService(ServerOptions options)
    {
        _options = options;
    }

    public async Task<RestoreDrillResult> RunAsync(string dumpPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dumpPath) || !File.Exists(dumpPath))
        {
            throw new FileNotFoundException("Backup dump was not found.", dumpPath);
        }

        var startedAt = DateTime.UtcNow;
        var databaseName = $"orderly_restore_drill_{startedAt:yyyyMMddHHmmss}_{Guid.NewGuid():N}".Substring(0, 47);

        await RunProcessAsync("createdb", new[]
        {
            "-h", _options.PostgresHost,
            "-p", _options.PostgresPort.ToString(),
            "-U", _options.PostgresUser,
            databaseName
        }, cancellationToken);

        try
        {
            await RunProcessAsync("pg_restore", new[]
            {
                "-h", _options.PostgresHost,
                "-p", _options.PostgresPort.ToString(),
                "-U", _options.PostgresUser,
                "--no-owner",
                "--no-privileges",
                "-d", databaseName,
                dumpPath
            }, cancellationToken);

            await RunProcessAsync("psql", new[]
            {
                "-h", _options.PostgresHost,
                "-p", _options.PostgresPort.ToString(),
                "-U", _options.PostgresUser,
                "-d", databaseName,
                "-v", "ON_ERROR_STOP=1",
                "-c", "SELECT COUNT(*) AS cloud_users FROM \"CloudUsers\";"
            }, cancellationToken);

            await RunProcessAsync("psql", new[]
            {
                "-h", _options.PostgresHost,
                "-p", _options.PostgresPort.ToString(),
                "-U", _options.PostgresUser,
                "-d", databaseName,
                "-v", "ON_ERROR_STOP=1",
                "-c", "SELECT COUNT(*) AS workspaces FROM \"CloudWorkspaces\";"
            }, cancellationToken);

            return new RestoreDrillResult(databaseName, startedAt, DateTime.UtcNow);
        }
        finally
        {
            await RunProcessAsync("dropdb", new[]
            {
                "-h", _options.PostgresHost,
                "-p", _options.PostgresPort.ToString(),
                "-U", _options.PostgresUser,
                "--if-exists",
                databaseName
            }, CancellationToken.None);
        }
    }

    private async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PGPASSWORD"] = _options.PostgresPassword;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        _ = await stdoutTask;
        var error = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed with exit code {process.ExitCode}: {error}");
        }
    }
}
