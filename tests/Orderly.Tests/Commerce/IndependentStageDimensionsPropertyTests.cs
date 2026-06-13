using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Property-based tests for the independence of an order's three stage dimensions in
/// <see cref="CommerceOrderService.ApplyStageTransition"/>.
///
/// Property 8: Order stage dimensions are independent.
/// For ANY order and ANY single-dimension stage action permitted by the active workflow, applying
/// that action changes only the targeted dimension and leaves the other two of
/// <see cref="OrderSalesStage"/>, <see cref="OrderPaymentStage"/>, and
/// <see cref="OrderFulfillmentStage"/> unchanged.
///
/// **Validates: Requirements 4.3**
/// </summary>
public class IndependentStageDimensionsPropertyTests
{
    /// <summary>The three independent stage dimensions a single-dimension action may target.</summary>
    private enum StageDimension
    {
        Sales = 0,
        Payment = 1,
        Fulfillment = 2,
    }

    // Generators over the full value set of each independent stage enum. Targets are drawn from the
    // same sets, so the generated target may coincide with the order's current stage on that
    // dimension; the property must still hold in that degenerate case.
    private static readonly Gen<OrderSalesStage> SalesGen = Gen.OneOfConst(
        OrderSalesStage.Draft,
        OrderSalesStage.Quoted,
        OrderSalesStage.Confirmed,
        OrderSalesStage.Completed,
        OrderSalesStage.Cancelled);

    private static readonly Gen<OrderPaymentStage> PaymentGen = Gen.OneOfConst(
        OrderPaymentStage.Unpaid,
        OrderPaymentStage.PartiallyPaid,
        OrderPaymentStage.Paid,
        OrderPaymentStage.Refunded);

    private static readonly Gen<OrderFulfillmentStage> FulfillmentGen = Gen.OneOfConst(
        OrderFulfillmentStage.NotStarted,
        OrderFulfillmentStage.InProgress,
        OrderFulfillmentStage.Ready,
        OrderFulfillmentStage.Fulfilled,
        OrderFulfillmentStage.Returned);

    private static readonly Gen<StageDimension> DimensionGen = Gen.OneOfConst(
        StageDimension.Sales,
        StageDimension.Payment,
        StageDimension.Fulfillment);

    /// <summary>
    /// A complete single-dimension-action scenario: the order's arbitrary current stages, which one
    /// of the three dimensions the action targets, a target value for each dimension (only the one
    /// matching <see cref="Dimension"/> is used), and whether the permitting workflow transition
    /// declares an explicit source-stage constraint (anchored to the current stage so it still
    /// permits the action).
    /// </summary>
    private sealed record Scenario(
        OrderSalesStage CurrentSales,
        OrderPaymentStage CurrentPayment,
        OrderFulfillmentStage CurrentFulfillment,
        StageDimension Dimension,
        OrderSalesStage TargetSales,
        OrderPaymentStage TargetPayment,
        OrderFulfillmentStage TargetFulfillment,
        bool UseFromConstraint);

    private static readonly Gen<Scenario> ScenarioGen =
        from currentSales in SalesGen
        from currentPayment in PaymentGen
        from currentFulfillment in FulfillmentGen
        from dimension in DimensionGen
        from targetSales in SalesGen
        from targetPayment in PaymentGen
        from targetFulfillment in FulfillmentGen
        from useFrom in Gen.Bool
        select new Scenario(
            currentSales,
            currentPayment,
            currentFulfillment,
            dimension,
            targetSales,
            targetPayment,
            targetFulfillment,
            useFrom);

    [Fact]
    public void Property8_single_dimension_action_changes_only_the_targeted_dimension()
    {
        var service = new CommerceOrderService();

        ScenarioGen.Sample(
            scenario =>
            {
                var order = new Order
                {
                    WorkspaceId = Guid.NewGuid(),
                    SalesStage = scenario.CurrentSales,
                    PaymentStage = scenario.CurrentPayment,
                    FulfillmentStage = scenario.CurrentFulfillment,
                };

                // Capture the pre-action stages so the two untouched dimensions can be verified.
                OrderSalesStage originalSales = order.SalesStage;
                OrderPaymentStage originalPayment = order.PaymentStage;
                OrderFulfillmentStage originalFulfillment = order.FulfillmentStage;

                (OrderStageTransitionRequest request, OrderWorkflowConfiguration workflow) =
                    BuildSingleDimensionAction(scenario);

                OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

                // The action is permitted by construction, so it must apply.
                Assert.True(result.IsApplied, $"Expected the permitted single-dimension action to apply, got {result.Outcome}.");

                switch (scenario.Dimension)
                {
                    case StageDimension.Sales:
                        // Only the sales dimension changes; payment and fulfillment are untouched.
                        Assert.Equal(scenario.TargetSales, order.SalesStage);
                        Assert.Equal(originalPayment, order.PaymentStage);
                        Assert.Equal(originalFulfillment, order.FulfillmentStage);
                        break;

                    case StageDimension.Payment:
                        Assert.Equal(scenario.TargetPayment, order.PaymentStage);
                        Assert.Equal(originalSales, order.SalesStage);
                        Assert.Equal(originalFulfillment, order.FulfillmentStage);
                        break;

                    case StageDimension.Fulfillment:
                        Assert.Equal(scenario.TargetFulfillment, order.FulfillmentStage);
                        Assert.Equal(originalSales, order.SalesStage);
                        Assert.Equal(originalPayment, order.PaymentStage);
                        break;
                }
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// Builds a request that names exactly one dimension and a workflow whose single permitted
    /// transition names that same dimension with the scenario's target value, so the action is
    /// guaranteed permitted. When the scenario asks for a source constraint, it is anchored to the
    /// order's current stage on that dimension so the transition still matches.
    /// </summary>
    private static (OrderStageTransitionRequest Request, OrderWorkflowConfiguration Workflow) BuildSingleDimensionAction(
        Scenario scenario)
    {
        OrderStageTransitionRequest request;
        OrderStageTransition transition;

        switch (scenario.Dimension)
        {
            case StageDimension.Sales:
                request = new OrderStageTransitionRequest { TargetSalesStage = scenario.TargetSales };
                transition = new OrderStageTransition
                {
                    ToSalesStage = scenario.TargetSales,
                    FromSalesStage = scenario.UseFromConstraint ? scenario.CurrentSales : null,
                };
                break;

            case StageDimension.Payment:
                request = new OrderStageTransitionRequest { TargetPaymentStage = scenario.TargetPayment };
                transition = new OrderStageTransition
                {
                    ToPaymentStage = scenario.TargetPayment,
                    FromPaymentStage = scenario.UseFromConstraint ? scenario.CurrentPayment : null,
                };
                break;

            case StageDimension.Fulfillment:
            default:
                request = new OrderStageTransitionRequest { TargetFulfillmentStage = scenario.TargetFulfillment };
                transition = new OrderStageTransition
                {
                    ToFulfillmentStage = scenario.TargetFulfillment,
                    FromFulfillmentStage = scenario.UseFromConstraint ? scenario.CurrentFulfillment : null,
                };
                break;
        }

        var workflow = new OrderWorkflowConfiguration { Transitions = [transition] };
        return (request, workflow);
    }

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Sales_only_action_leaves_payment_and_fulfillment_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Draft,
            PaymentStage = OrderPaymentStage.PartiallyPaid,
            FulfillmentStage = OrderFulfillmentStage.InProgress,
        };

        var request = new OrderStageTransitionRequest { TargetSalesStage = OrderSalesStage.Confirmed };
        var workflow = new OrderWorkflowConfiguration
        {
            Transitions = [new OrderStageTransition { ToSalesStage = OrderSalesStage.Confirmed }],
        };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsApplied);
        Assert.Equal(OrderSalesStage.Confirmed, order.SalesStage);
        Assert.Equal(OrderPaymentStage.PartiallyPaid, order.PaymentStage);
        Assert.Equal(OrderFulfillmentStage.InProgress, order.FulfillmentStage);
    }

    [Fact]
    public void Payment_only_action_leaves_sales_and_fulfillment_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Confirmed,
            PaymentStage = OrderPaymentStage.Unpaid,
            FulfillmentStage = OrderFulfillmentStage.Ready,
        };

        var request = new OrderStageTransitionRequest { TargetPaymentStage = OrderPaymentStage.Paid };
        var workflow = new OrderWorkflowConfiguration
        {
            Transitions =
            [
                new OrderStageTransition
                {
                    FromPaymentStage = OrderPaymentStage.Unpaid,
                    ToPaymentStage = OrderPaymentStage.Paid,
                },
            ],
        };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsApplied);
        Assert.Equal(OrderPaymentStage.Paid, order.PaymentStage);
        Assert.Equal(OrderSalesStage.Confirmed, order.SalesStage);
        Assert.Equal(OrderFulfillmentStage.Ready, order.FulfillmentStage);
    }

    [Fact]
    public void Fulfillment_only_action_leaves_sales_and_payment_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Quoted,
            PaymentStage = OrderPaymentStage.Refunded,
            FulfillmentStage = OrderFulfillmentStage.NotStarted,
        };

        var request = new OrderStageTransitionRequest { TargetFulfillmentStage = OrderFulfillmentStage.Fulfilled };
        var workflow = new OrderWorkflowConfiguration
        {
            Transitions = [new OrderStageTransition { ToFulfillmentStage = OrderFulfillmentStage.Fulfilled }],
        };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsApplied);
        Assert.Equal(OrderFulfillmentStage.Fulfilled, order.FulfillmentStage);
        Assert.Equal(OrderSalesStage.Quoted, order.SalesStage);
        Assert.Equal(OrderPaymentStage.Refunded, order.PaymentStage);
    }
}
