namespace Orderly.Contracts.Offline;

public interface IEmergencyDraftSubmitter : IAsyncDisposable
{
    event Action<int>? DraftCountChanged;
    event Action<bool>? ConnectivityChanged;

    Task TriggerSubmissionAsync(CancellationToken cancellationToken = default);
}
