namespace Orderly.Core.Services;

public interface IAutoReplyService
{
    Task QueueReplyAsync(string platform, string contactHandle, string content, CancellationToken cancellationToken = default);
}
