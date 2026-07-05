namespace Orderly.Contracts.Offline;

public interface IEmergencyDraftSubmitter : IAsyncDisposable
{
    event Action<int>? DraftCountChanged;
    event Action<bool>? ConnectivityChanged;

    Task<IReadOnlyList<EmergencyDraftDto>> ListDraftsAsync(CancellationToken cancellationToken = default);
    Task SubmitDraftAsync(string draftId, CancellationToken cancellationToken = default);
    Task DiscardDraftAsync(string draftId, CancellationToken cancellationToken = default);
    Task TriggerSubmissionAsync(CancellationToken cancellationToken = default);
}
