namespace Orderly.Core.Commerce.Repositories;

/// <summary>
/// Generic CRUD contract for one Universal_Domain_Model entity persisted by the data layer
/// (Requirement 3.2). Every entity has exactly one repository exposing create, read, update, and
/// delete operations.
///
/// <para><b>Active vs. including-deleted reads.</b> The default read operations
/// (<see cref="GetByIdAsync"/>, <see cref="GetAllAsync"/>) return only <i>active</i> records and
/// exclude soft-deleted/archived ones (those with a non-null <c>DeletedAt</c>), per Requirement 2.9.
/// The <c>...IncludingDeleted</c> variants are provided for recovery scenarios and return
/// soft-deleted records as well.</para>
///
/// <para><b>Delete is a soft-delete.</b> <see cref="DeleteAsync"/> performs a recoverable
/// soft-delete (sets <c>DeletedAt</c> and the deleted lifecycle status) rather than removing the
/// row, so the record is excluded from active queries but remains recoverable (Requirement 2.9).</para>
/// </summary>
/// <typeparam name="TEntity">The Universal_Domain_Model entity type managed by this repository.</typeparam>
public interface ICommerceRepository<TEntity>
    where TEntity : CommerceEntity
{
    /// <summary>Inserts a new entity and returns it.</summary>
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Returns the active (non-soft-deleted) entity with the given id, or null when none.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active (non-soft-deleted) entities.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the entity with the given id even when soft-deleted, or null when none. For recovery.</summary>
    Task<TEntity?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns every entity including soft-deleted ones. For recovery.</summary>
    Task<IReadOnlyList<TEntity>> GetAllIncludingDeletedAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing active entity.</summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the entity with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
