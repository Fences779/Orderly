using CommunityToolkit.Mvvm.Input;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanPrepareAutoReplyDraft))]
    private async Task PrepareAutoReplyDraftAsync(AiSuggestionListItem? suggestionItem)
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var target = suggestionItem ?? SelectedAiSuggestion;
        if (target is null)
        {
            StatusMessage = "请先选择一条 AI 建议";
            return;
        }

        var context = GetConversationContext(customer);
        await ExecuteSaveActionAsync(
            "正在准备回复草稿...",
            $"回复草稿已写入{context.ContextLabel}，仅本地保存，未发送",
            "准备回复草稿失败",
            "准备回复草稿失败",
            async () =>
            {
                var prepared = await _autoReplyService.PrepareReplyAsync(target.Id);
                if (prepared is null)
                {
                    throw new InvalidOperationException($"AI 建议不存在：{target.Id}");
                }

                await ReloadSelectedCustomerDetailsAsync(customer);
                SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == target.Id) ?? AiSuggestions.FirstOrDefault();
            });
    }

    [RelayCommand(CanExecute = nameof(CanMarkAutoReplySent))]
    private async Task MarkAutoReplySentAsync(AiSuggestionListItem? suggestionItem)
    {
        await UpdateAutoReplyDraftAsync(
            suggestionItem,
            busyMessage: "正在标记回复已发送...",
            successMessage: "回复草稿已标记为已发送，仅更新本地状态",
            errorPrefix: "标记回复已发送失败",
            async target => await _autoReplyService.MarkReplySentAsync(target.Id));
    }

    [RelayCommand(CanExecute = nameof(CanRejectAutoReplyDraft))]
    private async Task RejectAutoReplyDraftAsync(AiSuggestionListItem? suggestionItem)
    {
        await UpdateAutoReplyDraftAsync(
            suggestionItem,
            busyMessage: "正在拒绝回复草稿...",
            successMessage: "回复草稿已拒绝，仅更新本地状态",
            errorPrefix: "拒绝回复草稿失败",
            async target => await _autoReplyService.MarkReplyRejectedAsync(target.Id));
    }

    private async Task UpdateAutoReplyDraftAsync(
        AiSuggestionListItem? suggestionItem,
        string busyMessage,
        string successMessage,
        string errorPrefix,
        Func<AiSuggestionListItem, Task> action)
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var target = suggestionItem ?? SelectedAiSuggestion;
        if (target is null)
        {
            StatusMessage = "请先选择一条回复草稿";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage,
            successMessage,
            errorPrefix,
            errorPrefix,
            async () =>
            {
                await action(target);
                await ReloadSelectedCustomerDetailsAsync(customer);
                SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == target.Id) ?? AiSuggestions.FirstOrDefault();
            });
    }

    private bool CanPrepareAutoReplyDraft(AiSuggestionListItem? suggestionItem)
    {
        var target = suggestionItem ?? SelectedAiSuggestion;
        return target is not null
            && target.CanPrepareDraft
            && !IsBusy;
    }

    private bool CanMarkAutoReplySent(AiSuggestionListItem? suggestionItem)
    {
        var target = suggestionItem ?? SelectedAiSuggestion;
        return target is not null
            && target.CanMarkSent
            && !IsBusy;
    }

    private bool CanRejectAutoReplyDraft(AiSuggestionListItem? suggestionItem)
    {
        var target = suggestionItem ?? SelectedAiSuggestion;
        return target is not null
            && target.CanRejectDraft
            && !IsBusy;
    }

    private void NotifyAutoReplyCommandStateChanged()
    {
        PrepareAutoReplyDraftCommand.NotifyCanExecuteChanged();
        MarkAutoReplySentCommand.NotifyCanExecuteChanged();
        RejectAutoReplyDraftCommand.NotifyCanExecuteChanged();
    }
}
