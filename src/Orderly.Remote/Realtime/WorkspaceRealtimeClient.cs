using Microsoft.AspNetCore.SignalR.Client;
using Orderly.Contracts.Realtime;
using Orderly.Remote.Auth;

namespace Orderly.Remote.Realtime;

public sealed class WorkspaceRealtimeClient : IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly CloudAuthSession _session;
    private HubConnection? _connection;

    public event Action<RealtimeEventPayload>? EntityChanged;
    public event Action<RealtimeEventPayload>? PresenceChanged;
    public event Action? ForcedLogout;

    public WorkspaceRealtimeClient(string baseUrl, CloudAuthSession session)
    {
        _hubUrl = baseUrl.TrimEnd('/') + "/hubs/workspace";
        _session = session;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_session.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<RealtimeEventPayload>(RealtimeEvent.EntityCreated, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.EntityUpdated, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.EntityArchived, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.EntityRecovered, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.InventoryChanged, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.DashboardInvalidated, p => EntityChanged?.Invoke(p));
        _connection.On<RealtimeEventPayload>(RealtimeEvent.EditingPresenceChanged, p => PresenceChanged?.Invoke(p));
        _connection.On("ForceLogout", () => ForcedLogout?.Invoke());

        await _connection.StartAsync(cancellationToken);
        await _connection.InvokeAsync("JoinWorkspace", _session.WorkspaceId, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            await _connection.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
