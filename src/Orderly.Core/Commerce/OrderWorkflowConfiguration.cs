namespace Orderly.Core.Commerce;

/// <summary>
/// A single permitted stage transition defined by a workspace's active workflow configuration
/// (Req 4.4, 5.6). A transition is <b>composite</b>: it may target one, two, or all three of the
/// independent stage dimensions — <see cref="OrderSalesStage"/>, <see cref="OrderPaymentStage"/>,
/// and <see cref="OrderFulfillmentStage"/> (Req 4.3).
///
/// <para>
/// A dimension is <b>named</b> by this transition when its <c>To…</c> target value is non-null; a
/// valid transition names at least one dimension. For each named dimension an optional
/// <c>From…</c> value constrains the source stage the order must currently be in for the transition
/// to apply; when a <c>From…</c> value is null the transition permits the named dimension's change
/// from any current stage.
/// </para>
///
/// <para>Only the dimensions a transition names are ever changed; the others remain untouched
/// (Req 4.3, 4.4).</para>
/// </summary>
public sealed record OrderStageTransition
{
    /// <summary>Optional required current sales stage. Null means "from any sales stage".</summary>
    public OrderSalesStage? FromSalesStage { get; init; }

    /// <summary>Target sales stage. Non-null when this transition names the sales dimension.</summary>
    public OrderSalesStage? ToSalesStage { get; init; }

    /// <summary>Optional required current payment stage. Null means "from any payment stage".</summary>
    public OrderPaymentStage? FromPaymentStage { get; init; }

    /// <summary>Target payment stage. Non-null when this transition names the payment dimension.</summary>
    public OrderPaymentStage? ToPaymentStage { get; init; }

    /// <summary>Optional required current fulfillment stage. Null means "from any fulfillment stage".</summary>
    public OrderFulfillmentStage? FromFulfillmentStage { get; init; }

    /// <summary>Target fulfillment stage. Non-null when this transition names the fulfillment dimension.</summary>
    public OrderFulfillmentStage? ToFulfillmentStage { get; init; }

    /// <summary><c>true</c> when this transition names the sales dimension.</summary>
    public bool NamesSalesStage => ToSalesStage is not null;

    /// <summary><c>true</c> when this transition names the payment dimension.</summary>
    public bool NamesPaymentStage => ToPaymentStage is not null;

    /// <summary><c>true</c> when this transition names the fulfillment dimension.</summary>
    public bool NamesFulfillmentStage => ToFulfillmentStage is not null;

    /// <summary>The number of stage dimensions this transition targets (1, 2, or 3 when valid).</summary>
    public int NamedDimensionCount =>
        (NamesSalesStage ? 1 : 0) + (NamesPaymentStage ? 1 : 0) + (NamesFulfillmentStage ? 1 : 0);

    /// <summary><c>true</c> when this transition names at least one stage dimension.</summary>
    public bool NamesAnyDimension => NamedDimensionCount > 0;
}

/// <summary>
/// The workflow configuration governing an order's three independent stage dimensions (Req 5.6).
/// It assigns each dimension an initial stage value and enumerates the composite transitions the
/// workspace permits over those dimensions (Req 4.4, 4.5). Stage transitions requested through
/// <c>IOrderService</c> are validated against the active configuration; a transition is applied only
/// when this configuration permits it (Req 4.4) and is otherwise rejected with no partial update
/// (Req 4.5).
/// </summary>
public sealed record OrderWorkflowConfiguration
{
    /// <summary>The initial sales stage assigned to a new order (Req 5.6).</summary>
    public OrderSalesStage InitialSalesStage { get; init; } = OrderSalesStage.Draft;

    /// <summary>The initial payment stage assigned to a new order (Req 5.6).</summary>
    public OrderPaymentStage InitialPaymentStage { get; init; } = OrderPaymentStage.Unpaid;

    /// <summary>The initial fulfillment stage assigned to a new order (Req 5.6).</summary>
    public OrderFulfillmentStage InitialFulfillmentStage { get; init; } = OrderFulfillmentStage.NotStarted;

    /// <summary>
    /// The composite transitions this workflow permits (Req 4.4). Each names one, two, or all three
    /// of the independent stage dimensions. A requested transition is permitted only when one of
    /// these entries matches it; otherwise it is rejected (Req 4.5).
    /// </summary>
    public IReadOnlyList<OrderStageTransition> Transitions { get; init; } = [];
}
