namespace Orderly.Core.Commerce.Services;

/// <summary>
/// A requested order stage transition: the target stage value for each dimension the caller wants
/// to change (Req 4.4). A dimension is <b>named</b> by the request when its <c>Target…</c> value is
/// non-null; the dimensions whose targets are null are left untouched (Req 4.3). A valid request
/// names at least one of the three independent dimensions.
/// </summary>
public sealed record OrderStageTransitionRequest
{
    /// <summary>The desired sales stage. Non-null when the request names the sales dimension.</summary>
    public OrderSalesStage? TargetSalesStage { get; init; }

    /// <summary>The desired payment stage. Non-null when the request names the payment dimension.</summary>
    public OrderPaymentStage? TargetPaymentStage { get; init; }

    /// <summary>The desired fulfillment stage. Non-null when the request names the fulfillment dimension.</summary>
    public OrderFulfillmentStage? TargetFulfillmentStage { get; init; }

    /// <summary><c>true</c> when the request names the sales dimension.</summary>
    public bool NamesSalesStage => TargetSalesStage is not null;

    /// <summary><c>true</c> when the request names the payment dimension.</summary>
    public bool NamesPaymentStage => TargetPaymentStage is not null;

    /// <summary><c>true</c> when the request names the fulfillment dimension.</summary>
    public bool NamesFulfillmentStage => TargetFulfillmentStage is not null;

    /// <summary>The number of stage dimensions this request targets (1, 2, or 3 when valid).</summary>
    public int NamedDimensionCount =>
        (NamesSalesStage ? 1 : 0) + (NamesPaymentStage ? 1 : 0) + (NamesFulfillmentStage ? 1 : 0);

    /// <summary><c>true</c> when the request names at least one stage dimension.</summary>
    public bool NamesAnyDimension => NamedDimensionCount > 0;
}

/// <summary>The outcome of an attempted order stage transition (Req 4.4, 4.5).</summary>
public enum OrderStageTransitionOutcome
{
    /// <summary>The transition was permitted by the active workflow and applied (Req 4.4).</summary>
    Applied = 0,

    /// <summary>
    /// The transition was not permitted by the active workflow; all three stage dimensions were
    /// left unchanged with no partial update (Req 4.5).
    /// </summary>
    TransitionNotPermitted = 1
}

/// <summary>
/// The result of <see cref="IOrderService.ApplyStageTransition"/>. Distinguishes a successfully
/// applied transition from a rejected one (Req 4.4, 4.5), following the typed-result convention used
/// across the Commerce Service Layer.
/// </summary>
public sealed record OrderStageTransitionResult
{
    /// <summary>The outcome of the transition attempt.</summary>
    public OrderStageTransitionOutcome Outcome { get; init; }

    /// <summary>An optional neutral, human-readable explanation of the outcome.</summary>
    public string? Message { get; init; }

    /// <summary><c>true</c> when the transition was permitted and applied.</summary>
    public bool IsApplied => Outcome == OrderStageTransitionOutcome.Applied;

    /// <summary>
    /// <c>true</c> when the transition was rejected because the active workflow does not permit it;
    /// in this case the order's stage dimensions are unchanged (Req 4.5).
    /// </summary>
    public bool IsNotPermitted => Outcome == OrderStageTransitionOutcome.TransitionNotPermitted;

    /// <summary>Creates the canonical "applied" result (Req 4.4).</summary>
    public static OrderStageTransitionResult Applied(string? message = null) => new()
    {
        Outcome = OrderStageTransitionOutcome.Applied,
        Message = message
    };

    /// <summary>Creates the canonical "transition not permitted" result (Req 4.5).</summary>
    public static OrderStageTransitionResult NotPermitted(string? message = null) => new()
    {
        Outcome = OrderStageTransitionOutcome.TransitionNotPermitted,
        Message = message ?? "The requested stage transition is not permitted by the active workflow; no stage dimension was changed."
    };
}

/// <summary>The outcome of an attempted order completion (Req 4.6, 4.7).</summary>
public enum OrderCompletionOutcome
{
    /// <summary>
    /// Every aggregated per-<c>InventoryItemId</c> requirement was satisfied; the inventory
    /// deductions, movements, and customer-statistics updates were applied and committed within the
    /// Core_Write_Transaction (Req 4.6, 4.16, 4.17).
    /// </summary>
    Completed = 0,

    /// <summary>
    /// At least one aggregated per-<c>InventoryItemId</c> requirement exceeded the item's available
    /// quantity; the completion was rejected and the entire transaction rolled back with no partial
    /// update (Req 4.7).
    /// </summary>
    InsufficientInventory = 1
}

/// <summary>
/// One inventory item whose aggregated required quantity exceeded its available quantity, explaining
/// an <see cref="OrderCompletionOutcome.InsufficientInventory"/> rejection (Req 4.7).
/// </summary>
public sealed record InventoryShortfall
{
    /// <summary>The inventory item that was short.</summary>
    public Guid InventoryItemId { get; init; }

    /// <summary>The required quantity aggregated across all order items referencing this inventory item (Req 4.16).</summary>
    public decimal RequiredQuantity { get; init; }

    /// <summary>The quantity available against which the requirement was evaluated.</summary>
    public decimal QuantityAvailable { get; init; }
}

/// <summary>
/// The result of <see cref="IOrderService.CompleteOrderAsync"/>. Distinguishes a committed
/// completion from an insufficient-inventory rejection (Req 4.6, 4.7), following the typed-result
/// convention used across the Commerce Service Layer.
/// </summary>
public sealed record OrderCompletionResult
{
    /// <summary>The outcome of the completion attempt.</summary>
    public OrderCompletionOutcome Outcome { get; init; }

    /// <summary>An optional neutral, human-readable explanation of the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// The per-item shortfalls when the completion was rejected for insufficient inventory; empty for
    /// a successful completion.
    /// </summary>
    public IReadOnlyList<InventoryShortfall> Shortfalls { get; init; } = Array.Empty<InventoryShortfall>();

    /// <summary><c>true</c> when the completion was applied and committed.</summary>
    public bool IsCompleted => Outcome == OrderCompletionOutcome.Completed;

    /// <summary>
    /// <c>true</c> when the completion was rejected because an aggregated requirement exceeded
    /// availability; in this case all inventory quantities and customer statistics are unchanged (Req 4.7).
    /// </summary>
    public bool IsInsufficientInventory => Outcome == OrderCompletionOutcome.InsufficientInventory;

    /// <summary>Creates the canonical "completed" result (Req 4.6).</summary>
    public static OrderCompletionResult Completed(string? message = null) => new()
    {
        Outcome = OrderCompletionOutcome.Completed,
        Message = message
    };

    /// <summary>Creates the canonical "insufficient inventory" result (Req 4.7).</summary>
    public static OrderCompletionResult InsufficientInventory(
        IReadOnlyList<InventoryShortfall> shortfalls,
        string? message = null) => new()
    {
        Outcome = OrderCompletionOutcome.InsufficientInventory,
        Shortfalls = shortfalls,
        Message = message ?? "库存不足，订单无法完成；所有库存数量与客户统计均未改变。"
    };
}

/// <summary>
/// Commerce Service Layer contract for <see cref="Order"/> operations over the
/// Universal_Domain_Model (Req 4.1). This interface is industry-agnostic and free of any
/// Forbidden_Term.
///
/// <para>
/// Task 7.1 introduces order <b>recalculation</b> (Req 4.2). Task 7.3 adds independent
/// three-dimensional stage transitions (Req 4.3–4.5). Order completion with aggregated inventory
/// deduction inside a Core_Write_Transaction (Req 4.6, 4.7, 4.16–4.19) is added by a later task
/// (7.5) and extends this same interface.
/// </para>
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Recomputes and applies an order's derived monetary fields and gross margin from its line
    /// items and recorded payments (Req 4.2). This is the calculation that runs whenever an order
    /// is created or updated.
    ///
    /// <para>The following are recomputed and assigned to <paramref name="order"/>, each monetary
    /// value normalized to a scale of exactly 2 decimal places via <see cref="CommerceMoney"/>:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="Order.Subtotal"/> — the sum of every line total (unit price × quantity).</description></item>
    ///   <item><description><see cref="Order.Total"/> — the final order total.</description></item>
    ///   <item><description><see cref="Order.Cost"/> — the sum of every line cost (unit cost × quantity).</description></item>
    ///   <item><description><see cref="Order.GrossProfit"/> — total minus cost.</description></item>
    ///   <item><description><see cref="Order.PaidAmount"/> — the sum of the supplied payment amounts.</description></item>
    ///   <item><description><see cref="Order.ReceivableAmount"/> — total minus paid amount.</description></item>
    /// </list>
    ///
    /// <para><see cref="Order.GrossMargin"/> is recomputed as a percentage constrained to the
    /// inclusive range [0, 100] and rounded to 2 decimal places (Req 4.2).</para>
    ///
    /// <para>Each line's <see cref="OrderItem.LineTotal"/> is also recomputed from its unit price
    /// and quantity so the persisted line totals stay consistent with the recomputed subtotal.</para>
    /// </summary>
    /// <param name="order">The order whose derived fields are recomputed and updated in place.</param>
    /// <param name="items">The order's line items. An empty collection yields zero monetary totals.</param>
    /// <param name="payments">The payments recorded against the order. An empty collection yields a zero paid amount.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="order"/>, <paramref name="items"/>, or <paramref name="payments"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a recomputed monetary aggregate falls outside the valid <see cref="CommerceMoney"/> range.</exception>
    void RecalculateOrder(
        Order order,
        IReadOnlyCollection<OrderItem> items,
        IReadOnlyCollection<PaymentRecord> payments);

    /// <summary>
    /// Applies a requested stage transition to <paramref name="order"/> over its three independent
    /// stage dimensions — <see cref="Order.SalesStage"/>, <see cref="Order.PaymentStage"/>, and
    /// <see cref="Order.FulfillmentStage"/> — validated against the active
    /// <paramref name="workflow"/> (Req 4.3, 4.4, 4.5).
    ///
    /// <para>The request names one, two, or all three dimensions (its non-null
    /// <c>Target…</c> values). The transition is applied only when the active workflow contains a
    /// permitted transition that targets exactly the named dimensions, sets them to the requested
    /// target values, and whose source-stage constraints are satisfied by the order's current
    /// stages. When applied, only the named dimensions are updated to their target values and the
    /// remaining dimension(s) are left unchanged (Req 4.3, 4.4); the result reports
    /// <see cref="OrderStageTransitionOutcome.Applied"/>.</para>
    ///
    /// <para>When the workflow does not permit the requested transition — including a request that
    /// names no dimension — all three stage dimensions are left unchanged with no partial update and
    /// the result reports <see cref="OrderStageTransitionOutcome.TransitionNotPermitted"/>
    /// (Req 4.5). Because the request is fully validated before any field is mutated, a rejected
    /// transition never produces a partial update.</para>
    /// </summary>
    /// <param name="order">The order whose stage dimensions are transitioned in place when permitted.</param>
    /// <param name="request">The requested transition naming the target stage value per dimension to change.</param>
    /// <param name="workflow">The active workflow configuration that defines the permitted transitions.</param>
    /// <returns>An <see cref="OrderStageTransitionResult"/> indicating whether the transition was applied or rejected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="order"/>, <paramref name="request"/>, or <paramref name="workflow"/> is null.</exception>
    OrderStageTransitionResult ApplyStageTransition(
        Order order,
        OrderStageTransitionRequest request,
        OrderWorkflowConfiguration workflow);

    /// <summary>
    /// Completes the order identified by <paramref name="orderId"/>, applying its inventory
    /// deductions and customer-statistics update atomically within a single Core_Write_Transaction
    /// (Req 4.6, 4.7, 4.16, 4.17, 4.19, 18.1–18.5).
    ///
    /// <para><b>Aggregation (Req 4.16).</b> The required quantity is aggregated <i>per</i>
    /// <see cref="OrderItem.InventoryItemId"/> across every inventory-linked line on the order, rather
    /// than evaluating each line independently. Lines not linked to an inventory item (services,
    /// custom items, products not stocked) take no part in the availability check or the deduction
    /// and never block completion (Req 4.6).</para>
    ///
    /// <para><b>Availability and deduction (Req 4.6, 4.17).</b> When every aggregated requirement is
    /// less than or equal to the corresponding inventory item's available quantity, the operation
    /// applies <i>exactly one</i> deduction per inventory item equal to its aggregated requirement,
    /// records one outbound <see cref="InventoryMovement"/> per inventory item, advances the order's
    /// <see cref="Order.SalesStage"/> to <see cref="OrderSalesStage.Completed"/>, and refreshes the
    /// linked customer's rolled-up statistics — all committed together. Re-running completion is
    /// idempotent: deductions already recorded (identified by a stable business key) are not applied
    /// again, so no inventory is double-deducted (Req 4.20, 18.6).</para>
    ///
    /// <para><b>Rejection and rollback (Req 4.7, 18.3).</b> If any aggregated requirement exceeds its
    /// item's available quantity, the completion is rejected, the entire transaction is rolled back so
    /// all inventory quantities and customer statistics remain unchanged with no partial update, and
    /// the result reports <see cref="OrderCompletionOutcome.InsufficientInventory"/> with the offending
    /// items.</para>
    ///
    /// <para><b>Financial reuse (Req 4.19).</b> Completion never creates a duplicate
    /// <see cref="PaymentRecord"/> or <see cref="CashFlowEntry"/>; any financial records that already
    /// exist for the order are reused as-is.</para>
    /// </summary>
    /// <param name="orderId">The identity of the order to complete.</param>
    /// <param name="completedAtUtc">The UTC instant recorded on generated inventory movements.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="OrderCompletionResult"/> reporting either a committed completion or an
    /// insufficient-inventory rejection.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the order does not exist, a referenced inventory item is missing, or the service
    /// was constructed without the persistence dependencies completion requires.
    /// </exception>
    Task<OrderCompletionResult> CompleteOrderAsync(
        Guid orderId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default);
}
