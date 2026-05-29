using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private sealed class EmptyWorkbenchTaskService : IWorkbenchTaskService
    {
        public static EmptyWorkbenchTaskService Instance { get; } = new();

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskFilter filter, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }

        public Task<IReadOnlyList<WorkbenchTask>> GetTasksAsync(WorkbenchTaskQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkbenchTask>>([]);
        }
    }

    private sealed class EmptyGlobalSearchService : IGlobalSearchService
    {
        public static EmptyGlobalSearchService Instance { get; } = new();

        public Task<SearchResultSet> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SearchResultSet
            {
                Query = request?.Query?.Trim() ?? string.Empty,
                Limit = request?.Limit ?? 50,
                TotalCount = 0,
                Items = []
            });
        }
    }

    private sealed class EmptyNavigationRouteService : INavigationRouteService
    {
        public static EmptyNavigationRouteService Instance { get; } = new();

        public Task<NavigationRouteResult> ResolveAsync(SearchResultItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(WorkbenchTask task, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }

        public Task<NavigationRouteResult> ResolveAsync(QuickAction action, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NavigationRouteResult
            {
                CanNavigate = false,
                DisabledReason = "Navigation route service is not configured.",
                StatusMessage = "Navigation route service is not configured."
            });
        }
    }

    private sealed class EmptyStringNarrationOrderService : IStringNarrationOrderService
    {
        public static EmptyStringNarrationOrderService Instance { get; } = new();

        public Task<StringNarrationWhoamiResult> WhoamiAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderListResult> GetOrdersAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationFulfillmentStats> GetFulfillmentStatsAsync(StringNarrationOrderQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> GetOrderDetailAsync(string orderNo, string tradeNo = "", string id = "", CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> UpdateFulfillmentAsync(StringNarrationFulfillmentUpdateRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationExceptionActionResult> ApplyExceptionActionAsync(StringNarrationExceptionActionRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationExceptionSampleReplayResult> ReplayExceptionSamplesAsync(StringNarrationExceptionSampleReplayRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }

        public Task<StringNarrationOrderDetail> GenerateProductionOrderAsync(StringNarrationGenerateProductionOrderRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述订单服务未配置。");
        }
    }

    private sealed class EmptyStringNarrationBusinessService : IStringNarrationBusinessService
    {
        public static EmptyStringNarrationBusinessService Instance { get; } = new();

        public Task<StringNarrationInventoryListResult> GetInventoryAsync(StringNarrationInventoryQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationCashflowListResult> GetCashflowAsync(StringNarrationCashflowQuery query, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationCashflowHealthDashboardResult> GetCashflowHealthDashboardAsync(
            StringNarrationCashflowHealthDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }

        public Task<StringNarrationInventoryManagementDashboardResult> GetInventoryManagementDashboardAsync(
            StringNarrationInventoryManagementDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("串述业务数据服务未配置。");
        }
    }

    private sealed class EmptyInventoryWorkspaceService : IInventoryWorkspaceService
    {
        public static EmptyInventoryWorkspaceService Instance { get; } = new();

        public Task<StringNarrationInventoryManagementDashboardResult> GetDashboardAsync(
            StringNarrationInventoryManagementDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("库存工作区服务未配置。");
        }

        public Task<InventoryImportPreviewResult> PrepareWorkbookImportAsync(
            string workbookPath,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("库存工作区服务未配置。");
        }

        public Task<InventoryImportCommitResult> CommitWorkbookImportAsync(
            InventoryImportPreviewResult preview,
            bool writeBackWorkbook,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("库存工作区服务未配置。");
        }

        public Task ExportWorkbookAsync(
            string workbookPath,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("库存工作区服务未配置。");
        }
    }
}
