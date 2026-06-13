using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Commerce Service Layer implementation of <see cref="ICustomFieldService"/> over the
/// Universal_Domain_Model (Req 4.1, 5.4). Each <see cref="CustomFieldDefinition"/> is associated with
/// exactly one <see cref="BusinessEntityType"/> (fixed on the entity) and one owning
/// <see cref="BusinessTemplate"/>. The service enforces the per-entity-type bound: a template may hold
/// between 0 and <see cref="ICustomFieldService.MaxDefinitionsPerEntityType"/> active definitions for
/// any single entity type, and an attempt to add one beyond that bound is rejected with
/// <see cref="CustomFieldDefinitionOutcome.CustomFieldLimitExceeded"/> while persisting nothing
/// (Req 5.4).
///
/// <para>The count that governs the bound considers only active (non-soft-deleted) definitions sharing
/// the new definition's <see cref="CustomFieldDefinition.TemplateId"/> and
/// <see cref="CustomFieldDefinition.TargetEntityType"/>, so definitions for other entity types or other
/// templates are independent, and a soft-deleted definition frees a slot (Req 2.9, 5.4).</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads/writes only through
/// the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceCustomFieldService : ICustomFieldService
{
    private readonly ICustomFieldDefinitionRepository _definitionRepository;

    /// <summary>Creates the service over the Commerce custom-field-definition repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the repository is null.</exception>
    public CommerceCustomFieldService(ICustomFieldDefinitionRepository definitionRepository)
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
    }

    /// <inheritdoc />
    public async Task<CustomFieldDefinitionResult> AddDefinitionAsync(
        CustomFieldDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Count existing active definitions for the same template + entity type before any write so a
        // rejection persists nothing (Req 5.4).
        IReadOnlyList<CustomFieldDefinition> existing = await GetByEntityTypeAsync(
            definition.TemplateId, definition.TargetEntityType, cancellationToken).ConfigureAwait(false);

        if (existing.Count >= ICustomFieldService.MaxDefinitionsPerEntityType)
        {
            // "Cannot add a custom field: entity type '{0}' already has the maximum of {1} fields."
            return CustomFieldDefinitionResult.LimitExceeded(
                $"无法新增自定义字段：实体类型“{definition.TargetEntityType}”已达到最多 {ICustomFieldService.MaxDefinitionsPerEntityType} 个字段的上限。");
        }

        CustomFieldDefinition created = await _definitionRepository
            .CreateAsync(definition, cancellationToken)
            .ConfigureAwait(false);

        return CustomFieldDefinitionResult.Added(created);
    }

    /// <inheritdoc />
    public Task<CustomFieldDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _definitionRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomFieldDefinition>> GetByTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomFieldDefinition> all = await _definitionRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return all.Where(d => d.TemplateId == templateId).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomFieldDefinition>> GetByEntityTypeAsync(
        Guid templateId,
        BusinessEntityType entityType,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomFieldDefinition> all = await _definitionRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return all.Where(d => d.TemplateId == templateId && d.TargetEntityType == entityType).ToList();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(CustomFieldDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await _definitionRepository.UpdateAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _definitionRepository.DeleteAsync(id, cancellationToken);
}
