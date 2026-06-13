namespace Orderly.Core.Commerce;

/// <summary>
/// A point-in-time snapshot of a business metric owned by a single workspace (Req 2.2). Used to
/// build dashboard trend series (Req 4.13). The metric key and capture moment are fixed at
/// creation; the optional <see cref="BusinessKey"/> makes generation idempotent so re-running a
/// refresh produces no duplicates (Req 4.20, 18.6). The mutable values advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class BusinessMetricSnapshot : WorkspaceScopedEntity
{
    private decimal _numericValue;
    private CommerceMoney? _moneyValue;

    /// <summary>Stable identifier of the captured metric (for example a revenue or order-count metric). Fixed at creation.</summary>
    public string MetricKey { get; init; } = string.Empty;

    /// <summary>The UTC moment the metric value was captured. Fixed at creation.</summary>
    public DateTime CapturedAt { get; init; }

    /// <summary>Numeric value of the metric at capture time.</summary>
    public decimal NumericValue
    {
        get => _numericValue;
        set { _numericValue = value; MarkUpdated(); }
    }

    /// <summary>Optional monetary value of the metric at capture time. Monetary, scale 2.</summary>
    public CommerceMoney? MoneyValue
    {
        get => _moneyValue;
        set { _moneyValue = value; MarkUpdated(); }
    }

    /// <summary>Stable business key used for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }
}
