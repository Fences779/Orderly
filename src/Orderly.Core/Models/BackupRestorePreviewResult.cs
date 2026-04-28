namespace Orderly.Core.Models;

public sealed class BackupRestorePreviewResult
{
    public string BackupPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? ExportedAt { get; set; }
    public int? SchemaVersion { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public bool IsChecksumValid { get; set; }
    public IReadOnlyDictionary<string, int> Counts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public BackupValidationResult Validation { get; set; } = new();
    public BackupRestoreTargetState TargetState { get; set; } = BackupRestoreTargetState.Unknown;
    public IReadOnlyDictionary<string, int> TargetCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public bool WillClearQaData { get; set; }
    public bool RequiresQaDataClear { get; set; }
    public bool IsQaTaggedBackup { get; set; }
    public bool CanRestore { get; set; }
    public string RefuseReason { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = [];
}
