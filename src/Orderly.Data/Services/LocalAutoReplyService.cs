using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalAutoReplyService : IAutoReplyService
{
    private readonly IAiSuggestionRepository _suggestionRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalAutoReplyService(IAiSuggestionRepository suggestionRepository, IActivityLogRepository activityLogRepository)
    {
        _suggestionRepository = suggestionRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<AiSuggestion?> PrepareReplyAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null)
        {
            return null;
        }

        suggestion.Status = AiSuggestionStatus.Accepted;
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.AutoReplyDraftPrepared,
            CustomerId = suggestion.CustomerId,
            OrderId = suggestion.OrderId,
            Title = "生成回复草稿",
            Description = "本地 Stub 已准备回复草稿，未真实发送。",
            Operator = "local-stub"
        }, cancellationToken);

        return suggestion;
    }

    public async Task MarkReplySentAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null || suggestion.Status == AiSuggestionStatus.Sent)
        {
            return;
        }

        suggestion.Status = AiSuggestionStatus.Sent;
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);
    }

    public async Task MarkReplyRejectedAsync(int suggestionId, CancellationToken cancellationToken = default)
    {
        var suggestion = await _suggestionRepository.GetByIdAsync(suggestionId, cancellationToken);
        if (suggestion is null || suggestion.Status == AiSuggestionStatus.Rejected)
        {
            return;
        }

        suggestion.Status = AiSuggestionStatus.Rejected;
        await _suggestionRepository.UpdateAsync(suggestion, cancellationToken);
    }
}
