using System.Net.Http;
using System.Text.Json;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
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

    public async Task<IReadOnlyList<EmergencyDraftDto>> ListDraftsAsync(CancellationToken cancellationToken = default)
    {
        var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
        DraftCountChanged?.Invoke(CountActionableDrafts(drafts));
        return drafts;
    }

    public async Task SubmitDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            throw new ArgumentException("草稿 Id 不能为空。", nameof(draftId));
        }

        var draft = await _queue.GetAsync(draftId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("草稿不存在或已处理。");

        try
        {
            await EnsureBaseRevisionStillCurrentAsync(draft, cancellationToken).ConfigureAwait(false);
            await SubmitDraftAsync(draft, cancellationToken).ConfigureAwait(false);
            await _queue.RemoveAsync(draft.Id, cancellationToken).ConfigureAwait(false);
            ConnectivityChanged?.Invoke(true);
        }
        catch (HttpRequestException ex)
        {
            await _queue.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Pending, ex.Message, cancellationToken).ConfigureAwait(false);
            ConnectivityChanged?.Invoke(false);
            throw;
        }
        catch (Exception ex)
        {
            await _queue.UpdateStatusAsync(draft.Id, EmergencyDraftStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
            DraftCountChanged?.Invoke(CountActionableDrafts(drafts));
        }
    }

    public async Task DiscardDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return;
        }

        await _queue.RemoveAsync(draftId, cancellationToken).ConfigureAwait(false);
        var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
        DraftCountChanged?.Invoke(CountActionableDrafts(drafts));
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollingInterval);
        do
        {
            await RefreshDraftCountAsync(cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task TrySubmitPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
            var pending = drafts.Where(IsActionableDraft).ToList();

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
        await _client.PostAsync<EmergencyDraftDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/emergency-drafts",
            draft,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshDraftCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            var drafts = await _queue.ListAsync(cancellationToken).ConfigureAwait(false);
            DraftCountChanged?.Invoke(CountActionableDrafts(drafts));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ConnectivityChanged?.Invoke(false);
            Console.Error.WriteLine($"Emergency draft count refresh failed: {ex}");
        }
    }

    private async Task EnsureBaseRevisionStillCurrentAsync(EmergencyDraftDto draft, CancellationToken cancellationToken)
    {
        if (draft.BaseRevision is null || string.IsNullOrWhiteSpace(draft.EntityId))
        {
            return;
        }

        if (!Guid.TryParse(draft.EntityId, out var entityId))
        {
            throw new InvalidOperationException("草稿目标实体 Id 格式不正确。");
        }

        var path = BuildEntityPath(draft.EntityType, entityId);
        if (path is null)
        {
            return;
        }

        var latest = await _client.GetAsync<JsonElement>(path, cancellationToken).ConfigureAwait(false);
        if (latest.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new InvalidOperationException("云端目标数据不存在，请刷新后重新确认。");
        }

        if (TryGetRevision(latest, out var latestRevision) && latestRevision != draft.BaseRevision.Value)
        {
            throw new InvalidOperationException("云端数据已变化，请刷新页面后重新确认是否提交该草稿。");
        }
    }

    private string? BuildEntityPath(string entityType, Guid entityId)
    {
        var path = entityType switch
        {
            EntityType.Order => $"orders/{entityId:N}",
            EntityType.Customer => $"customers/{entityId:N}",
            EntityType.BusinessTask => $"business-tasks/{entityId:N}",
            _ => null
        };

        return path is null ? null : $"api/workspaces/{_session.WorkspaceId:N}/{path}";
    }

    private static bool TryGetRevision(JsonElement element, out long revision)
    {
        if (element.TryGetProperty("revision", out var camel) && camel.TryGetInt64(out revision))
        {
            return true;
        }

        if (element.TryGetProperty("Revision", out var pascal) && pascal.TryGetInt64(out revision))
        {
            return true;
        }

        revision = default;
        return false;
    }

    private static int CountActionableDrafts(IEnumerable<EmergencyDraftDto> drafts)
        => drafts.Count(IsActionableDraft);

    private static bool IsActionableDraft(EmergencyDraftDto draft)
        => string.Equals(draft.Status, EmergencyDraftStatus.Pending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(draft.Status, EmergencyDraftStatus.Failed, StringComparison.OrdinalIgnoreCase);
}
