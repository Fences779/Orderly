namespace Orderly.Core.Commerce;

/// <summary>
/// Classifies an inventory movement and determines how it changes an inventory item's quantity (Req 4.8).
/// </summary>
public enum InventoryMovementType
{
    /// <summary>Stock entering inventory (increases quantity).</summary>
    Inbound = 0,

    /// <summary>Stock leaving inventory (decreases quantity).</summary>
    Outbound = 1,

    /// <summary>A manual correction that sets or adjusts the quantity.</summary>
    Adjustment = 2
}
