using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    partial void OnStringNarrationListKeywordChanged(string value)
    {
        OnPropertyChanged(nameof(Keyword));
    }

    partial void OnSelectedStringNarrationStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(Status));
    }

    partial void OnSelectedStringNarrationFulfillmentStatusFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FulfillmentStatus));
    }

    partial void OnStringNarrationStartAtChanged(long value)
    {
        OnPropertyChanged(nameof(StartAt));
    }

    partial void OnStringNarrationEndAtChanged(long value)
    {
        OnPropertyChanged(nameof(EndAt));
    }

    partial void OnStringNarrationFulfillmentStatsChanged(StringNarrationFulfillmentStats value)
    {
        OnPropertyChanged(nameof(Stats));
        OnPropertyChanged(nameof(StringNarrationStatsCalculatedAtText));
    }

    public string Keyword
    {
        get => StringNarrationListKeyword;
        set => StringNarrationListKeyword = value ?? string.Empty;
    }

    public string Status
    {
        get => SelectedStringNarrationStatusFilter;
        set => SelectedStringNarrationStatusFilter = string.IsNullOrWhiteSpace(value) ? "全部" : value.Trim();
    }

    public string FulfillmentStatus
    {
        get => SelectedStringNarrationFulfillmentStatusFilter;
        set => SelectedStringNarrationFulfillmentStatusFilter = string.IsNullOrWhiteSpace(value) ? "全部" : value.Trim();
    }

    public long StartAt
    {
        get => StringNarrationStartAt;
        set => StringNarrationStartAt = value;
    }

    public long EndAt
    {
        get => StringNarrationEndAt;
        set => StringNarrationEndAt = value;
    }

    public StringNarrationFulfillmentStats Stats => StringNarrationFulfillmentStats;

    public string StringNarrationStatsCalculatedAtText => StringNarrationFulfillmentStats is null || StringNarrationFulfillmentStats.CalculatedAt <= 0
        ? "未同步"
        : FormatGatewayTime(StringNarrationFulfillmentStats.CalculatedAt);

    public IAsyncRelayCommand LoadOrders => LoadStringNarrationOrdersCommand;
    public IAsyncRelayCommand LoadStats => LoadStringNarrationStatsCommand;
    public IAsyncRelayCommand RefreshDetail => RefreshStringNarrationOrderDetailCommand;
    public IAsyncRelayCommand UpdateFulfillment => UpdateStringNarrationFulfillmentCommand;
    public IAsyncRelayCommand GenerateProductionOrder => GenerateStringNarrationProductionOrderCommand;
}
