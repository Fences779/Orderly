namespace Orderly.Contracts.Offline;

/// <summary>
/// Persistent queue for emergency drafts created while the client is offline.
/// Drafts are submitted to the cloud once connectivity is restored.
/// </summary>
public interface IEmergencyDraftQueue
{
    Task AddAsync(EmergencyDraftDto draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmergencyDraftDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<EmergencyDraftDto?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string id, string status, string? error, CancellationToken cancellationToken = default);
}
