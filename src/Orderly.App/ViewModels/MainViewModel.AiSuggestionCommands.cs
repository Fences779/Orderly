using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanGenerateAiSuggestion))]
    private async Task GenerateAiSuggestionAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        if (IsBusy)
        {
            return;
        }

        var context = GetConversationContext(customer);

        try
        {
            IsGeneratingAiSuggestion = true;
            StatusMessage = "正在生成本地 AI 建议...";

            var created = await _aiAssistantService.GenerateAndSaveReplySuggestionAsync(
                customer.Id,
                context.OrderId,
                context.DealId);

            await ReloadSelectedCustomerDetailsAsync(customer);
            SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == created.Id) ?? AiSuggestions.FirstOrDefault();
            StatusMessage = $"本地 AI 建议已生成并写入{context.ContextLabel}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成 AI 建议失败：{ex.Message}";
            ShowErrorMessage("生成 AI 建议失败", ex);
        }
        finally
        {
            IsGeneratingAiSuggestion = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAcceptAiSuggestion))]
    private async Task AcceptAiSuggestionAsync(AiSuggestionListItem? suggestionItem)
    {
        await UpdateAiSuggestionStatusAsync(
            suggestionItem,
            AiSuggestionStatus.Accepted,
            "正在接受 AI 建议...",
            "AI 建议已接受，仅更新本地状态",
            "接受 AI 建议失败");
    }

    [RelayCommand(CanExecute = nameof(CanRejectAiSuggestion))]
    private async Task RejectAiSuggestionAsync(AiSuggestionListItem? suggestionItem)
    {
        await UpdateAiSuggestionStatusAsync(
            suggestionItem,
            AiSuggestionStatus.Rejected,
            "正在拒绝 AI 建议...",
            "AI 建议已拒绝，仅更新本地状态",
            "拒绝 AI 建议失败");
    }

    private async Task UpdateAiSuggestionStatusAsync(
        AiSuggestionListItem? suggestionItem,
        AiSuggestionStatus status,
        string busyMessage,
        string successMessage,
        string errorPrefix)
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
            busyMessage,
            successMessage,
            errorPrefix,
            errorPrefix,
            async () =>
            {
                await _aiAssistantService.UpdateSuggestionStatusAsync(target.Id, status, context.DealId);
                await ReloadSelectedCustomerDetailsAsync(customer);
                SelectedAiSuggestion = AiSuggestions.FirstOrDefault(item => item.Id == target.Id) ?? AiSuggestions.FirstOrDefault();
            });
    }

    private bool CanGenerateAiSuggestion()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanAcceptAiSuggestion(AiSuggestionListItem? suggestionItem)
    {
        return CanReviewAiSuggestion(suggestionItem);
    }

    private bool CanRejectAiSuggestion(AiSuggestionListItem? suggestionItem)
    {
        return CanReviewAiSuggestion(suggestionItem);
    }

    private bool CanReviewAiSuggestion(AiSuggestionListItem? suggestionItem)
    {
        var target = suggestionItem ?? SelectedAiSuggestion;
        return target is not null
            && target.Status == AiSuggestionStatus.Draft
            && !IsBusy;
    }

    private void NotifyAiSuggestionCommandStateChanged()
    {
        NotifyCommandStateChanged(
            GenerateAiSuggestionCommand,
            AcceptAiSuggestionCommand,
            RejectAiSuggestionCommand);
    }
}
