using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CsCheck;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Templates;

/// <summary>
/// Property-based tests for workflow-validated stage transitions in
/// <see cref="CommerceOrderService.ApplyStageTransition"/>.
///
/// Property 9: Stage transitions honor the active workflow with no partial update.
/// For ANY workflow configuration (in which each of the three stage dimensions has an assigned
/// initial stage) and ANY requested stage transition: if the transition is permitted, only the
/// dimension(s) it names are updated to their target values; if the transition is not permitted,
/// all three stage dimensions remain unchanged and a not-permitted error result is returned.
///
/// The test generates both permitted and non-permitted transitions to exercise both branches: a
/// random arm (arbitrary workflow + arbitrary request, classified by an independent oracle that
/// mirrors the workflow's matching rules) and a constructed-permitted arm (a request aligned to a
/// transition the workflow contains, with the order's stages set to satisfy any source constraint).
///
/// **Validates: Requirements 4.4, 4.5, 5.6**
/// </summary>
public class WorkflowValidatedTransitionsPropertyTests
{
    // Generators over the full value set of each independent stage enum.
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

    /// <summary>
    /// A generated workflow transition. The dimension mask (bit0=sales, bit1=payment,
    /// bit2=fulfillment, never 0) selects which dimensions it names; for each named dimension an
    /// optional <c>From…</c> source constraint is included.
    /// </summary>
    private static readonly Gen<OrderStageTransition> TransitionGen =
        from mask in Gen.Int[1, 7]
        from toSales in SalesGen
        from toPayment in PaymentGen
        from toFulfillment in FulfillmentGen
        from fromSales in SalesGen
        from fromPayment in PaymentGen
        from fromFulfillment in FulfillmentGen
        from useFromSales in Gen.Bool
        from useFromPayment in Gen.Bool
        from useFromFulfillment in Gen.Bool
        select new OrderStageTransition
        {
            ToSalesStage = (mask & 1) != 0 ? toSales : null,
            FromSalesStage = (mask & 1) != 0 && useFromSales ? fromSales : null,
            ToPaymentStage = (mask & 2) != 0 ? toPayment : null,
            FromPaymentStage = (mask & 2) != 0 && useFromPayment ? fromPayment : null,
            ToFulfillmentStage = (mask & 4) != 0 ? toFulfillment : null,
            FromFulfillmentStage = (mask & 4) != 0 && useFromFulfillment ? fromFulfillment : null,
        };

    /// <summary>A generated request naming one, two, or all three dimensions (mask never 0).</summary>
    private static readonly Gen<OrderStageTransitionRequest> RequestGen =
        from mask in Gen.Int[1, 7]
        from sales in SalesGen
        from payment in PaymentGen
        from fulfillment in FulfillmentGen
        select new OrderStageTransitionRequest
        {
            TargetSalesStage = (mask & 1) != 0 ? sales : null,
            TargetPaymentStage = (mask & 2) != 0 ? payment : null,
            TargetFulfillmentStage = (mask & 4) != 0 ? fulfillment : null,
        };

    /// <summary>
    /// A complete scenario: the order's arbitrary current stages, the active workflow (with its
    /// three assigned initial stages and a list of permitted transitions), and the requested
    /// transition. The constructed-permitted arm guarantees the request matches a workflow
    /// transition; the random arm leaves the outcome to the oracle.
    /// </summary>
    private sealed record Scenario(
        OrderSalesStage CurrentSales,
        OrderPaymentStage CurrentPayment,
        OrderFulfillmentStage CurrentFulfillment,
        OrderWorkflowConfiguration Workflow,
        OrderStageTransitionRequest Request);

    // Random arm: arbitrary current stages, arbitrary workflow (0..4 transitions), arbitrary request.
    private static readonly Gen<Scenario> RandomScenarioGen =
        from currentSales in SalesGen
        from currentPayment in PaymentGen
        from currentFulfillment in FulfillmentGen
        from initSales in SalesGen
        from initPayment in PaymentGen
        from initFulfillment in FulfillmentGen
        from transitions in TransitionGen.List[0, 4]
        from request in RequestGen
        select new Scenario(
            currentSales,
            currentPayment,
            currentFulfillment,
            new OrderWorkflowConfiguration
            {
                InitialSalesStage = initSales,
                InitialPaymentStage = initPayment,
                InitialFulfillmentStage = initFulfillment,
                Transitions = transitions,
            },
            request);

    // Constructed-permitted arm: a request aligned to a "target" transition, with current stages set
    // to satisfy any of its source constraints, embedded in a workflow alongside arbitrary extras.
    private static readonly Gen<Scenario> PermittedScenarioGen =
        from target in TransitionGen
        from extras in TransitionGen.List[0, 3]
        from currentSales in SalesGen
        from currentPayment in PaymentGen
        from currentFulfillment in FulfillmentGen
        from initSales in SalesGen
        from initPayment in PaymentGen
        from initFulfillment in FulfillmentGen
        from extrasBeforeTarget in Gen.Bool
        select BuildPermittedScenario(
            target,
            extras,
            extrasBeforeTarget,
            currentSales,
            currentPayment,
            currentFulfillment,
            initSales,
            initPayment,
            initFulfillment);

    private static readonly Gen<Scenario> ScenarioGen = Gen.OneOf(RandomScenarioGen, PermittedScenarioGen);

    [Fact]
    public void Property9_transitions_honor_the_active_workflow_with_no_partial_update()
    {
        var service = new CommerceOrderService();

        // Track that both branches are actually exercised across the run (the task requires both
        // permitted and non-permitted transitions). Indices: 0 = permitted, 1 = not permitted.
        int[] branchCounts = new int[2];

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

                OrderSalesStage originalSales = order.SalesStage;
                OrderPaymentStage originalPayment = order.PaymentStage;
                OrderFulfillmentStage originalFulfillment = order.FulfillmentStage;

                bool expectedPermitted = OraclePermitted(scenario.Workflow, order, scenario.Request);

                OrderStageTransitionResult result =
                    service.ApplyStageTransition(order, scenario.Request, scenario.Workflow);

                if (expectedPermitted)
                {
                    Interlocked.Increment(ref branchCounts[0]);

                    Assert.True(result.IsApplied, $"Expected a permitted transition to apply, got {result.Outcome}.");

                    // Only the named dimension(s) are updated to their targets; the rest are unchanged.
                    OrderSalesStage expectedSales =
                        scenario.Request.TargetSalesStage ?? originalSales;
                    OrderPaymentStage expectedPayment =
                        scenario.Request.TargetPaymentStage ?? originalPayment;
                    OrderFulfillmentStage expectedFulfillment =
                        scenario.Request.TargetFulfillmentStage ?? originalFulfillment;

                    Assert.Equal(expectedSales, order.SalesStage);
                    Assert.Equal(expectedPayment, order.PaymentStage);
                    Assert.Equal(expectedFulfillment, order.FulfillmentStage);
                }
                else
                {
                    Interlocked.Increment(ref branchCounts[1]);

                    Assert.True(
                        result.IsNotPermitted,
                        $"Expected a non-permitted transition to be rejected, got {result.Outcome}.");

                    // No partial update: all three dimensions remain exactly as they were.
                    Assert.Equal(originalSales, order.SalesStage);
                    Assert.Equal(originalPayment, order.PaymentStage);
                    Assert.Equal(originalFulfillment, order.FulfillmentStage);
                }
            },
            iter: PbtConfig.MinIterations);

        Assert.True(branchCounts[0] > 0, "Expected at least one permitted transition to be exercised.");
        Assert.True(branchCounts[1] > 0, "Expected at least one non-permitted transition to be exercised.");
    }

    /// <summary>
    /// Builds a scenario whose request is guaranteed permitted: the request names exactly the
    /// <paramref name="target"/> transition's dimensions with its target values, and the order's
    /// current stages are forced to the target's source constraints (where declared) so the
    /// transition matches. Arbitrary extra transitions are included to mimic a realistic workflow;
    /// because permission is an OR over transitions, they never revoke the guaranteed match.
    /// </summary>
    private static Scenario BuildPermittedScenario(
        OrderStageTransition target,
        IReadOnlyList<OrderStageTransition> extras,
        bool extrasBeforeTarget,
        OrderSalesStage currentSales,
        OrderPaymentStage currentPayment,
        OrderFulfillmentStage currentFulfillment,
        OrderSalesStage initSales,
        OrderPaymentStage initPayment,
        OrderFulfillmentStage initFulfillment)
    {
        // Force current stages to satisfy the target's source constraints where declared.
        OrderSalesStage scopedSales = target.FromSalesStage ?? currentSales;
        OrderPaymentStage scopedPayment = target.FromPaymentStage ?? currentPayment;
        OrderFulfillmentStage scopedFulfillment = target.FromFulfillmentStage ?? currentFulfillment;

        var request = new OrderStageTransitionRequest
        {
            TargetSalesStage = target.ToSalesStage,
            TargetPaymentStage = target.ToPaymentStage,
            TargetFulfillmentStage = target.ToFulfillmentStage,
        };

        var transitions = new List<OrderStageTransition>();
        if (extrasBeforeTarget)
        {
            transitions.AddRange(extras);
            transitions.Add(target);
        }
        else
        {
            transitions.Add(target);
            transitions.AddRange(extras);
        }

        var workflow = new OrderWorkflowConfiguration
        {
            InitialSalesStage = initSales,
            InitialPaymentStage = initPayment,
            InitialFulfillmentStage = initFulfillment,
            Transitions = transitions,
        };

        return new Scenario(scopedSales, scopedPayment, scopedFulfillment, workflow, request);
    }

    // --- Independent oracle mirroring the workflow's matching semantics (Req 4.4, 4.5) ---

    /// <summary>
    /// A workflow permits a request when the request names at least one dimension and some workflow
    /// transition matches it for the order's current stages.
    /// </summary>
    private static bool OraclePermitted(
        OrderWorkflowConfiguration workflow,
        Order order,
        OrderStageTransitionRequest request)
        => request.NamesAnyDimension
            && workflow.Transitions.Any(transition => OracleMatches(transition, order, request));

    /// <summary>
    /// A transition matches a request when it names exactly the same dimensions, targets the same
    /// stage values on those dimensions, and — for each named dimension declaring a source
    /// constraint — the order's current stage satisfies it.
    /// </summary>
    private static bool OracleMatches(
        OrderStageTransition transition,
        Order order,
        OrderStageTransitionRequest request)
    {
        if (transition.NamesSalesStage != request.NamesSalesStage
            || transition.NamesPaymentStage != request.NamesPaymentStage
            || transition.NamesFulfillmentStage != request.NamesFulfillmentStage)
        {
            return false;
        }

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

    // --- Focused unit examples complementing the property above ---

    [Fact]
    public void Composite_transition_updates_all_named_dimensions_and_only_those()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Quoted,
            PaymentStage = OrderPaymentStage.Unpaid,
            FulfillmentStage = OrderFulfillmentStage.NotStarted,
        };

        // Names sales + payment (two dimensions); fulfillment is left out and must stay unchanged.
        var request = new OrderStageTransitionRequest
        {
            TargetSalesStage = OrderSalesStage.Confirmed,
            TargetPaymentStage = OrderPaymentStage.Paid,
        };
        var workflow = new OrderWorkflowConfiguration
        {
            Transitions =
            [
                new OrderStageTransition
                {
                    FromSalesStage = OrderSalesStage.Quoted,
                    ToSalesStage = OrderSalesStage.Confirmed,
                    ToPaymentStage = OrderPaymentStage.Paid,
                },
            ],
        };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsApplied);
        Assert.Equal(OrderSalesStage.Confirmed, order.SalesStage);
        Assert.Equal(OrderPaymentStage.Paid, order.PaymentStage);
        Assert.Equal(OrderFulfillmentStage.NotStarted, order.FulfillmentStage);
    }

    [Fact]
    public void Wrong_target_value_is_not_permitted_and_leaves_all_dimensions_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Draft,
            PaymentStage = OrderPaymentStage.Unpaid,
            FulfillmentStage = OrderFulfillmentStage.NotStarted,
        };

        // Request targets Completed, but the only permitted sales transition targets Confirmed.
        var request = new OrderStageTransitionRequest { TargetSalesStage = OrderSalesStage.Completed };
        var workflow = new OrderWorkflowConfiguration
        {
            Transitions = [new OrderStageTransition { ToSalesStage = OrderSalesStage.Confirmed }],
        };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsNotPermitted);
        Assert.Equal(OrderSalesStage.Draft, order.SalesStage);
        Assert.Equal(OrderPaymentStage.Unpaid, order.PaymentStage);
        Assert.Equal(OrderFulfillmentStage.NotStarted, order.FulfillmentStage);
    }

    [Fact]
    public void Unsatisfied_source_constraint_is_not_permitted_and_leaves_all_dimensions_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Draft,
            PaymentStage = OrderPaymentStage.PartiallyPaid,
            FulfillmentStage = OrderFulfillmentStage.Ready,
        };

        // The transition requires payment to currently be Unpaid, but it is PartiallyPaid.
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

        Assert.True(result.IsNotPermitted);
        Assert.Equal(OrderSalesStage.Draft, order.SalesStage);
        Assert.Equal(OrderPaymentStage.PartiallyPaid, order.PaymentStage);
        Assert.Equal(OrderFulfillmentStage.Ready, order.FulfillmentStage);
    }

    [Fact]
    public void Empty_workflow_permits_nothing_and_leaves_all_dimensions_unchanged()
    {
        var service = new CommerceOrderService();
        var order = new Order
        {
            WorkspaceId = Guid.NewGuid(),
            SalesStage = OrderSalesStage.Confirmed,
            PaymentStage = OrderPaymentStage.Paid,
            FulfillmentStage = OrderFulfillmentStage.InProgress,
        };

        var request = new OrderStageTransitionRequest { TargetFulfillmentStage = OrderFulfillmentStage.Fulfilled };
        var workflow = new OrderWorkflowConfiguration { Transitions = [] };

        OrderStageTransitionResult result = service.ApplyStageTransition(order, request, workflow);

        Assert.True(result.IsNotPermitted);
        Assert.Equal(OrderSalesStage.Confirmed, order.SalesStage);
        Assert.Equal(OrderPaymentStage.Paid, order.PaymentStage);
        Assert.Equal(OrderFulfillmentStage.InProgress, order.FulfillmentStage);
    }
}
