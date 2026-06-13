using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>One row of the Cash Flow page, projected from a Universal_Domain_Model <see cref="CashFlowEntry"/>.</summary>
public sealed record CashFlowRow(
    Guid Id,
    CashFlowDirection Direction,
    string Amount,
    CashFlowSettlementStatus SettlementStatus,
    DateTime OccurredAt,
    string? CategoryName);

/// <summary>
/// Dedicated ViewModel for the Cash Flow (现金流) page. The period summary, health score, and recent
/// entries are sourced exclusively from <see cref="ICashFlowService"/> and the Commerce
/// <see cref="ICashFlowEntryRepository"/> (Req 6.7, 7.3); no legacy remote service is invoked.
/// </summary>
public sealed partial class CashflowPageViewModel : CommercePageViewModel
{
    /// <summary>The trailing window, in days, summarized by the Cash Flow page.</summary>
    private const int SummaryWindowDays = 30;

    private readonly ICashFlowService _cashFlowService;
    private readonly ICashFlowEntryRepository _cashFlowEntryRepository;

    [ObservableProperty]
    private string _realizedIncome = "0.00";

    [ObservableProperty]
    private string _realizedExpense = "0.00";

    [ObservableProperty]
    private string _netCashFlow = "0.00";

    [ObservableProperty]
    private string _outstandingReceivable = "0.00";

    [ObservableProperty]
    private string _outstandingPayable = "0.00";

    [ObservableProperty]
    private int _healthScore;

    /// <summary>Creates the Cash Flow page ViewModel over the cash-flow service and entry repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public CashflowPageViewModel(
        ICashFlowService cashFlowService,
        ICashFlowEntryRepository cashFlowEntryRepository)
    {
        _cashFlowService = cashFlowService ?? throw new ArgumentNullException(nameof(cashFlowService));
        _cashFlowEntryRepository = cashFlowEntryRepository ?? throw new ArgumentNullException(nameof(cashFlowEntryRepository));
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionCashflow;

    /// <summary>The most recent cash-flow entries within the summarized window.</summary>
    public ObservableCollection<CashFlowRow> Entries { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Entries.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        var period = new DateRange(nowUtc.AddDays(-SummaryWindowDays), nowUtc);

        CashFlowPeriodSummary summary = await _cashFlowService
            .GetPeriodSummaryAsync(period, cancellationToken)
            .ConfigureAwait(true);

        RealizedIncome = summary.RealizedIncome.ToString();
        RealizedExpense = summary.RealizedExpense.ToString();
        NetCashFlow = summary.NetCashFlow.ToString();
        OutstandingReceivable = summary.OutstandingReceivable.ToString();
        OutstandingPayable = summary.OutstandingPayable.ToString();
        HealthScore = summary.HealthScore;

        IReadOnlyList<CashFlowEntry> entries = await _cashFlowEntryRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(true);

        Entries.Clear();
        foreach (CashFlowEntry entry in entries
            .Where(e => period.Contains(e.OccurredAt))
            .OrderByDescending(e => e.OccurredAt))
        {
            Entries.Add(new CashFlowRow(
                entry.Id,
                entry.Direction,
                entry.Amount.ToString(),
                entry.SettlementStatus,
                entry.OccurredAt,
                entry.CategoryName));
        }

        NotifyEmptyStateChanged();
    }
}
