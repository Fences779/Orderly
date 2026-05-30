using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class StringNarrationInventoryWorkspaceServiceAdapter : IInventoryWorkspaceService
{
    private readonly IStringNarrationBusinessService _businessService;

    public StringNarrationInventoryWorkspaceServiceAdapter(IStringNarrationBusinessService businessService)
    {
        _businessService = businessService;
    }

    public Task<StringNarrationInventoryManagementDashboardResult> GetDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        return _businessService.GetInventoryManagementDashboardAsync(request, cancellationToken);
    }

    public Task<InventoryImportPreviewResult> PrepareWorkbookImportAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可导入 Excel。");
    }

    public Task<InventoryImportCommitResult> CommitWorkbookImportAsync(InventoryImportPreviewResult preview, bool writeBackWorkbook, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可同步到云端。");
    }

    public Task ExportWorkbookAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            $"{InventoryGatewayOptions.EndpointEnvironmentVariableName} / {InventoryGatewayOptions.TokenEnvironmentVariableName} 未配置，当前只支持库存只读看板，暂不可导出 Excel。");
    }
}
