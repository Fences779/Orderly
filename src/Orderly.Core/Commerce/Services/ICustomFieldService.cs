namespace Orderly.Core.Commerce.Services;

/// <summary>The outcome of an attempt to add a <see cref="CustomFieldDefinition"/> (Req 5.4).</summary>
public enum CustomFieldDefinitionOutcome
{
    /// <summary>The definition was within the per-entity-type bound and was persisted (Req 5.4).</summary>
    Added = 0,

    /// <summary>
    /// Adding the definition would exceed the maximum of
    /// <see cref="ICustomFieldService.MaxDefinitionsPerEntityType"/> definitions for its entity type
    /// within the owning template; the add was rejected and nothing was persisted (Req 5.4).
    /// </summary>
    CustomFieldLimitExceeded = 1
}

/// <summary>
/// The result of <see cref="ICustomFieldService.AddDefinitionAsync"/>. Distinguishes a successful add
/// from one rejected for exceeding the per-entity-type bound (Req 5.4), following the typed-result
/// convention used across the Commerce Service Layer.
/// </summary>
public sealed record CustomFieldDefinitionResult
{
    /// <summary>The outcome of the add attempt.</summary>
    public CustomFieldDefinitionOutcome Outcome { get; init; }

    /// <summary>The definition that was persisted on success; <c>null</c> when the add was rejected.</summary>
    public CustomFieldDefinition? Definition { get; init; }

    /// <summary>
    /// A neutral, human-readable explanation of the rejection when the per-entity-type bound was
    /// exceeded (Req 5.4); <c>null</c> on success.
    /// </summary>
    public string? Error { get; init; }

    /// <summary><c>true</c> when the definition was within the bound and was persisted.</summary>
    public bool IsAdded => Outcome == CustomFieldDefinitionOutcome.Added;

    /// <summary>
    /// <c>true</c> when the add was rejected for exceeding the per-entity-type bound; in this case
    /// nothing was persisted (Req 5.4).
    /// </summary>
    public bool IsLimitExceeded => Outcome == CustomFieldDefinitionOutcome.CustomFieldLimitExceeded;

    /// <summary>Creates the canonical "added" result (Req 5.4).</summary>
    public static CustomFieldDefinitionResult Added(CustomFieldDefinition definition) => new()
    {
        Outcome = CustomFieldDefinitionOutcome.Added,
        Definition = definition
    };

    /// <summary>Creates the canonical "limit exceeded" result (Req 5.4).</summary>
    public static CustomFieldDefinitionResult LimitExceeded(string error) => new()
    {
        Outcome = CustomFieldDefinitionOutcome.CustomFieldLimitExceeded,
        Error = error
    };
}

/// <summary>
/// Commerce Service Layer contract for <see cref="CustomFieldDefinition"/> management (Req 4.1, 5.4).
/// Each definition is associated with exactly one <see cref="BusinessEntityType"/> (fixed on the
/// entity) and one owning <see cref="BusinessTemplate"/>. The service supports between 0 and
/// <see cref="MaxDefinitionsPerEntityType"/> definitions per entity type within a template; an attempt
/// to add one beyond that bound is rejected with
/// <see cref="CustomFieldDefinitionOutcome.CustomFieldLimitExceeded"/> and persists nothing (Req 5.4).
///
/// <para>Reads return only active (non-soft-deleted) definitions and <see cref="DeleteAsync"/> performs
/// a recoverable soft-delete (Req 2.9), both delegated to the underlying repository.</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term, and reads/writes only
/// through the Commerce repositories so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public interface ICustomFieldService
{
    /// <summary>The maximum number of <see cref="CustomFieldDefinition"/> entries per entity type within a template (Req 5.4).</summary>
    public const int MaxDefinitionsPerEntityType = 100;

    /// <summary>
    /// Adds a <see cref="CustomFieldDefinition"/> when its owning template/entity type currently holds
    /// fewer than <see cref="MaxDefinitionsPerEntityType"/> active definitions; otherwise rejects the
    /// add with <see cref="CustomFieldDefinitionOutcome.CustomFieldLimitExceeded"/> and persists nothing
    /// (Req 5.4). The definition's <see cref="CustomFieldDefinition.TargetEntityType"/> fixes the single
    /// entity type it is associated with.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
    Task<CustomFieldDefinitionResult> AddDefinitionAsync(CustomFieldDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Returns the active definition with the given id, or <c>null</c> when none.</summary>
    Task<CustomFieldDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all active definitions owned by the template identified by <paramref name="templateId"/>.</summary>
    Task<IReadOnlyList<CustomFieldDefinition>> GetByTemplateAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active definitions owned by <paramref name="templateId"/> that target
    /// <paramref name="entityType"/> (Req 5.4).
    /// </summary>
    Task<IReadOnlyList<CustomFieldDefinition>> GetByEntityTypeAsync(Guid templateId, BusinessEntityType entityType, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing definition's mutable fields.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
    Task UpdateAsync(CustomFieldDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the definition with the given id so it is excluded from active queries but remains recoverable.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
