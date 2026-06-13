using System.Collections.Generic;
using Orderly.Core.Commerce;
using Orderly.Data.Commerce.Services;
using Xunit;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Example/unit tests for the Task 13.4 <see cref="TemplateConfigurationSerializer"/> (Req 5.5, 5.6).
/// They verify that page configuration (metric-card/table-column show/hide and the page defaults) and
/// workflow configuration (initial stage per dimension plus composite transitions) round-trip through
/// the JSON stored in <see cref="BusinessTemplate.ConfigJson"/>.
/// </summary>
public sealed class TemplateConfigurationSerializerTests
{
    [Fact]
    public void Empty_or_null_config_deserializes_to_defaults()
    {
        TemplateConfiguration fromNull = TemplateConfigurationSerializer.Deserialize(null);
        TemplateConfiguration fromBlank = TemplateConfigurationSerializer.Deserialize("   ");

        Assert.Equal(OrderSalesStage.Draft, fromNull.Workflow.InitialSalesStage);
        Assert.Equal(OrderPaymentStage.Unpaid, fromNull.Workflow.InitialPaymentStage);
        Assert.Equal(OrderFulfillmentStage.NotStarted, fromNull.Workflow.InitialFulfillmentStage);
        Assert.Empty(fromNull.Workflow.Transitions);
        Assert.Empty(fromNull.Page.MetricCards);
        Assert.Empty(fromBlank.Page.TableColumns);
    }

    [Fact]
    public void Page_configuration_round_trips()
    {
        var configuration = new TemplateConfiguration
        {
            Page = new TemplatePageConfiguration
            {
                MetricCards = new Dictionary<string, TemplateElementVisibility>
                {
                    ["revenue"] = TemplateElementVisibility.Shown,
                    ["margin"] = TemplateElementVisibility.Hidden,
                },
                TableColumns = new Dictionary<string, TemplateElementVisibility>
                {
                    ["sku"] = TemplateElementVisibility.Hidden,
                },
                DefaultSort = "createdAt",
                DefaultUnit = "pcs",
                DefaultCurrency = "CNY",
                DefaultOrderFlow = "standard",
            },
        };

        string json = TemplateConfigurationSerializer.Serialize(configuration);
        TemplateConfiguration restored = TemplateConfigurationSerializer.Deserialize(json);

        Assert.Equal(TemplateElementVisibility.Shown, restored.Page.MetricCards["revenue"]);
        Assert.Equal(TemplateElementVisibility.Hidden, restored.Page.MetricCards["margin"]);
        Assert.True(restored.Page.IsMetricCardShown("revenue"));
        Assert.False(restored.Page.IsMetricCardShown("margin"));
        // An unconfigured key resolves to shown — always exactly one of shown/hidden (Req 5.5).
        Assert.True(restored.Page.IsMetricCardShown("unconfigured"));
        Assert.False(restored.Page.IsTableColumnShown("sku"));
        Assert.Equal("createdAt", restored.Page.DefaultSort);
        Assert.Equal("pcs", restored.Page.DefaultUnit);
        Assert.Equal("CNY", restored.Page.DefaultCurrency);
        Assert.Equal("standard", restored.Page.DefaultOrderFlow);
    }

    [Fact]
    public void Workflow_configuration_with_composite_transitions_round_trips()
    {
        var configuration = new TemplateConfiguration
        {
            Workflow = new OrderWorkflowConfiguration
            {
                InitialSalesStage = OrderSalesStage.Quoted,
                InitialPaymentStage = OrderPaymentStage.PartiallyPaid,
                InitialFulfillmentStage = OrderFulfillmentStage.InProgress,
                Transitions = new[]
                {
                    // Single-dimension transition.
                    new OrderStageTransition { FromSalesStage = OrderSalesStage.Quoted, ToSalesStage = OrderSalesStage.Confirmed },
                    // Composite transition updating all three dimensions at once.
                    new OrderStageTransition
                    {
                        ToSalesStage = OrderSalesStage.Completed,
                        ToPaymentStage = OrderPaymentStage.Paid,
                        ToFulfillmentStage = OrderFulfillmentStage.Fulfilled,
                    },
                },
            },
        };

        string json = TemplateConfigurationSerializer.Serialize(configuration);
        TemplateConfiguration restored = TemplateConfigurationSerializer.Deserialize(json);

        Assert.Equal(OrderSalesStage.Quoted, restored.Workflow.InitialSalesStage);
        Assert.Equal(OrderPaymentStage.PartiallyPaid, restored.Workflow.InitialPaymentStage);
        Assert.Equal(OrderFulfillmentStage.InProgress, restored.Workflow.InitialFulfillmentStage);

        Assert.Equal(2, restored.Workflow.Transitions.Count);

        OrderStageTransition single = restored.Workflow.Transitions[0];
        Assert.Equal(1, single.NamedDimensionCount);
        Assert.Equal(OrderSalesStage.Quoted, single.FromSalesStage);
        Assert.Equal(OrderSalesStage.Confirmed, single.ToSalesStage);

        OrderStageTransition composite = restored.Workflow.Transitions[1];
        Assert.Equal(3, composite.NamedDimensionCount);
        Assert.Equal(OrderSalesStage.Completed, composite.ToSalesStage);
        Assert.Equal(OrderPaymentStage.Paid, composite.ToPaymentStage);
        Assert.Equal(OrderFulfillmentStage.Fulfilled, composite.ToFulfillmentStage);
    }
}
