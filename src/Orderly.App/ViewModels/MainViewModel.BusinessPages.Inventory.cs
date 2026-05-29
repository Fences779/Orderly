using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private bool _hasLoadedInventoryOnce;

    public ObservableCollection<StringNarrationInventoryItem> InventoryItems { get; } = new();
    public ObservableCollection<StringNarrationInventoryMovement> InventoryRecentMovements { get; } = new();

    public bool HasInventoryItems => InventoryItems.Count > 0;
    public bool HasInventoryRecentMovements => InventoryRecentMovements.Count > 0;
    public bool HasInventoryDashboardItems => InventoryDashboardItems.Count > 0;
    public int InventoryLowStockCount => InventoryDashboardSummary.LowStockCount;
    public decimal InventoryTotalStock => InventoryItems.Sum(item => item.StockOnHand);
    public decimal InventoryAvailableStock => InventoryItems.Sum(item => item.AvailableStock);
    public decimal InventoryTotalValue => InventoryItems.Sum(item => item.InventoryValue);
    public StringNarrationInventoryManagementDashboardSummary InventoryDashboardSummary => InventoryDashboardResult.Summary;
    public IReadOnlyList<StringNarrationInventoryManagementDashboardItem> InventoryDashboardItems => InventoryDashboardResult.Items;
    public StringNarrationInventoryManagementDashboardFilterOptions InventoryDashboardFilterOptions => InventoryDashboardResult.FilterOptions;
    public StringNarrationInventoryManagementDashboardPageInfo InventoryDashboardPageInfo => InventoryDashboardResult.PageInfo;
    public string InventoryItemsCountText => $"{InventoryItems.Count} 个 SKU";
    public string InventoryLowStockCountText => InventoryDashboardLowStockCountText;
    public string InventoryTotalStockText => $"{InventoryTotalStock:0.##}";
    public string InventoryAvailableStockText => $"{InventoryAvailableStock:0.##}";
    public string InventoryTotalValueText => FormatCurrency(InventoryTotalValue);
    public string InventoryUpdatedAtText => InventoryDashboardResult.UpdatedAt <= 0 ? "未同步" : FormatBusinessTimestamp(InventoryDashboardResult.UpdatedAt);
    public string InventoryDashboardLowStockCountText => BuildInventoryDashboardCountText(InventoryDashboardSummary.LowStockCount);
    public string InventoryDashboardFastSellingCountText => BuildInventoryDashboardCountText(InventoryDashboardSummary.FastSellingCount);
    public string InventoryDashboardLowSellingCountText => BuildInventoryDashboardCountText(InventoryDashboardSummary.LowSellingCount);
    public string InventoryDashboardHealthStatusText => BuildInventoryHealthStatusText();
    public string InventoryDashboardHealthSummaryText => BuildInventoryHealthSummaryText();
    public string InventoryDashboardWarningCountText => BuildInventoryDashboardCountText(InventoryDashboardSummary.InventoryWarningCount);
    public string InventoryDashboardAvgOrderMaterialUsageText => BuildInventoryDashboardNumberText(InventoryDashboardSummary.AvgOrderMaterialUsage, "0.##");
    public string InventoryDashboardAvgMaterialUnitCostText => BuildInventoryDashboardCurrencyText(InventoryDashboardSummary.AvgMaterialUnitCost);
    public string InventoryDashboardAvgBraceletSalePriceText => BuildInventoryDashboardCurrencyText(InventoryDashboardSummary.AvgBraceletSalePrice);
    public string InventoryDashboardAvgBraceletCostPriceText => BuildInventoryDashboardCurrencyText(InventoryDashboardSummary.AvgBraceletCostPrice);
    public string InventoryDashboardGrossMarginRateText => BuildInventoryDashboardNumberText(InventoryDashboardSummary.GrossMarginRate, "0.##", "%");
    public string InventoryDashboardCategorySummaryText => InventoryDashboardFilterOptions.Categories.Count > 0
        ? $"{InventoryDashboardFilterOptions.Categories.Count} 个分类"
        : GetInventoryDashboardFallbackText("暂无分类");
    public string InventoryDashboardStatusSummaryText => InventoryDashboardFilterOptions.Statuses.Count > 0
        ? $"{InventoryDashboardFilterOptions.Statuses.Count} 个状态选项"
        : GetInventoryDashboardFallbackText("暂无筛选项");
    public string InventoryDashboardPageInfoText => HasInventoryDashboardSnapshot
        ? $"第 {Math.Max(InventoryDashboardPageInfo.Page, 1)} / {Math.Max(InventoryDashboardPageInfo.TotalPages, 1)} 页，共 {InventoryDashboardPageInfo.Total} 条"
        : GetInventoryDashboardFallbackText("暂无分页结果");
    public string InventoryEmptyStateText => string.IsNullOrWhiteSpace(InventoryError)
        ? "暂无库存数据，点击刷新从库存主库拉取。"
        : InventoryError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInventoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCashflowCommand))]
    private bool isInventoryLoading;

    [ObservableProperty]
    private string inventoryError = string.Empty;

    [ObservableProperty]
    private string inventoryKeyword = string.Empty;

    [ObservableProperty]
    private StringNarrationInventoryManagementDashboardResult inventoryDashboardResult = new();

    [ObservableProperty]
    private StringNarrationInventoryItem? selectedInventoryItem;

    [RelayCommand(CanExecute = nameof(CanRunBusinessDataReadAction))]
    private async Task RefreshInventoryAsync()
    {
        if (IsInventoryLoading)
        {
            return;
        }

        try
        {
            IsInventoryLoading = true;
            InventoryError = string.Empty;
            StatusMessage = "正在加载库存数据...";
            var dashboardResult = await _inventoryWorkspaceService.GetDashboardAsync(new StringNarrationInventoryManagementDashboardRequest
            {
                Keyword = InventoryKeyword,
                Status = "all",
                SortBy = "sold30dRatio",
                SortDirection = "desc",
                Page = 1,
                PageSize = 100
            });
            InventoryDashboardResult = dashboardResult;
            ReplaceCollection(InventoryItems, dashboardResult.Items
                .OrderBy(item => GetInventoryDashboardStatusRank(item.Status))
                .ThenBy(item => item.Category)
                .ThenBy(item => item.MaterialName)
                .Select(MapDashboardItemToInventoryItem));
            ReplaceCollection(InventoryRecentMovements, []);
            SelectedInventoryItem = InventoryItems.FirstOrDefault();
            _hasLoadedInventoryOnce = true;
            StatusMessage = $"已加载库存看板：{InventoryItems.Count} 个 SKU";
        }
        catch (Exception ex)
        {
            InventoryError = $"库存数据加载失败：{ex.Message}";
            StatusMessage = InventoryError;
        }
        finally
        {
            IsInventoryLoading = false;
            NotifyInventoryStateChanged();
        }
    }

    private static StringNarrationInventoryItem MapDashboardItemToInventoryItem(StringNarrationInventoryManagementDashboardItem item)
    {
        var enabled = !string.Equals(item.Status, "disabled", StringComparison.OrdinalIgnoreCase);
        var statusLabel = string.IsNullOrWhiteSpace(item.StatusLabel)
            ? item.Status switch
            {
                "fast_selling" => "动销偏快",
                "low_selling" => "低动销",
                "low_stock" => "低库存",
                "disabled" => "停用",
                _ => "正常"
            }
            : item.StatusLabel.Trim();

        return new StringNarrationInventoryItem
        {
            Id = string.IsNullOrWhiteSpace(item.MaterialId) ? item.MaterialName : item.MaterialId,
            SkuId = string.IsNullOrWhiteSpace(item.MaterialId) ? item.MaterialName : item.MaterialId,
            Name = item.MaterialName,
            Category = item.Category,
            CostPrice = item.UnitCost ?? 0,
            PurchasePrice = item.UnitCost ?? 0,
            StockOnHand = item.CurrentStockQty,
            StockReserved = 0,
            SafetyStock = item.SafeStockSuggestedQty ?? 0,
            StockUnit = item.StockUnit,
            SupplierName = item.SupplierName,
            InventoryRemark = string.IsNullOrWhiteSpace(item.Remark)
                ? $"看板状态：{statusLabel}"
                : $"{item.Remark}（看板状态：{statusLabel}）",
            Enabled = enabled,
            LastRestockedAt = item.LastRestockedAt
        };
    }

    private static int GetInventoryDashboardStatusRank(string status)
    {
        return status switch
        {
            "low_stock" => 0,
            "fast_selling" => 1,
            "low_selling" => 2,
            "normal" => 3,
            "disabled" => 4,
            _ => 5
        };
    }

    private void NotifyInventoryStateChanged()
    {
        OnPropertyChanged(nameof(HasInventoryItems));
        OnPropertyChanged(nameof(HasInventoryRecentMovements));
        OnPropertyChanged(nameof(HasInventoryDashboardItems));
        OnPropertyChanged(nameof(InventoryLowStockCount));
        OnPropertyChanged(nameof(InventoryDashboardSummary));
        OnPropertyChanged(nameof(InventoryDashboardItems));
        OnPropertyChanged(nameof(InventoryDashboardFilterOptions));
        OnPropertyChanged(nameof(InventoryDashboardPageInfo));
        OnPropertyChanged(nameof(InventoryTotalStock));
        OnPropertyChanged(nameof(InventoryAvailableStock));
        OnPropertyChanged(nameof(InventoryTotalValue));
        OnPropertyChanged(nameof(InventoryItemsCountText));
        OnPropertyChanged(nameof(InventoryLowStockCountText));
        OnPropertyChanged(nameof(InventoryDashboardLowStockCountText));
        OnPropertyChanged(nameof(InventoryDashboardFastSellingCountText));
        OnPropertyChanged(nameof(InventoryDashboardLowSellingCountText));
        OnPropertyChanged(nameof(InventoryDashboardHealthStatusText));
        OnPropertyChanged(nameof(InventoryDashboardHealthSummaryText));
        OnPropertyChanged(nameof(InventoryDashboardWarningCountText));
        OnPropertyChanged(nameof(InventoryDashboardAvgOrderMaterialUsageText));
        OnPropertyChanged(nameof(InventoryDashboardAvgMaterialUnitCostText));
        OnPropertyChanged(nameof(InventoryDashboardAvgBraceletSalePriceText));
        OnPropertyChanged(nameof(InventoryDashboardAvgBraceletCostPriceText));
        OnPropertyChanged(nameof(InventoryDashboardGrossMarginRateText));
        OnPropertyChanged(nameof(InventoryDashboardCategorySummaryText));
        OnPropertyChanged(nameof(InventoryDashboardStatusSummaryText));
        OnPropertyChanged(nameof(InventoryDashboardPageInfoText));
        OnPropertyChanged(nameof(InventoryTotalStockText));
        OnPropertyChanged(nameof(InventoryAvailableStockText));
        OnPropertyChanged(nameof(InventoryTotalValueText));
        OnPropertyChanged(nameof(InventoryUpdatedAtText));
        OnPropertyChanged(nameof(InventoryEmptyStateText));
    }

    private string BuildInventoryHealthStatusText()
    {
        return BuildDisplayText(
            InventoryDashboardSummary.InventoryHealthStatus,
            GetInventoryDashboardFallbackText("暂不可用"));
    }

    private string BuildInventoryHealthSummaryText()
    {
        return BuildDisplayText(
            InventoryDashboardSummary.InventoryHealthSummary,
            GetInventoryDashboardFallbackText("库存看板摘要暂不可用"));
    }

    private bool HasInventoryDashboardSnapshot => InventoryDashboardResult.UpdatedAt > 0
        || HasInventoryDashboardItems
        || !string.IsNullOrWhiteSpace(InventoryDashboardSummary.InventoryHealthStatus)
        || !string.IsNullOrWhiteSpace(InventoryDashboardSummary.InventoryHealthSummary);

    private string GetInventoryDashboardFallbackText(string afterLoadFallback)
    {
        if (IsInventoryLoading)
        {
            return "加载中";
        }

        if (HasInventoryDashboardSnapshot)
        {
            return string.Empty;
        }

        return _hasLoadedInventoryOnce || !string.IsNullOrWhiteSpace(InventoryError)
            ? afterLoadFallback
            : "未同步";
    }

    private string BuildInventoryDashboardCountText(int value)
    {
        var fallback = GetInventoryDashboardFallbackText("未接入");
        return string.IsNullOrEmpty(fallback) ? $"{value} 项" : fallback;
    }

    private string BuildInventoryDashboardCurrencyText(decimal? value)
    {
        var fallback = GetInventoryDashboardFallbackText("未评估");
        return value.HasValue ? FormatCurrency(value) : fallback;
    }

    private string BuildInventoryDashboardNumberText(decimal? value, string format, string suffix = "")
    {
        var fallback = GetInventoryDashboardFallbackText("未评估");
        return value.HasValue
            ? $"{value.Value.ToString(format)}{suffix}"
            : fallback;
    }
}
