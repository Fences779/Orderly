namespace Orderly.Core.Services;

public interface IAiAssistantService
{
    Task<string> DraftReplyAsync(string context, CancellationToken cancellationToken = default);
}
