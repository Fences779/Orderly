using Orderly.Contracts.Realtime;

namespace Orderly.Server.Services;

public interface ISignalRNotifier
{
    Task NotifyAsync(Guid workspaceId, string eventName, RealtimeEventPayload payload);
    Task NotifyUserDisabledAsync(Guid userId);
}
