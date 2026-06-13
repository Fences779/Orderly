using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>
/// One row of the Workbench 7-day trend series, projected from a <see cref="DashboardTrendPoint"/>.
/// </summary>
public sealed record WorkbenchTrendRow(
    DateOnly Date,
    int CompletedOrderCount,
    string Revenue,
    string CashInflow,
    string CashOutflow);

/// <summary>
/// Dedicated ViewModel for the Workbench (工作台) page. Sources dashboard metrics and the dense
/// 7-day trend series exclusively from <see cref="IDashboardService"/> (Req 6.2, 7.3); it never
/// calls a legacy remote service or binds to a legacy aggregation property.
/// </summary>
public sealed partial class WorkbenchPageViewModel : CommercePageViewModel
{
    private readonly IDashboardService _dashboardService;

    [ObservableProperty]
    private int _totalOrders;

    [ObservableProperty]
    private int _completedOrders;

    [ObservableProperty]
    private string _totalRevenue = "0.00";

    [ObservableProperty]
    private string _grossProfit = "0.00";

    [ObservableProperty]
    private string _outstandingReceivable = "0.00";

    [ObservableProperty]
    private string _netCashFlow = "0.00";

    [ObservableProperty]
    private int _customerCount;

    [ObservableProperty]
    private int _lowStockItemCount;

    private bool _hasSnapshot;

    /// <summary>Creates the Workbench page ViewModel over the dashboard service.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dashboardService"/> is null.</exception>
    public WorkbenchPageViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionWorkbench;

    /// <summary>The dense 7-day dashboard trend series (Req 4.13).</summary>
    public ObservableCollection<WorkbenchTrendRow> Trend { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => !_hasSnapshot;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        DashboardSnapshot snapshot = await _dashboardService
            .GetSnapshotAsync(DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(true);

        DashboardMetrics metrics = snapshot.Metrics;
        TotalOrders = metrics.TotalOrders;
        CompletedOrders = metrics.CompletedOrders;
        TotalRevenue = metrics.TotalRevenue.ToString();
        GrossProfit = metrics.GrossProfit.ToString();
        OutstandingReceivable = metrics.OutstandingReceivable.ToString();
        NetCashFlow = metrics.NetCashFlow.ToString();
        CustomerCount = metrics.CustomerCount;
        LowStockItemCount = metrics.LowStockItemCount;

        Trend.Clear();
        foreach (DashboardTrendPoint point in snapshot.Trend)
        {
            Trend.Add(new WorkbenchTrendRow(
                point.Date,
                point.CompletedOrderCount,
                point.Revenue.ToString(),
                point.CashInflow.ToString(),
                point.CashOutflow.ToString()));
        }

        _hasSnapshot = true;
        NotifyEmptyStateChanged();
    }
}
