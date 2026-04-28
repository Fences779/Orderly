using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IBackupService
{
    Task<BackupResult> ExportAsync(
        string outputPath,
        string createdBy = "p2.7",
        bool tagForQaScope = false,
        CancellationToken cancellationToken = default);

    Task<BackupValidationResult> ValidateAsync(
        string backupPath,
        string createdBy = "p2.7",
        bool tagForQaScope = false,
        CancellationToken cancellationToken = default);

    Task<BackupResult?> GetLatestBackupAsync(CancellationToken cancellationToken = default);
}
