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
    public int InventoryLowStockCount => InventoryItems.Count(item => item.IsLowStock);
    public decimal InventoryTotalStock => InventoryItems.Sum(item => item.StockOnHand);
    public decimal InventoryAvailableStock => InventoryItems.Sum(item => item.AvailableStock);
    public decimal InventoryTotalValue => InventoryItems.Sum(item => item.InventoryValue);
    public string InventoryItemsCountText => $"{InventoryItems.Count} 个 SKU";
    public string InventoryLowStockCountText => $"{InventoryLowStockCount} 项";
    public string InventoryTotalStockText => $"{InventoryTotalStock:0.##}";
    public string InventoryAvailableStockText => $"{InventoryAvailableStock:0.##}";
    public string InventoryTotalValueText => FormatCurrency(InventoryTotalValue);
    public string InventoryUpdatedAtText => InventoryResult.UpdatedAt <= 0 ? "未同步" : FormatBusinessTimestamp(InventoryResult.UpdatedAt);
    public string InventoryEmptyStateText => string.IsNullOrWhiteSpace(InventoryError)
        ? "暂无库存数据，点击刷新从 adminPcGateway 拉取。"
        : InventoryError;
    public StringNarrationCashflowSummary CashflowSummary => CashflowResult.Summary;
    public string CashflowIncomeTotalText => CashflowSummary.IncomeTotalText;
    public string CashflowExpenseTotalText => CashflowSummary.ExpenseTotalText;
    public string CashflowNetAmountText => CashflowSummary.NetAmountText;
    public string CashflowReceivableAmountText => CashflowSummary.ReceivableAmountText;
    public string CashflowPayableAmountText => CashflowSummary.PayableAmountText;
    public string CashflowEntriesCountText => $"{CashflowEntries.Count} 条";
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
    private StringNarrationInventoryListResult inventoryResult = new();

    [ObservableProperty]
    private StringNarrationCashflowListResult cashflowResult = new();

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
            var result = await _stringNarrationBusinessService.GetInventoryAsync(new StringNarrationInventoryQuery
            {
                Keyword = InventoryKeyword,
                IncludeDisabled = true,
                PageSize = 100
            });

            InventoryResult = result;
            ReplaceCollection(InventoryItems, result.Items.OrderBy(item => item.IsLowStock ? 0 : 1).ThenBy(item => item.Category).ThenBy(item => item.Name));
            ReplaceCollection(InventoryRecentMovements, result.RecentMovements.OrderByDescending(item => item.OccurredAt).Take(20));
            SelectedInventoryItem = InventoryItems.FirstOrDefault();
            _hasLoadedInventoryOnce = true;
            StatusMessage = $"已加载库存：{InventoryItems.Count} 个 SKU";
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
            var result = await _stringNarrationBusinessService.GetCashflowAsync(new StringNarrationCashflowQuery
            {
                Keyword = CashflowKeyword,
                PageSize = 100
            });

            CashflowResult = result;
            ReplaceCollection(CashflowEntries, result.Entries.OrderByDescending(item => item.OccurredAt));
            SelectedCashflowEntry = CashflowEntries.FirstOrDefault();
            _hasLoadedCashflowOnce = true;
            StatusMessage = $"已加载现金流：{CashflowEntries.Count} 条";
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

    private void NotifyInventoryStateChanged()
    {
        OnPropertyChanged(nameof(HasInventoryItems));
        OnPropertyChanged(nameof(HasInventoryRecentMovements));
        OnPropertyChanged(nameof(InventoryLowStockCount));
        OnPropertyChanged(nameof(InventoryTotalStock));
        OnPropertyChanged(nameof(InventoryAvailableStock));
        OnPropertyChanged(nameof(InventoryTotalValue));
        OnPropertyChanged(nameof(InventoryItemsCountText));
        OnPropertyChanged(nameof(InventoryLowStockCountText));
        OnPropertyChanged(nameof(InventoryTotalStockText));
        OnPropertyChanged(nameof(InventoryAvailableStockText));
        OnPropertyChanged(nameof(InventoryTotalValueText));
        OnPropertyChanged(nameof(InventoryUpdatedAtText));
        OnPropertyChanged(nameof(InventoryEmptyStateText));
    }

    private void NotifyCashflowStateChanged()
    {
        OnPropertyChanged(nameof(HasCashflowEntries));
        OnPropertyChanged(nameof(CashflowSummary));
        OnPropertyChanged(nameof(CashflowIncomeTotalText));
        OnPropertyChanged(nameof(CashflowExpenseTotalText));
        OnPropertyChanged(nameof(CashflowNetAmountText));
        OnPropertyChanged(nameof(CashflowReceivableAmountText));
        OnPropertyChanged(nameof(CashflowPayableAmountText));
        OnPropertyChanged(nameof(CashflowEntriesCountText));
        OnPropertyChanged(nameof(CashflowUpdatedAtText));
        OnPropertyChanged(nameof(CashflowEmptyStateText));
    }

    private static string FormatCurrency(decimal value)
    {
        return value == 0 ? "¥0" : $"¥{value:N0}";
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
}
