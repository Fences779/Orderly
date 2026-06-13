namespace Orderly.Core.Commerce;

/// <summary>
/// Lifecycle state shared by every Universal_Domain_Model entity. Drives soft-delete/archive
/// behavior and exclusion of records from active queries (Req 2.9).
/// </summary>
public enum EntityLifecycleStatus
{
    /// <summary>The entity is active and visible to active queries.</summary>
    Active = 0,

    /// <summary>The entity has been archived; retained and recoverable but excluded from active queries.</summary>
    Archived = 1,

    /// <summary>The entity has been soft-deleted; retained and recoverable but excluded from active queries.</summary>
    Deleted = 2
}
