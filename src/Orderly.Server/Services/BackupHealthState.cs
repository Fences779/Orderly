using System.Text.Json;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class BackupHealthSnapshot
{
    public DateTime? LastBackupAtUtc { get; set; }
    public string? LastBackupFileName { get; set; }
    public string? LocalBackupPath { get; set; }
    public bool? OssUploaded { get; set; }
    public string? OssKey { get; set; }
    public DateTime? LastPreMigrationBackupAtUtc { get; set; }
    public string? PreMigrationBackupPath { get; set; }
    public DateTime? LastPreImportBackupAtUtc { get; set; }
    public string? PreImportBackupPath { get; set; }
    public DateTime? LastRestoreDrillAtUtc { get; set; }
    public string? LastRestoreDrillStatus { get; set; }
    public string? LastRestoreDrillDatabase { get; set; }
    public string? LastRestoreDrillError { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class BackupHealthState
{
    private const string HealthFileName = "backup-health.json";
    private const string RestoreDrillFileName = "restore-drill-health.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static BackupHealthSnapshot Load(ServerOptions options)
    {
        var snapshot = LoadFile(GetHealthPath(options)) ?? new BackupHealthSnapshot();
        var restoreDrill = LoadFile(GetRestoreDrillPath(options));
        if (restoreDrill is not null)
        {
            snapshot.LastRestoreDrillAtUtc = restoreDrill.LastRestoreDrillAtUtc;
            snapshot.LastRestoreDrillStatus = restoreDrill.LastRestoreDrillStatus;
            snapshot.LastRestoreDrillDatabase = restoreDrill.LastRestoreDrillDatabase;
            snapshot.LastRestoreDrillError = restoreDrill.LastRestoreDrillError;
        }

        return snapshot;
    }

    public static void Update(ServerOptions options, Action<BackupHealthSnapshot> mutate)
    {
        Directory.CreateDirectory(options.LocalBackupDirectory);
        var snapshot = Load(options);
        mutate(snapshot);
        snapshot.UpdatedAtUtc = DateTime.UtcNow;
        File.WriteAllText(GetHealthPath(options), JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public static string GetHealthPath(ServerOptions options)
        => Path.Combine(options.LocalBackupDirectory, HealthFileName);

    public static string GetRestoreDrillPath(ServerOptions options)
        => Path.Combine(options.LocalBackupDirectory, RestoreDrillFileName);

    private static BackupHealthSnapshot? LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BackupHealthSnapshot>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
