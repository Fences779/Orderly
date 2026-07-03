using System.Collections.Concurrent;
using Orderly.Contracts.Offline;

namespace Orderly.Remote.Cache;

public sealed class EmergencyDraftQueue : IEmergencyDraftQueue
{
    private readonly ConcurrentDictionary<string, EmergencyDraftDto> _drafts = new();

    public Task AddAsync(EmergencyDraftDto draft, CancellationToken cancellationToken = default)
    {
        _drafts[draft.Id] = draft;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EmergencyDraftDto>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<EmergencyDraftDto>>(_drafts.Values.OrderBy(d => d.CreatedAtUtc).ToList());

    public Task<EmergencyDraftDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        _drafts.TryGetValue(id, out var draft);
        return Task.FromResult(draft);
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        _drafts.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string id, string status, string? error, CancellationToken cancellationToken = default)
    {
        if (_drafts.TryGetValue(id, out var draft))
        {
            draft.Status = status;
            draft.LastSubmitError = error;
        }
        return Task.CompletedTask;
    }
}
