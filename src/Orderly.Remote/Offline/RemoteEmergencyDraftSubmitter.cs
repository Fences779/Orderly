using System.Net.Http;
using Orderly.Contracts.Offline;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Offline;

public sealed class RemoteEmergencyDraftSubmitter : IEmergencyDraftSubmitter
{
    private readonly RemoteCommerceClient _client;
    private readonly IEmergencyDraftQueue _queue;
    private readonly CloudAuthSession _session;
    private readonly TimeSpan _pollingInterval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public event Action<int>? DraftCountChanged;
    public event Action<bool>? ConnectivityChanged;

    public RemoteEmergencyDraftSubmitter(
        RemoteCommerceClient client,
        IEmergencyDraftQueue queue,
        CloudAuthSession session,
        TimeSpan? pollingInterval = null)
    {
        _client = client;
        _queue = queue;
        _session = session;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(60);
    }

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _cts.CancelAsync();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when the loop is stopped.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public async Task TriggerSubmissionAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        await TrySubmitPendingAsync(linkedCts.Token).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollingInterval);
        do
        {
            await TrySubmitPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task TrySubmitPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
            var pending = drafts.Where(d => string.Equals(d.Status, EmergencyDraftStatus.Pending, StringComparison.OrdinalIgnoreCase)).ToList();

            DraftCountChanged?.Invoke(pending.Count);

            if (pending.Count == 0)
            {
                return;
            }

            var anySucceeded = false;
            var anyNetworkFailure = false;

            foreach (var draft in pending)
            {
                try
                {
                    await SubmitDraftAsync(draft, cancellationToken).ConfigureAwait(false);
                    await _queue.RemoveAsync(draft.Id, cancellationToken).ConfigureAwait(false);
                    anySucceeded = true;
                }
                catch (HttpRequestException ex)
                {
                    anyNetworkFailure = true;
                    await _queue.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Pending, ex.Message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _queue.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
                }
            }

            var remaining = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
            var remainingPending = remaining.Count(d => string.Equals(d.Status, EmergencyDraftStatus.Pending, StringComparison.OrdinalIgnoreCase));
            DraftCountChanged?.Invoke(remainingPending);

            if (anySucceeded && !anyNetworkFailure)
            {
                ConnectivityChanged?.Invoke(true);
            }
            else if (anyNetworkFailure)
            {
                ConnectivityChanged?.Invoke(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown in progress; suppress.
        }
        catch (Exception ex)
        {
            // Never crash the background loop; surface via connectivity event.
            ConnectivityChanged?.Invoke(false);
            Console.Error.WriteLine($"Emergency draft submission loop failed: {ex}");
        }
    }

    private async Task SubmitDraftAsync(EmergencyDraftDto draft, CancellationToken cancellationToken)
    {
        // Placeholder endpoint: the server-side EmergencyDraftController is not implemented yet.
        // Once it exists, this single POST will hand the draft off for server-side replay/validation.
        await _client.PostAsync<EmergencyDraftDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/emergency-drafts",
            draft,
            cancellationToken).ConfigureAwait(false);
    }
}
