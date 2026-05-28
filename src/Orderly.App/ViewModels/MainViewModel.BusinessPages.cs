using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private bool _hasLoadedInventoryOnce;
    private bool _hasLoadedCashflowOnce;

    public ObservableCollection<StringNarrationInventoryItem> InventoryItems { get; } = new();
    public ObservableCollection<StringNarrationInventoryMovement> InventoryRecentMovements { get; } = new();
    public ObservableCollection<StringNarrationCashflowEntry> CashflowEntries { get; } = new();

    public bool HasInventoryItems => InventoryItems.Count > 0;
    public bool HasInventoryRecentMovements => InventoryRecentMovements.Count > 0;
    public bool HasCashflowEntries => CashflowEntries.Count > 0;
    public bool HasInventoryDashboardItems => InventoryDashboardItems.Count > 0;
    public bool HasCashflowHealthTrendItems => CashflowHealthTrendItems.Count > 0;
    public bool HasCashflowIncomeBreakdownItems => CashflowIncomeBreakdown.Items.Count > 0;
    public bool HasCashflowExpenseBreakdownItems => CashflowExpenseBreakdown.Items.Count > 0;
    public bool HasCashflowUpcomingCashItems => CashflowUpcomingCashItems.Count > 0;
    public bool HasCashflowAdviceFocusItems => CashflowAdvice.NextFocus.Count > 0;
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
        ? "暂无库存数据，点击刷新从 adminPcGateway 拉取。"
        : InventoryError;
    public StringNarrationCashflowSummary CashflowSummary => CashflowResult.Summary;
    public StringNarrationCashflowHealthDashboardSummary CashflowHealthSummary => CashflowHealthDashboardResult.Summary;
    public IReadOnlyList<StringNarrationCashflowHealthDashboardTrendItem> CashflowHealthTrendItems => CashflowHealthDashboardResult.TrendItems;
    public StringNarrationCashflowHealthDashboardBreakdown CashflowIncomeBreakdown => CashflowHealthDashboardResult.IncomeBreakdown;
    public StringNarrationCashflowHealthDashboardBreakdown CashflowExpenseBreakdown => CashflowHealthDashboardResult.ExpenseBreakdown;
    public IReadOnlyList<StringNarrationCashflowHealthDashboardUpcomingCashItem> CashflowUpcomingCashItems => CashflowHealthDashboardResult.UpcomingCashItems;
    public StringNarrationCashflowHealthDashboardAdvice CashflowAdvice => CashflowHealthDashboardResult.Advice;
    public string CashflowIncomeTotalText => CashflowSummary.IncomeTotalText;
    public string CashflowExpenseTotalText => CashflowSummary.ExpenseTotalText;
    public string CashflowNetAmountText => CashflowSummary.NetAmountText;
    public string CashflowReceivableAmountText => CashflowSummary.ReceivableAmountText;
    public string CashflowPayableAmountText => CashflowSummary.PayableAmountText;
    public string CashflowEntriesCountText => $"{CashflowEntries.Count} 条";
    public string CashflowHealthScoreText => CashflowHealthSummary.CashFlowHealthScore is > 0 ? $"{CashflowHealthSummary.CashFlowHealthScore} 分" : "未评估";
    public string CashflowHealthLevelText => BuildDisplayText(CashflowHealthSummary.CashFlowHealthLevel, "未评估");
    public string CashflowHealthSummaryText => BuildCashflowHealthSummaryText();
    public string CashflowCashBalanceText => BuildCashflowDashboardCurrencyText(CashflowHealthSummary.CashBalanceAmount, "未接入");
    public string CashflowAvailableCashText => BuildCashflowDashboardCurrencyText(CashflowHealthSummary.AvailableCashAmount, "未评估");
    public string CashflowReceivableAmountHealthText => BuildCashflowDashboardCurrencyText(CashflowHealthSummary.ReceivableAmount, "未接入");
    public string CashflowPayableAmountHealthText => BuildCashflowDashboardCurrencyText(CashflowHealthSummary.PayableAmount, "未接入");
    public string CashflowSupportDaysText => CashflowHealthSummary.SupportDays is > 0
        ? $"{CashflowHealthSummary.SupportDays} 天"
        : GetCashflowDashboardFallbackText("未评估");
    public string CashflowAvgDailyExpense7dText => BuildCashflowDashboardAmountText(CashflowHealthSummary.AvgDailyExpense7d);
    public string CashflowAdviceTitleText => BuildDisplayText(CashflowAdvice.HealthTitle, "暂无建议");
    public string CashflowAdviceDescriptionText => BuildDisplayText(CashflowAdvice.HealthDescription, "现金流健康看板暂不可用");
    public string CashflowUpdatedAtText => CashflowResult.UpdatedAt <= 0 ? "未同步" : FormatBusinessTimestamp(CashflowResult.UpdatedAt);
    public string CashflowEmptyStateText => string.IsNullOrWhiteSpace(CashflowError)
        ? "暂无现金流数据，点击刷新从 adminPcGateway 拉取。"
        : CashflowError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInventoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCashflowCommand))]
    private bool isInventoryLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshInventoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCashflowCommand))]
    private bool isCashflowLoading;

    [ObservableProperty]
    private string inventoryError = string.Empty;

    [ObservableProperty]
    private string cashflowError = string.Empty;

    [ObservableProperty]
    private string inventoryKeyword = string.Empty;

    [ObservableProperty]
    private string cashflowKeyword = string.Empty;

    [ObservableProperty]
    private StringNarrationInventoryManagementDashboardResult inventoryDashboardResult = new();

    [ObservableProperty]
    private StringNarrationCashflowListResult cashflowResult = new();

    [ObservableProperty]
    private StringNarrationCashflowHealthDashboardResult cashflowHealthDashboardResult = new();

    [ObservableProperty]
    private StringNarrationInventoryItem? selectedInventoryItem;

    [ObservableProperty]
    private StringNarrationCashflowEntry? selectedCashflowEntry;

    private async Task EnsureBusinessSectionLoadedAsync(string section)
    {
        if (string.Equals(section, SectionInventory, StringComparison.Ordinal) && !_hasLoadedInventoryOnce)
        {
            await RefreshInventoryAsync();
        }
        else if (string.Equals(section, SectionCashflow, StringComparison.Ordinal) && !_hasLoadedCashflowOnce)
        {
            await RefreshCashflowAsync();
        }
    }

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
            var loadResult = await LoadInventoryDashboardWithFallbackAsync();
            InventoryDashboardResult = loadResult.DashboardResult;
            ReplaceCollection(InventoryItems, loadResult.DashboardResult.Items
                .OrderBy(item => GetInventoryDashboardStatusRank(item.Status))
                .ThenBy(item => item.Category)
                .ThenBy(item => item.MaterialName)
                .Select(MapDashboardItemToInventoryItem));
            ReplaceCollection(InventoryRecentMovements, loadResult.RecentMovements.OrderByDescending(item => item.OccurredAt).Take(20));
            SelectedInventoryItem = InventoryItems.FirstOrDefault();
            _hasLoadedInventoryOnce = true;
            if (!string.IsNullOrWhiteSpace(loadResult.WarningMessage))
            {
                InventoryError = loadResult.WarningMessage;
                StatusMessage = $"已加载库存：{InventoryItems.Count} 个 SKU；{loadResult.WarningMessage}";
            }
            else
            {
                StatusMessage = $"已加载库存：{InventoryItems.Count} 个 SKU";
            }
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

    private async Task<InventoryDashboardLoadResult> LoadInventoryDashboardWithFallbackAsync()
    {
        try
        {
            return new InventoryDashboardLoadResult
            {
                DashboardResult = await _stringNarrationBusinessService.GetInventoryManagementDashboardAsync(new StringNarrationInventoryManagementDashboardRequest
                {
                    Keyword = InventoryKeyword,
                    Status = "all",
                    SortBy = "sold30dRatio",
                    SortDirection = "desc",
                    Page = 1,
                    PageSize = 100
                })
            };
        }
        catch (InvalidOperationException ex) when (IsDashboardActionUnavailable(ex))
        {
            var legacyResult = await _stringNarrationBusinessService.GetInventoryAsync(new StringNarrationInventoryQuery
            {
                Keyword = InventoryKeyword,
                IncludeDisabled = true,
                Page = 1,
                PageSize = 100
            });
            var legacyLowStockCount = legacyResult.Items.Count(item => item.IsLowStock);
            return new InventoryDashboardLoadResult
            {
                DashboardResult = new StringNarrationInventoryManagementDashboardResult
                {
                    UpdatedAt = legacyResult.UpdatedAt,
                    Summary = new StringNarrationInventoryManagementDashboardSummary
                    {
                        LowStockCount = legacyLowStockCount,
                        FastSellingCount = 0,
                        LowSellingCount = 0,
                        InventoryHealthStatus = "暂不可用",
                        InventoryHealthSummary = "库存看板接口暂不可用，当前展示兼容旧库存列表。",
                        InventoryWarningCount = legacyLowStockCount
                    },
                    FilterOptions = new StringNarrationInventoryManagementDashboardFilterOptions
                    {
                        Categories = legacyResult.Items
                            .Select(item => item.Category)
                            .Where(category => !string.IsNullOrWhiteSpace(category))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        Statuses =
                        [
                            new StringNarrationInventoryManagementDashboardFilterOption { Value = "all", Label = "全部状态" },
                            new StringNarrationInventoryManagementDashboardFilterOption { Value = "normal", Label = "正常" },
                            new StringNarrationInventoryManagementDashboardFilterOption { Value = "low_stock", Label = "低库存" },
                            new StringNarrationInventoryManagementDashboardFilterOption { Value = "disabled", Label = "停用" }
                        ],
                        DefaultSortBy = "sold30dRatio",
                        DefaultSortDirection = "desc"
                    },
                    Items = legacyResult.Items.Select(item => new StringNarrationInventoryManagementDashboardItem
                    {
                        MaterialId = item.Id,
                        MaterialName = item.Name,
                        Category = item.Category,
                        CurrentStockQty = item.StockOnHand,
                        StockUnit = item.StockUnit,
                        Sold7dQty = 0,
                        Sold7dRatio = 0,
                        Sold30dQty = 0,
                        Sold30dRatio = 0,
                        Consumed7dQty = 0,
                        Consumed30dQty = 0,
                        SafeStockSuggestedQty = null,
                        Status = item.Enabled ? (item.IsLowStock ? "low_stock" : "normal") : "disabled",
                        StatusLabel = item.StatusText,
                        UnitCost = item.UnitCost > 0 ? item.UnitCost : null,
                        LastRestockedAt = item.LastRestockedAt,
                        SupplierName = item.SupplierName,
                        Remark = item.InventoryRemark
                    }).ToList(),
                    PageInfo = new StringNarrationInventoryManagementDashboardPageInfo
                    {
                        Page = 1,
                        PageSize = legacyResult.Items.Count > 0 ? legacyResult.Items.Count : 100,
                        Total = legacyResult.Total > 0 ? legacyResult.Total : legacyResult.Items.Count,
                        TotalPages = (legacyResult.Total > 0 || legacyResult.Items.Count > 0) ? 1 : 0
                    }
                },
                RecentMovements = legacyResult.RecentMovements,
                WarningMessage = "库存看板接口暂不可用，当前展示兼容旧库存列表。"
            };
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunBusinessDataReadAction))]
    private async Task RefreshCashflowAsync()
    {
        if (IsCashflowLoading)
        {
            return;
        }

        try
        {
            IsCashflowLoading = true;
            CashflowError = string.Empty;
            StatusMessage = "正在加载现金流数据...";

            var cashflowHealthWarning = string.Empty;
            try
            {
                CashflowHealthDashboardResult = await _stringNarrationBusinessService.GetCashflowHealthDashboardAsync(new StringNarrationCashflowHealthDashboardRequest
                {
                    Range = "30d"
                });
            }
            catch (InvalidOperationException ex) when (IsCashflowHealthDashboardActionUnavailable(ex))
            {
                CashflowHealthDashboardResult = new StringNarrationCashflowHealthDashboardResult();
                cashflowHealthWarning = "现金流健康看板接口暂不可用，已保留明细列表。";
                CashflowError = cashflowHealthWarning;
            }

            var result = await _stringNarrationBusinessService.GetCashflowAsync(new StringNarrationCashflowQuery
            {
                Keyword = CashflowKeyword,
                Page = 1,
                PageSize = 100
            });

            CashflowResult = result;
            ReplaceCollection(CashflowEntries, result.Entries.OrderByDescending(item => item.OccurredAt));
            SelectedCashflowEntry = CashflowEntries.FirstOrDefault();
            _hasLoadedCashflowOnce = true;
            StatusMessage = string.IsNullOrWhiteSpace(cashflowHealthWarning)
                ? $"已加载现金流：{CashflowEntries.Count} 条"
                : $"已加载现金流：{CashflowEntries.Count} 条；{cashflowHealthWarning}";
        }
        catch (Exception ex)
        {
            CashflowError = $"现金流数据加载失败：{ex.Message}";
            StatusMessage = CashflowError;
        }
        finally
        {
            IsCashflowLoading = false;
            NotifyCashflowStateChanged();
        }
    }

    private bool CanRunBusinessDataReadAction()
    {
        return !IsInventoryLoading && !IsCashflowLoading;
    }

    private static bool IsDashboardActionUnavailable(Exception ex)
    {
        if (string.IsNullOrWhiteSpace(ex.Message))
        {
            return false;
        }

        return ex.Message.Contains("INVALID_ACTION", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("不支持的 action", StringComparison.Ordinal)
            || ex.Message.Contains("inventoryManagementDashboard", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCashflowHealthDashboardActionUnavailable(Exception ex)
    {
        if (string.IsNullOrWhiteSpace(ex.Message))
        {
            return false;
        }

        return ex.Message.Contains("INVALID_ACTION", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("不支持的 action", StringComparison.Ordinal)
            || ex.Message.Contains("cashflowHealthDashboard", StringComparison.OrdinalIgnoreCase);
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

    private void NotifyCashflowStateChanged()
    {
        OnPropertyChanged(nameof(HasCashflowEntries));
        OnPropertyChanged(nameof(HasCashflowHealthTrendItems));
        OnPropertyChanged(nameof(HasCashflowIncomeBreakdownItems));
        OnPropertyChanged(nameof(HasCashflowExpenseBreakdownItems));
        OnPropertyChanged(nameof(HasCashflowUpcomingCashItems));
        OnPropertyChanged(nameof(HasCashflowAdviceFocusItems));
        OnPropertyChanged(nameof(CashflowSummary));
        OnPropertyChanged(nameof(CashflowHealthDashboardResult));
        OnPropertyChanged(nameof(CashflowHealthSummary));
        OnPropertyChanged(nameof(CashflowHealthTrendItems));
        OnPropertyChanged(nameof(CashflowIncomeBreakdown));
        OnPropertyChanged(nameof(CashflowExpenseBreakdown));
        OnPropertyChanged(nameof(CashflowUpcomingCashItems));
        OnPropertyChanged(nameof(CashflowAdvice));
        OnPropertyChanged(nameof(CashflowIncomeTotalText));
        OnPropertyChanged(nameof(CashflowExpenseTotalText));
        OnPropertyChanged(nameof(CashflowNetAmountText));
        OnPropertyChanged(nameof(CashflowReceivableAmountText));
        OnPropertyChanged(nameof(CashflowPayableAmountText));
        OnPropertyChanged(nameof(CashflowEntriesCountText));
        OnPropertyChanged(nameof(CashflowHealthScoreText));
        OnPropertyChanged(nameof(CashflowHealthLevelText));
        OnPropertyChanged(nameof(CashflowHealthSummaryText));
        OnPropertyChanged(nameof(CashflowCashBalanceText));
        OnPropertyChanged(nameof(CashflowAvailableCashText));
        OnPropertyChanged(nameof(CashflowReceivableAmountHealthText));
        OnPropertyChanged(nameof(CashflowPayableAmountHealthText));
        OnPropertyChanged(nameof(CashflowSupportDaysText));
        OnPropertyChanged(nameof(CashflowAvgDailyExpense7dText));
        OnPropertyChanged(nameof(CashflowAdviceTitleText));
        OnPropertyChanged(nameof(CashflowAdviceDescriptionText));
        OnPropertyChanged(nameof(CashflowUpdatedAtText));
        OnPropertyChanged(nameof(CashflowEmptyStateText));
    }

    private static string FormatCurrency(decimal? value)
    {
        if (!value.HasValue)
        {
            return "不可得";
        }

        return value.Value == 0 ? "¥0" : $"¥{value.Value:N0}";
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

    private string BuildCashflowHealthSummaryText()
    {
        return BuildDisplayText(
            CashflowHealthSummary.CashFlowHealthSummary,
            GetCashflowDashboardFallbackText("现金流健康看板暂不可用"));
    }

    private bool HasInventoryDashboardSnapshot => InventoryDashboardResult.UpdatedAt > 0
        || HasInventoryDashboardItems
        || !string.IsNullOrWhiteSpace(InventoryDashboardSummary.InventoryHealthStatus)
        || !string.IsNullOrWhiteSpace(InventoryDashboardSummary.InventoryHealthSummary);

    private bool HasCashflowHealthDashboardSnapshot => CashflowHealthDashboardResult.UpdatedAt > 0
        || HasCashflowHealthTrendItems
        || CashflowHealthSummary.CashFlowHealthScore.HasValue
        || CashflowHealthSummary.CashBalanceAmount.HasValue
        || CashflowHealthSummary.AvailableCashAmount.HasValue
        || !string.IsNullOrWhiteSpace(CashflowHealthSummary.CashFlowHealthSummary);

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

    private string GetCashflowDashboardFallbackText(string afterLoadFallback)
    {
        if (IsCashflowLoading)
        {
            return "加载中";
        }

        if (HasCashflowHealthDashboardSnapshot)
        {
            return string.Empty;
        }

        return _hasLoadedCashflowOnce || HasCashflowEntries || !string.IsNullOrWhiteSpace(CashflowError)
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

    private string BuildCashflowDashboardCurrencyText(decimal? value, string afterLoadFallback)
    {
        var fallback = GetCashflowDashboardFallbackText(afterLoadFallback);
        return value.HasValue ? FormatCurrency(value) : fallback;
    }

    private string BuildCashflowDashboardAmountText(decimal value)
    {
        var fallback = GetCashflowDashboardFallbackText("未评估");
        return HasCashflowHealthDashboardSnapshot ? FormatCurrency(value) : fallback;
    }

    private static string BuildDisplayText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatBusinessTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return "未同步";
        }

        try
        {
            var milliseconds = timestamp < 10_000_000_000 ? timestamp * 1000 : timestamp;
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "未同步";
        }
    }

    private sealed class InventoryDashboardLoadResult
    {
        public StringNarrationInventoryManagementDashboardResult DashboardResult { get; init; } = new();
        public IReadOnlyList<StringNarrationInventoryMovement> RecentMovements { get; init; } = [];
        public string WarningMessage { get; init; } = string.Empty;
    }
}
