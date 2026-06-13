namespace Orderly.Core.Commerce;

/// <summary>
/// Enumerates the Universal_Domain_Model entity types that custom fields and templates can target.
/// Each <c>CustomFieldDefinition</c> is associated with exactly one of these types (Req 5.4).
/// All names are industry-agnostic.
/// </summary>
public enum BusinessEntityType
{
    Workspace = 0,
    Template = 1,
    Product = 2,
    ProductVariant = 3,
    InventoryItem = 4,
    InventoryMovement = 5,
    Customer = 6,
    CustomerContact = 7,
    Order = 8,
    OrderItem = 9,
    PaymentRecord = 10,
    CashFlowEntry = 11,
    Supplier = 12,
    BusinessTask = 13,
    BusinessInsight = 14,
    BusinessMetricSnapshot = 15,
    UnitDefinition = 16
}
