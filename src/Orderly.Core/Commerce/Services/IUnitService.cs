namespace Orderly.Core.Commerce.Services;

/// <summary>
/// Universal unit-of-measure service (Req 4.1). Provides create/read/update/delete over
/// <see cref="UnitDefinition"/> (built-in/system units with a null <c>TemplateId</c>, or
/// template-scoped user-defined units). Reads return only active (non-soft-deleted) units;
/// <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9).
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface IUnitService
{
    /// <summary>Persists a new unit definition and returns it.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="unit"/> is null.</exception>
    Task<UnitDefinition> CreateAsync(UnitDefinition unit, CancellationToken cancellationToken = default);

    /// <summary>Returns the active unit definition with the given id, or <c>null</c> when none.</summary>
    Task<UnitDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active unit definitions.</summary>
    Task<IReadOnlyList<UnitDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing unit definition.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="unit"/> is null.</exception>
    Task UpdateAsync(UnitDefinition unit, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the unit definition with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
