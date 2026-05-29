using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IInventoryWorkspaceService
{
    Task<StringNarrationInventoryManagementDashboardResult> GetDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryImportPreviewResult> PrepareWorkbookImportAsync(
        string workbookPath,
        CancellationToken cancellationToken = default);

    Task<InventoryImportCommitResult> CommitWorkbookImportAsync(
        InventoryImportPreviewResult preview,
        bool writeBackWorkbook,
        CancellationToken cancellationToken = default);

    Task ExportWorkbookAsync(
        string workbookPath,
        CancellationToken cancellationToken = default);
}
