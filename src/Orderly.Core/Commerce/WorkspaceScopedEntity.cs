namespace Orderly.Core.Commerce;

/// <summary>
/// Base for business-data entities that belong to exactly one <c>BusinessWorkspace</c>.
/// Adds a non-null <see cref="WorkspaceId"/> on top of the shared <see cref="CommerceEntity"/>
/// audit/lifecycle/personalization surface (Req 2.2, 2.4). Entities such as Product,
/// InventoryItem, Customer, Order, PaymentRecord, CashFlowEntry, Supplier, BusinessTask,
/// BusinessInsight, and BusinessMetricSnapshot extend this type.
/// </summary>
public abstract class WorkspaceScopedEntity : CommerceEntity
{
    /// <summary>
    /// Identity of the owning workspace. Non-null: every workspace-scoped entity belongs to
    /// exactly one workspace.
    /// </summary>
    public Guid WorkspaceId { get; init; }
}
