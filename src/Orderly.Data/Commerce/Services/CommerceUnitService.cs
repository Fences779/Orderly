using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="IUnitService"/> over the
/// Universal_Domain_Model (Req 4.1). Provides create/read/update/delete over
/// <see cref="UnitDefinition"/> (built-in/system units with a null <c>TemplateId</c>, or
/// template-scoped user-defined units). Reads return only active (non-soft-deleted) units and
/// <see cref="DeleteAsync"/> performs a recoverable soft-delete (Req 2.9), both delegated to the
/// underlying repository.
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceUnitService : IUnitService
{
    private readonly IUnitDefinitionRepository _unitRepository;

    /// <summary>Creates the service over the Commerce unit-definition repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceUnitService(IUnitDefinitionRepository unitRepository)
    {
        _unitRepository = unitRepository ?? throw new ArgumentNullException(nameof(unitRepository));
    }

    /// <inheritdoc />
    public async Task<UnitDefinition> CreateAsync(UnitDefinition unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return await _unitRepository.CreateAsync(unit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<UnitDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _unitRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<UnitDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
        => _unitRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(UnitDefinition unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        await _unitRepository.UpdateAsync(unit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _unitRepository.DeleteAsync(id, cancellationToken);
}
