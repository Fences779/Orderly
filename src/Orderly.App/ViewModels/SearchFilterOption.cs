using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public enum SearchFilterKind
{
    All,
    CustomerStatus,
    CustomerPriority,
    OrderStatus,
    DealStage,
    FollowUpStatus
}

public sealed class SearchFilterOption
{
    public static SearchFilterOption All { get; } = new("全部状态", SearchFilterKind.All);

    public SearchFilterOption(string label, SearchFilterKind kind, Enum? value = null)
    {
        Label = label;
        Kind = kind;
        Value = value;
    }

    public string Label { get; }
    public SearchFilterKind Kind { get; }
    public Enum? Value { get; }
}

public enum QuickFilterKind
{
    All,
    TodayFollowUp,
    OverdueFollowUp,
    TomorrowFollowUp,
    PendingOrders,
    WonOrders
}

public sealed class QuickFilterOption
{
    public static QuickFilterOption All { get; } = new("全部", QuickFilterKind.All);

    public QuickFilterOption(string label, QuickFilterKind kind)
    {
        Label = label;
        Kind = kind;
    }

    public string Label { get; }
    public QuickFilterKind Kind { get; }
}
