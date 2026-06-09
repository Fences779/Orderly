namespace Orderly.Core.Models;

public sealed class BackupValidationResult
{
    public string BackupPath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public BackupManifest? Manifest { get; set; }
    public string ActualChecksum { get; set; } = string.Empty;
    public string ActualIntegrityTag { get; set; } = string.Empty;
    public bool IsChecksumValid { get; set; }
    public bool IsIntegrityValid { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}
