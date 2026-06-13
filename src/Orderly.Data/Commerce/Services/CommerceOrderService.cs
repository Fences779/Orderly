using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IOrderService"/> over the
/// Universal_Domain_Model (Req 4.1). Industry-agnostic and free of any Forbidden_Term.
///
/// <para>
/// Task 7.1 implements order <b>recalculation</b> (Req 4.2): on create/update the order's derived
/// monetary fields (subtotal, total, cost, gross profit, paid amount, receivable) are recomputed —
/// each normalized to a scale of exactly 2 via <see cref="CommerceMoney"/> — and the gross margin
/// is recomputed as a percentage constrained to the inclusive range [0, 100] rounded to 2 decimal
/// places. Task 7.3 adds independent three-dimensional stage transitions validated against the
/// active workflow configuration (Req 4.3, 4.4, 4.5). Order completion with aggregated inventory
/// deduction (Req 4.6, 4.7, 4.16–4.19) is added by a later task (7.5).
/// </para>
/// </summary>
public sealed class CommerceOrderService : IOrderService
{
    /// <summary>The scale (decimal places) the gross margin percentage is rounded to (Req 4.2).</summary>
    private const int GrossMarginScale = 2;

    /// <summary>Inclusive lower bound of the gross margin percentage (Req 4.2).</summary>
    private const decimal MinGrossMargin = 0m;

    /// <summary>Inclusive upper bound of the gross margin percentage (Req 4.2).</summary>
    private const decimal MaxGrossMargin = 100m;

    private readonly SqliteConnectionFactory? _connectionFactory;
    private readonly ICommerceOrderRepository? _orderRepository;
    private readonly IOrderItemRepository? _orderItemRepository;
    private readonly IInventoryItemRepository? _inventoryItemRepository;
    private readonly IInventoryMovementRepository? _inventoryMovementRepository;
    private readonly ICommerceCustomerRepository? _customerRepository;

    /// <summary>
    /// Creates a service that supports the stateless calculation operations
    /// (<see cref="RecalculateOrder"/> and <see cref="ApplyStageTransition"/>) only. Calling
    /// <see cref="CompleteOrderAsync"/> on an instance created this way throws, because completion
    /// requires the persistence dependencies; use the dependency constructor for completion.
    /// </summary>
    public CommerceOrderService()
    {
    }

    /// <summary>
    /// Creates a service wired for order completion within a Core_Write_Transaction (Req 4.6, 4.7,
    /// 18.1). The transaction is opened from <paramref name="connectionFactory"/>, and every read and
    /// write flows through the supplied Commerce repositories so the operation is atomic and the
    /// P0_Security_System encrypted-connection path is preserved (C-2).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public CommerceOrderService(
        SqliteConnectionFactory connectionFactory,
        ICommerceOrderRepository orderRepository,
        IOrderItemRepository orderItemRepository,
        IInventoryItemRepository inventoryItemRepository,
        IInventoryMovementRepository inventoryMovementRepository,
        ICommerceCustomerRepository customerRepository)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _orderItemRepository = orderItemRepository ?? throw new ArgumentNullException(nameof(orderItemRepository));
        _inventoryItemRepository = inventoryItemRepository ?? throw new ArgumentNullException(nameof(inventoryItemRepository));
        _inventoryMovementRepository = inventoryMovementRepository ?? throw new ArgumentNullException(nameof(inventoryMovementRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
    }

    /// <inheritdoc />
    public void RecalculateOrder(
        Order order,
        IReadOnlyCollection<OrderItem> items,
        IReadOnlyCollection<PaymentRecord> payments)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(payments);

        // Subtotal and cost are aggregated from the line items; each line total is recomputed from
        // its unit price and quantity so persisted line totals stay consistent with the subtotal.
        decimal subtotal = 0m;
        decimal cost = 0m;
        foreach (OrderItem item in items)
        {
            CommerceMoney lineTotal = CommerceMoney.From(item.UnitPrice.Amount * item.Quantity);
            item.LineTotal = lineTotal;

            subtotal += lineTotal.Amount;
            cost += CommerceMoney.From(item.UnitCost.Amount * item.Quantity).Amount;
        }

        CommerceMoney subtotalMoney = CommerceMoney.From(subtotal);
        CommerceMoney costMoney = CommerceMoney.From(cost);

        // With no order-level adjustment fields in the universal model, the final total equals the
        // subtotal. Gross profit is total minus cost.
        CommerceMoney totalMoney = subtotalMoney;
        CommerceMoney grossProfitMoney = CommerceMoney.From(totalMoney.Amount - costMoney.Amount);

        // Paid amount is the sum of recorded payments; receivable is the unpaid remainder.
        decimal paid = 0m;
        foreach (PaymentRecord payment in payments)
        {
            paid += payment.Amount.Amount;
        }

        CommerceMoney paidMoney = CommerceMoney.From(paid);
        CommerceMoney receivableMoney = CommerceMoney.From(totalMoney.Amount - paidMoney.Amount);

        order.Subtotal = subtotalMoney;
        order.Total = totalMoney;
        order.Cost = costMoney;
        order.GrossProfit = grossProfitMoney;
        order.PaidAmount = paidMoney;
        order.ReceivableAmount = receivableMoney;
        order.GrossMargin = ComputeGrossMargin(totalMoney, grossProfitMoney);
    }

    /// <inheritdoc />
    public OrderStageTransitionResult ApplyStageTransition(
        Order order,
        OrderStageTransitionRequest request,
        OrderWorkflowConfiguration workflow)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workflow);

        // A request that names no dimension cannot be matched by any workflow transition (every
        // workflow transition names 1–3 dimensions); reject it without touching any stage (Req 4.5).
        if (!request.NamesAnyDimension)
        {
            return OrderStageTransitionResult.NotPermitted(
                "The requested transition names no stage dimension; no stage dimension was changed.");
        }

        // Validate against the active workflow BEFORE mutating any field so a rejected transition can
        // never leave a partial update (Req 4.5).
        if (!IsPermitted(workflow, order, request))
        {
            return OrderStageTransitionResult.NotPermitted();
        }

        // Apply only the dimension(s) the request names; the others are deliberately left unchanged
        // because the three dimensions are independent (Req 4.3, 4.4). Each assignment routes through
        // the order's setter so UpdatedAt advances (Req 2.8).
        if (request.TargetSalesStage is OrderSalesStage targetSales)
        {
            order.SalesStage = targetSales;
        }

        if (request.TargetPaymentStage is OrderPaymentStage targetPayment)
        {
            order.PaymentStage = targetPayment;
        }

        if (request.TargetFulfillmentStage is OrderFulfillmentStage targetFulfillment)
        {
            order.FulfillmentStage = targetFulfillment;
        }

        return OrderStageTransitionResult.Applied();
    }

    /// <inheritdoc />
    public async Task<OrderCompletionResult> CompleteOrderAsync(
        Guid orderId,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureCompletionDependencies();

        // Read the order and its lines before opening the transaction; these reads use their own
        // short-lived connections (no ambient transaction is active yet).
        Order order = await _orderRepository!.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Order '{orderId}' was not found.");

        IReadOnlyList<OrderItem> allItems =
            await _orderItemRepository!.GetAllAsync(cancellationToken).ConfigureAwait(false);

        // Aggregate the required quantity PER InventoryItemId across every inventory-linked line of
        // this order, rather than evaluating each line independently (Req 4.16). Lines with no
        // inventory link (services, custom items, products not stocked) are skipped entirely so they
        // neither block completion nor incur a deduction (Req 4.6).
        Dictionary<Guid, decimal> requiredByItem = new();
        foreach (OrderItem item in allItems)
        {
            if (item.OrderId != orderId || item.InventoryItemId is not Guid inventoryItemId)
            {
                continue;
            }

            requiredByItem.TryGetValue(inventoryItemId, out decimal runningTotal);
            requiredByItem[inventoryItemId] = runningTotal + item.Quantity;
        }

        // Everything below runs inside the single Core_Write_Transaction: either every deduction,
        // movement, order-stage change, and customer-statistics update commits together, or the whole
        // operation rolls back on disposal leaving the data unchanged (Req 18.1, 18.3). A plain
        // (synchronous) using is required so the ambient transaction is published in — and cleared
        // from — this method's own execution context, allowing the repository calls below to enlist.
        using CoreWriteTransaction transaction = CoreWriteTransaction.Begin(_connectionFactory!);

        // A deduction already recorded for an (order, item) pair (identified by its business key) is
        // never applied twice, so re-running completion produces no duplicate movements or
        // double-deduction (Req 4.20, 18.6).
        IReadOnlyList<InventoryMovement> existingMovements =
            await _inventoryMovementRepository!.GetAllAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> alreadyDeductedKeys = existingMovements
            .Where(movement => movement.BusinessKey is not null)
            .Select(movement => movement.BusinessKey!)
            .ToHashSet(StringComparer.Ordinal);

        // Evaluate availability on the per-InventoryItemId aggregates that still need deducting.
        var pendingDeductions = new List<PendingDeduction>();
        var shortfalls = new List<InventoryShortfall>();
        foreach ((Guid inventoryItemId, decimal required) in requiredByItem)
        {
            string businessKey = DeductionBusinessKey(orderId, inventoryItemId);
            if (alreadyDeductedKeys.Contains(businessKey))
            {
                // This aggregate was already deducted by a previous completion; its quantity is
                // already reflected in QuantityAvailable, so it is neither re-checked nor re-applied.
                continue;
            }

            InventoryItem inventoryItem =
                await _inventoryItemRepository!.GetByIdAsync(inventoryItemId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Inventory item '{inventoryItemId}' referenced by order '{orderId}' was not found.");

            if (required > inventoryItem.QuantityAvailable)
            {
                shortfalls.Add(new InventoryShortfall
                {
                    InventoryItemId = inventoryItemId,
                    RequiredQuantity = required,
                    QuantityAvailable = inventoryItem.QuantityAvailable,
                });
            }
            else
            {
                pendingDeductions.Add(new PendingDeduction(inventoryItem, required, businessKey));
            }
        }

        // All-or-nothing: a single shortfall rejects the entire completion. No write has been made
        // yet, and the transaction rolls back on disposal, so every quantity and statistic is
        // unchanged (Req 4.7).
        if (shortfalls.Count > 0)
        {
            return OrderCompletionResult.InsufficientInventory(shortfalls);
        }

        // Apply EXACTLY ONE deduction and ONE outbound movement per InventoryItemId, equal to that
        // item's aggregated requirement (Req 4.6, 4.17).
        foreach (PendingDeduction deduction in pendingDeductions)
        {
            InventoryItem inventoryItem = deduction.Item;
            inventoryItem.QuantityAvailable -= deduction.Required;
            await _inventoryItemRepository!.UpdateAsync(inventoryItem, cancellationToken).ConfigureAwait(false);

            var movement = new InventoryMovement
            {
                WorkspaceId = inventoryItem.WorkspaceId,
                InventoryItemId = inventoryItem.Id,
                MovementType = InventoryMovementType.Outbound,
                Quantity = deduction.Required,
                OrderId = orderId,
                OccurredAt = completedAtUtc,
                BusinessKey = deduction.BusinessKey,
            };
            await _inventoryMovementRepository!.CreateAsync(movement, cancellationToken).ConfigureAwait(false);
        }

        // Advance only the sales dimension to Completed; payment and fulfillment are independent (Req 4.3).
        order.SalesStage = OrderSalesStage.Completed;
        await _orderRepository!.UpdateAsync(order, cancellationToken).ConfigureAwait(false);

        // Refresh the linked customer's rolled-up statistics from its completed orders (Req 4.6).
        await UpdateCustomerStatisticsAsync(order, cancellationToken).ConfigureAwait(false);

        // No PaymentRecord or CashFlowEntry is created here; any that already exist for the order are
        // reused as-is, so completion never duplicates financial records (Req 4.19).
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OrderCompletionResult.Completed();
    }

    /// <summary>
    /// Recomputes the linked customer's rolled-up RFM statistics — completed-order count, total
    /// spend, and last-order timestamp — from the customer's completed orders, treating the order
    /// being completed as completed (Req 4.6, 4.11). Recomputing (rather than incrementing) keeps the
    /// update idempotent so re-running completion yields identical statistics. A no-op when the order
    /// has no linked customer or the customer record is absent.
    /// </summary>
    private async Task UpdateCustomerStatisticsAsync(Order completedOrder, CancellationToken cancellationToken)
    {
        if (completedOrder.CustomerId is not Guid customerId)
        {
            return;
        }

        Customer? customer = await _customerRepository!.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return;
        }

        IReadOnlyList<Order> orders = await _orderRepository!.GetAllAsync(cancellationToken).ConfigureAwait(false);

        List<Order> completed = orders
            .Where(order => order.CustomerId == customerId
                && (order.Id == completedOrder.Id || order.SalesStage == OrderSalesStage.Completed))
            .ToList();

        // Guard against the just-updated order not yet being visible in the reloaded set.
        if (completed.All(order => order.Id != completedOrder.Id))
        {
            completed.Add(completedOrder);
        }

        customer.CompletedOrderCount = completed.Count;
        customer.TotalSpend = CommerceMoney.From(completed.Sum(order => order.Total.Amount));
        customer.LastOrderAt = completed.Max(order => order.OrderedAt);

        await _customerRepository!.UpdateAsync(customer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The stable business key for the single inventory deduction generated when an order is
    /// completed for a given inventory item; makes the deduction idempotent across re-runs (Req 4.20,
    /// 18.6). Contains no Forbidden_Term.
    /// </summary>
    private static string DeductionBusinessKey(Guid orderId, Guid inventoryItemId)
        => $"order-completion:{orderId:N}:{inventoryItemId:N}";

    /// <summary>Verifies the persistence dependencies required for completion were supplied.</summary>
    /// <exception cref="InvalidOperationException">Thrown when any completion dependency is missing.</exception>
    private void EnsureCompletionDependencies()
    {
        if (_connectionFactory is null
            || _orderRepository is null
            || _orderItemRepository is null
            || _inventoryItemRepository is null
            || _inventoryMovementRepository is null
            || _customerRepository is null)
        {
            throw new InvalidOperationException(
                "This CommerceOrderService was created without the persistence dependencies required for order completion; use the dependency constructor.");
        }
    }

    /// <summary>One inventory item pending a single deduction during a completion.</summary>
    private readonly record struct PendingDeduction(InventoryItem Item, decimal Required, string BusinessKey);

    /// <summary>
    /// Determines whether <paramref name="workflow"/> contains a permitted transition that matches
    /// <paramref name="request"/> for <paramref name="order"/>'s current stages (Req 4.4). A workflow
    /// transition matches when it names exactly the same dimensions as the request, targets the same
    /// stage values on those dimensions, and — for each named dimension that declares a source-stage
    /// constraint — the order's current stage on that dimension satisfies it.
    /// </summary>
    private static bool IsPermitted(
        OrderWorkflowConfiguration workflow,
        Order order,
        OrderStageTransitionRequest request)
    {
        foreach (OrderStageTransition transition in workflow.Transitions)
        {
            if (Matches(transition, order, request))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests a single workflow transition against the request and the order's current stages.
    /// </summary>
    private static bool Matches(
        OrderStageTransition transition,
        Order order,
        OrderStageTransitionRequest request)
    {
        // The set of named dimensions must match exactly: a transition naming a dimension the
        // request does not (or vice versa) is not the requested transition.
        if (transition.NamesSalesStage != request.NamesSalesStage
            || transition.NamesPaymentStage != request.NamesPaymentStage
            || transition.NamesFulfillmentStage != request.NamesFulfillmentStage)
        {
            return false;
        }

        // Sales dimension: target value must match, and any source constraint must be satisfied.
        if (transition.ToSalesStage is OrderSalesStage toSales)
        {
            if (toSales != request.TargetSalesStage!.Value)
            {
                return false;
            }

            if (transition.FromSalesStage is OrderSalesStage fromSales && fromSales != order.SalesStage)
            {
                return false;
            }
        }

        // Payment dimension.
        if (transition.ToPaymentStage is OrderPaymentStage toPayment)
        {
            if (toPayment != request.TargetPaymentStage!.Value)
            {
                return false;
            }

            if (transition.FromPaymentStage is OrderPaymentStage fromPayment && fromPayment != order.PaymentStage)
            {
                return false;
            }
        }

        // Fulfillment dimension.
        if (transition.ToFulfillmentStage is OrderFulfillmentStage toFulfillment)
        {
            if (toFulfillment != request.TargetFulfillmentStage!.Value)
            {
                return false;
            }

            if (transition.FromFulfillmentStage is OrderFulfillmentStage fromFulfillment
                && fromFulfillment != order.FulfillmentStage)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Computes gross margin as <c>grossProfit / total * 100</c>, rounded to 2 decimal places and
    /// constrained to the inclusive range [0, 100] (Req 4.2). When the total is zero or negative the
    /// margin is reported as 0 because no meaningful percentage of profit can be expressed.
    /// </summary>
    private static decimal ComputeGrossMargin(CommerceMoney total, CommerceMoney grossProfit)
    {
        if (total.Amount <= 0m)
        {
            return MinGrossMargin;
        }

        decimal margin = grossProfit.Amount / total.Amount * 100m;
        margin = Math.Round(margin, GrossMarginScale, MidpointRounding.AwayFromZero);

        return Math.Clamp(margin, MinGrossMargin, MaxGrossMargin);
    }
}
