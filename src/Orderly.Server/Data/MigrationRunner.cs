using System.Reflection;
using DbUp;
using DbUp.Engine;
using Orderly.Server.Models;
using Orderly.Server.Services;

namespace Orderly.Server.Data;

public sealed class MigrationRunner
{
    private readonly ServerOptions _options;

    public MigrationRunner(ServerOptions options)
    {
        _options = options;
    }

    public DatabaseUpgradeResult Run()
    {
        var connectionString = _options.GetConnectionString();
        var engine = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), script => script.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .LogToConsole()
            .WithoutTransaction()
            .Build();

        if (engine.IsUpgradeRequired() && _options.RequirePreMigrationBackup)
        {
            RunPreMigrationBackup();
        }

        return engine.PerformUpgrade();
    }

    public bool EnsureSchema()
    {
        var result = Run();
        return result.Successful;
    }

    private void RunPreMigrationBackup()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var fileName = $"orderly_pre_migration_{timestamp}.dump";
        var outputPath = Path.Combine(_options.LocalBackupDirectory, fileName);

        try
        {
            var backupService = new DatabaseBackupService(_options);
            backupService.BackupAsync(outputPath).GetAwaiter().GetResult();
            BackupHealthState.Update(_options, snapshot =>
            {
                snapshot.LastPreMigrationBackupAtUtc = DateTime.UtcNow;
                snapshot.PreMigrationBackupPath = outputPath;
                snapshot.LastError = null;
            });
        }
        catch (Exception ex)
        {
            BackupHealthState.Update(_options, snapshot => snapshot.LastError = $"Pre-migration backup failed: {ex.Message}");
            throw new InvalidOperationException("Pre-migration backup failed. Database migration was not started.", ex);
        }
    }
}
