using System.Text;
using CommunityToolkit.Mvvm.Input;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ImportInventoryWorkbookAsync()
    {
        if (IsBusy || IsInventoryLoading)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入库存 Excel",
            Filter = "Excel 工作簿|*.xlsx;*.xlsm",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        try
        {
            IsSaving = true;
            InventoryError = string.Empty;
            StatusMessage = "正在分析库存 Excel...";

            var preview = await _inventoryWorkspaceService.PrepareWorkbookImportAsync(dialog.FileName);
            if (!preview.CanCommit)
            {
                InventoryError = $"库存 Excel 校验失败：{BuildInventoryImportErrorSummary(preview)}";
                StatusMessage = InventoryError;
                ShowInventoryImportErrors(preview);
                return;
            }

            var confirmResult = System.Windows.MessageBox.Show(
                GetDialogOwner(),
                BuildInventoryImportConfirmMessage(preview),
                "确认同步库存 Excel",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.No);

            if (confirmResult != System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "已取消库存 Excel 同步。";
                return;
            }

            StatusMessage = "正在同步库存到云端并回写 Excel...";
            var commitResult = await _inventoryWorkspaceService.CommitWorkbookImportAsync(preview, writeBackWorkbook: true);
            await RefreshInventoryAsync();

            InventoryError = string.Empty;
            StatusMessage = $"库存同步完成：新增 {commitResult.InsertedCount}，更新 {commitResult.UpdatedCount}，未变化 {commitResult.UnchangedCount}";

            if (!string.IsNullOrWhiteSpace(commitResult.BackupPath))
            {
                System.Windows.MessageBox.Show(
                    GetDialogOwner(),
                    $"库存云同步已完成。\n\n批次：{commitResult.BatchNo}\n已回写：{commitResult.WorkbookPath}\n备份：{commitResult.BackupPath}",
                    "库存同步完成",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            InventoryError = $"库存 Excel 同步失败：{ex.Message}";
            StatusMessage = InventoryError;
            ShowErrorMessage("库存 Excel 同步失败", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ExportInventoryWorkbookAsync()
    {
        if (IsBusy || IsInventoryLoading)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出库存 Excel",
            Filter = "Excel 工作簿|*.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
            FileName = $"inventory-export-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在导出库存 Excel...",
            successMessage: $"库存 Excel 已导出：{dialog.FileName}",
            errorTitle: "导出库存 Excel 失败",
            errorStatusPrefix: "导出库存 Excel 失败",
            action: async () =>
            {
                await _inventoryWorkspaceService.ExportWorkbookAsync(dialog.FileName);
                InventoryError = string.Empty;
            });
    }

    private static string BuildInventoryImportConfirmMessage(Orderly.Core.Models.InventoryImportPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"文件：{preview.SourceFileName}");
        builder.AppendLine(preview.SummaryText);
        builder.AppendLine();
        builder.AppendLine("确认后将执行：");
        builder.AppendLine("1. 把本次库存快照同步到云端主库");
        builder.AppendLine("2. 生成原 Excel 备份");
        builder.AppendLine("3. 用确认后的云端快照回写当前 Excel");
        builder.AppendLine();
        builder.Append("是否继续？");
        return builder.ToString();
    }

    private static string BuildInventoryImportErrorSummary(Orderly.Core.Models.InventoryImportPreviewResult preview)
    {
        if (preview.Errors.Count == 0)
        {
            return "存在未识别的校验错误。";
        }

        var first = preview.Errors[0];
        return $"第 {first.RowNumber} 行：{first.Message}";
    }

    private static void ShowInventoryImportErrors(Orderly.Core.Models.InventoryImportPreviewResult preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"文件：{preview.SourceFileName}");
        builder.AppendLine($"错误数：{preview.Errors.Count}");
        builder.AppendLine();

        foreach (var error in preview.Errors.Take(10))
        {
            builder.AppendLine($"第 {error.RowNumber} 行：{error.Message}");
        }

        if (preview.Errors.Count > 10)
        {
            builder.AppendLine("...");
        }

        builder.AppendLine();
        builder.AppendLine("请先修正 Excel，再重新导入。");

        System.Windows.MessageBox.Show(
            GetDialogOwner(),
            builder.ToString(),
            "库存 Excel 校验失败",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }
}
