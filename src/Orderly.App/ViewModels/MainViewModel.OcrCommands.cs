using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using System.IO;
using System.Text.Json.Nodes;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private const string LocalOcrFallbackText = "【本地OCR占位】请人工确认截图内容后转为沟通记录。";

    [RelayCommand(CanExecute = nameof(CanSelectOcrImage))]
    private async Task SelectOcrImageAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择图片 / 导入截图",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        var filePath = dialog.FileName;
        var fileName = Path.GetFileName(filePath);
        var fileExists = File.Exists(filePath);
        var context = GetConversationContext(customer);

        await ExecuteSaveActionAsync(
            busyMessage: "正在执行 OCR...",
            successMessage: "OCR 已完成，可转为沟通记录",
            errorTitle: "OCR 执行失败",
            errorStatusPrefix: "OCR 执行失败",
            action: async () =>
            {
                var created = await _ocrService.CreateOcrTaskAsync(new OcrResult
                {
                    CustomerId = customer.Id,
                    OrderId = context.OrderId,
                    SourcePath = filePath,
                    SourceName = fileName,
                    MetadataJson = BuildOcrMetadataJson(fileName, fileExists)
                });

                CurrentOcrResult = created;

                OcrResult finalResult;
                if (!fileExists)
                {
                    finalResult = await _ocrService.FailOcrTaskAsync(created.Id, $"图片文件不存在：{fileName}")
                        ?? throw new InvalidOperationException($"OCR 任务不存在：{created.Id}。");
                }
                else
                {
                    finalResult = await _ocrService.CompleteOcrTaskAsync(created.Id, LocalOcrFallbackText)
                        ?? throw new InvalidOperationException($"OCR 任务不存在：{created.Id}。");
                }

                CurrentOcrResult = finalResult;
                await ReloadSelectedCustomerDetailsAsync(customer);

                if (finalResult.Status == OcrStatus.Failed)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(finalResult.ErrorMessage)
                        ? "OCR 执行失败。"
                        : finalResult.ErrorMessage);
                }
            });
    }

    [RelayCommand(CanExecute = nameof(CanConvertOcrToConversationMessage))]
    private async Task ConvertOcrToConversationMessageAsync()
    {
        var customer = SelectedCustomer;
        var ocrResult = CurrentOcrResult;
        if (customer is null || ocrResult is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var context = GetConversationContext(customer);

        await ExecuteSaveActionAsync(
            busyMessage: "正在转为沟通记录...",
            successMessage: "OCR 文本已转为沟通记录",
            errorTitle: "转沟通记录失败",
            errorStatusPrefix: "转沟通记录失败",
            action: async () =>
            {
                await _ocrService.ConvertToConversationMessageAsync(ocrResult.Id, customer.Name, context.DealId);
                await ReloadSelectedCustomerDetailsAsync(customer);
            });
    }

    private bool CanSelectOcrImage()
    {
        return SelectedCustomer is not null && !IsBusy;
    }

    private bool CanConvertOcrToConversationMessage()
    {
        return SelectedCustomer is not null
            && CurrentOcrResult is { Status: OcrStatus.Completed }
            && !string.IsNullOrWhiteSpace(CurrentOcrResult.ExtractedText)
            && !IsCurrentOcrConverted
            && !IsBusy;
    }

    private static string BuildOcrMetadataJson(string fileName, bool fileExists)
    {
        var root = new JsonObject
        {
            ["source"] = "manual-image",
            ["createdBy"] = "p2.5",
            ["provider"] = "local",
            ["usedFallback"] = true,
            ["fileName"] = fileName,
            ["fileExists"] = fileExists
        };

        return root.ToJsonString();
    }

    private int? TryGetCurrentOcrConvertedMessageId()
    {
        var metadata = ParseOcrMetadata(CurrentOcrResult?.MetadataJson);
        return metadata["convertedToMessageId"]?.GetValue<int?>();
    }

    private static JsonObject ParseOcrMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            return new JsonObject();
        }
    }
}
