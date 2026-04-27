using CommunityToolkit.Mvvm.Input;
using Orderly.App.Views;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanAddNote))]
    private async Task AddNoteAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        try
        {
            StatusMessage = "正在新增备注...";
            var (dialog, result) = await ShowDialogAsync(() => new AddNoteDialog(ReplyTemplates));

            if (result != true)
            {
                StatusMessage = "已取消新增备注";
                return;
            }

            await ExecuteSaveActionAsync(
                busyMessage: "正在保存备注...",
                successMessage: "备注已保存",
                errorTitle: "新增备注失败",
                errorStatusPrefix: "保存备注失败",
                action: async () =>
                {
                    var metadataJson = CreateNoteActivityMetadataJson(dialog.InsertedTemplate);
                    await _noteService.SaveNoteAsync(new CustomerNote
                    {
                        CustomerId = customer.Id,
                        DealId = SelectedDeal?.Id,
                        OrderId = SelectedOrder?.Id,
                        Type = dialog.SelectedNoteType,
                        Content = dialog.NoteContent
                    }, metadataJson);

                    await ReloadSelectedCustomerDetailsAsync(customer);
                });
        }
        catch (Exception ex)
        {
            StatusMessage = $"新增备注失败：{ex.Message}";
            ShowErrorMessage("新增备注失败", ex);
        }
    }

    [RelayCommand]
    private void CopyTemplate(ReplyTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        _clipboardService.SetText(template.Content);
        StatusMessage = $"已复制话术：{template.Title}";
    }
}
