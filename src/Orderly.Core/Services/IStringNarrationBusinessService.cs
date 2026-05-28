using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IStringNarrationBusinessService
{
    Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default);

    Task<StringNarrationInventoryManagementDashboardResult> GetInventoryManagementDashboardAsync(
        StringNarrationInventoryManagementDashboardRequest request,
        CancellationToken cancellationToken = default);

    Task<StringNarrationCashflowHealthDashboardResult> GetCashflowHealthDashboardAsync(
        StringNarrationCashflowHealthDashboardRequest request,
        CancellationToken cancellationToken = default);

    Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default);
}
