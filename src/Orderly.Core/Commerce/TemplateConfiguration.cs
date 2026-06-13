namespace Orderly.Core.Commerce;

/// <summary>
/// The structured customization a <see cref="BusinessTemplate"/> carries (Req 5.5, 5.6). It combines
/// the template's page configuration (<see cref="Page"/>) with its workflow configuration
/// (<see cref="Workflow"/>) over the three independent order stage dimensions. This is the typed view
/// of the JSON stored verbatim in <see cref="BusinessTemplate.ConfigJson"/>; the data layer serializes
/// and deserializes this type to/from that column.
///
/// <para>The <see cref="Workflow"/> assigns each of the <see cref="OrderSalesStage"/>,
/// <see cref="OrderPaymentStage"/>, and <see cref="OrderFulfillmentStage"/> dimensions an initial
/// stage value and enumerates the composite transitions the workspace permits over those dimensions
/// (Req 5.6). Custom-field definitions are persisted separately as <see cref="CustomFieldDefinition"/>
/// records scoped to the template and are not embedded here.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public sealed record TemplateConfiguration
{
    /// <summary>The template's page configuration (Req 5.5). Never null; defaults to an empty configuration.</summary>
    public TemplatePageConfiguration Page { get; init; } = new();

    /// <summary>
    /// The template's workflow configuration over the three independent stage dimensions (Req 5.6).
    /// Never null; defaults to the canonical initial stages with no transitions.
    /// </summary>
    public OrderWorkflowConfiguration Workflow { get; init; } = new();
}
