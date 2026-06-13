namespace Orderly.Core.Commerce;

/// <summary>
/// Shared base for every Universal_Domain_Model entity. Provides the common audit, lifecycle,
/// and personalization surface required by Requirements 2.4, 2.5, 2.7, 2.8, and 2.9:
/// a stable identity, a UTC <see cref="CreatedAt"/> that never changes, a UTC
/// <see cref="UpdatedAt"/> that advances on every persisted-field change, a nullable UTC
/// <see cref="DeletedAt"/>, an <see cref="EntityLifecycleStatus"/>, and a single nullable
/// <see cref="CustomFieldsJson"/> personalization field.
/// </summary>
public abstract class CommerceEntity
{
    private string? _customFieldsJson;

    /// <summary>
    /// Initializes audit and lifecycle state. <see cref="Id"/> and <see cref="CreatedAt"/> are
    /// seeded with sensible defaults but remain <c>init</c>-settable so callers (and the data
    /// layer when rehydrating persisted rows) can supply their own values via an object
    /// initializer. <see cref="UpdatedAt"/> starts equal to <see cref="CreatedAt"/>.
    /// </summary>
    protected CommerceEntity()
    {
        DateTime now = DateTime.UtcNow;
        Id = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
        Lifecycle = EntityLifecycleStatus.Active;
    }

    /// <summary>Stable identity of the entity.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Creation timestamp in UTC. Non-null and never changes after creation (Req 2.7, 2.8).
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last-modified timestamp in UTC. Non-null; advanced to the current UTC time on every
    /// persisted-field change via <see cref="MarkUpdated"/> (Req 2.7, 2.8).
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Soft-delete/archive timestamp in UTC, or <c>null</c> while the entity is active (Req 2.7, 2.9).
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Lifecycle state of the entity. Drives soft-delete/archive behavior and exclusion from
    /// active queries (Req 2.9).
    /// </summary>
    public EntityLifecycleStatus Lifecycle { get; private set; }

    /// <summary>
    /// Single nullable personalization field holding user-defined custom fields as JSON
    /// (Req 2.4). The value is stored exactly as provided with no assignment-time validation
    /// (Req 2.5); well-formedness is enforced later at the service/repository save boundary
    /// (Req 3.11, 3.12). Assigning a new value advances <see cref="UpdatedAt"/> (Req 2.8).
    /// </summary>
    public string? CustomFieldsJson
    {
        get => _customFieldsJson;
        set
        {
            _customFieldsJson = value;
            MarkUpdated();
        }
    }

    /// <summary>
    /// <c>true</c> when the entity is active: not soft-deleted/archived and visible to active
    /// queries. Used by the data layer to exclude soft-deleted records (Req 2.9).
    /// </summary>
    public bool IsActive => Lifecycle == EntityLifecycleStatus.Active && DeletedAt is null;

    /// <summary>
    /// Restores the persisted audit and lifecycle state when the data layer rehydrates an entity
    /// from storage, so that a load round-trip preserves <see cref="UpdatedAt"/>,
    /// <see cref="DeletedAt"/>, and <see cref="Lifecycle"/> exactly as they were stored. This is
    /// intended solely for repository rehydration: unlike a normal field mutation it does NOT
    /// advance <see cref="UpdatedAt"/> and it leaves <see cref="CreatedAt"/> unchanged (Req 2.7, 2.8).
    /// </summary>
    public void RestoreAuditState(DateTime updatedAt, DateTime? deletedAt, EntityLifecycleStatus lifecycle)
    {
        UpdatedAt = updatedAt;
        DeletedAt = deletedAt;
        Lifecycle = lifecycle;
    }

    /// <summary>
    /// Advances <see cref="UpdatedAt"/> to the current UTC time while leaving
    /// <see cref="CreatedAt"/> unchanged. Derived entities MUST call this whenever a persisted
    /// field changes so the audit timestamp reflects the mutation (Req 2.8). The new value is
    /// never earlier than the previous <see cref="UpdatedAt"/>.
    /// </summary>
    protected void MarkUpdated()
    {
        DateTime now = DateTime.UtcNow;
        UpdatedAt = now >= UpdatedAt ? now : UpdatedAt;
    }

    /// <summary>
    /// Archives the entity: sets <see cref="DeletedAt"/> to the current UTC time and
    /// <see cref="Lifecycle"/> to <see cref="EntityLifecycleStatus.Archived"/>, while retaining
    /// the stored data so the entity is excluded from active queries but remains recoverable
    /// (Req 2.9). Advances <see cref="UpdatedAt"/>.
    /// </summary>
    public void Archive()
    {
        DeletedAt = DateTime.UtcNow;
        Lifecycle = EntityLifecycleStatus.Archived;
        MarkUpdated();
    }

    /// <summary>
    /// Soft-deletes the entity: sets <see cref="DeletedAt"/> to the current UTC time and
    /// <see cref="Lifecycle"/> to <see cref="EntityLifecycleStatus.Deleted"/>, while retaining
    /// the stored data so the entity is excluded from active queries but remains recoverable
    /// (Req 2.9). Advances <see cref="UpdatedAt"/>.
    /// </summary>
    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        Lifecycle = EntityLifecycleStatus.Deleted;
        MarkUpdated();
    }

    /// <summary>
    /// Restores a soft-deleted or archived entity back to <see cref="EntityLifecycleStatus.Active"/>,
    /// clearing <see cref="DeletedAt"/> so it reappears in active queries with its data intact
    /// (Req 2.9). Advances <see cref="UpdatedAt"/>.
    /// </summary>
    public void Recover()
    {
        DeletedAt = null;
        Lifecycle = EntityLifecycleStatus.Active;
        MarkUpdated();
    }
}
