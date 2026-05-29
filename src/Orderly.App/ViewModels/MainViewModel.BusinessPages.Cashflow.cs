using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private bool _hasLoadedCashflowOnce;

    public ObservableCollection<StringNarrationCashflowEntry> CashflowEntries { get; } = new();

    public bool HasCashflowEntries => CashflowEntries.Count > 0;
    public bool HasCashflowHealthTrendItems => CashflowHealthTrendItems.Count > 0;
    public bool HasCashflowIncomeBreakdownItems => CashflowIncomeBreakdown.Items.Count > 0;
    public bool HasCashflowExpenseBreakdownItems => CashflowExpenseBreakdown.Items.Count > 0;
    public bool HasCashflowUpcomingCashItems => CashflowUpcomingCashItems.Count > 0;
    public bool HasCashflowAdviceFocusItems => CashflowAdvice.NextFocus.Count > 0;
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
    public string CashflowReceivableAmountHealthText => BuildCashflowAvailabilityCurrencyText(
        CashflowHealthSummary.ReceivableAmount,
        CashflowHealthDashboardResult.DataAvailability.Receivable,
        "未接入");
    public string CashflowPayableAmountHealthText => BuildCashflowAvailabilityCurrencyText(
        CashflowHealthSummary.PayableAmount,
        CashflowHealthDashboardResult.DataAvailability.Payable,
        "未接入");
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
    private bool isCashflowLoading;

    [ObservableProperty]
    private string cashflowError = string.Empty;

    [ObservableProperty]
    private string cashflowKeyword = string.Empty;

    [ObservableProperty]
    private StringNarrationCashflowListResult cashflowResult = new();

    [ObservableProperty]
    private StringNarrationCashflowHealthDashboardResult cashflowHealthDashboardResult = new();

    [ObservableProperty]
    private StringNarrationCashflowEntry? selectedCashflowEntry;

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

            CashflowHealthDashboardResult = await _stringNarrationBusinessService.GetCashflowHealthDashboardAsync(new StringNarrationCashflowHealthDashboardRequest
            {
                Range = "30d"
            });

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

    private string BuildCashflowHealthSummaryText()
    {
        return BuildDisplayText(
            CashflowHealthSummary.CashFlowHealthSummary,
            GetCashflowDashboardFallbackText("现金流健康看板暂不可用"));
    }

    private bool HasCashflowHealthDashboardSnapshot => CashflowHealthDashboardResult.UpdatedAt > 0
        || HasCashflowHealthTrendItems
        || CashflowHealthSummary.CashFlowHealthScore.HasValue
        || CashflowHealthSummary.CashBalanceAmount.HasValue
        || CashflowHealthSummary.AvailableCashAmount.HasValue
        || !string.IsNullOrWhiteSpace(CashflowHealthSummary.CashFlowHealthSummary);

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

    private string BuildCashflowDashboardCurrencyText(decimal? value, string afterLoadFallback)
    {
        var fallback = GetCashflowDashboardFallbackText(afterLoadFallback);
        return value.HasValue ? FormatCurrency(value) : fallback;
    }

    private string BuildCashflowAvailabilityCurrencyText(
        decimal? value,
        StringNarrationBusinessDataAvailabilityItem availability,
        string unavailableFallback)
    {
        if (availability.IsCompat)
        {
            return string.IsNullOrWhiteSpace(availability.Reason)
                ? "兼容占位"
                : $"兼容占位：{availability.Reason.Trim()}";
        }

        if (availability.IsUnavailable)
        {
            return string.IsNullOrWhiteSpace(availability.Reason)
                ? unavailableFallback
                : $"{unavailableFallback}：{availability.Reason.Trim()}";
        }

        return BuildCashflowDashboardCurrencyText(value, unavailableFallback);
    }

    private string BuildCashflowDashboardAmountText(decimal value)
    {
        var fallback = GetCashflowDashboardFallbackText("未评估");
        return HasCashflowHealthDashboardSnapshot ? FormatCurrency(value) : fallback;
    }
}
