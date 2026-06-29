using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using System.IO;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanManageBackup))]
    private void SelectBackupFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择恢复备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetDefaultBackupDirectory()
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        SelectedBackupPath = dialog.FileName;
        StatusMessage = "已选择恢复备份文件";
    }

    [RelayCommand(CanExecute = nameof(CanManageBackup))]
    private async Task ExportBackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出本地备份",
            Filter = "JSON 文件|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
            InitialDirectory = GetDefaultBackupDirectory(),
            FileName = $"orderly-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在导出本地备份...",
            successMessage: "本地备份已导出",
            errorTitle: "导出备份失败",
            errorStatusPrefix: "导出备份失败",
            action: async () =>
            {
                var result = await _backupService.ExportAsync(
                    dialog.FileName,
                    includeSensitivePlaintext: IncludeSensitiveInExportInput);
                UpdateRecentBackupStatus(result);
            });
    }

    [RelayCommand(CanExecute = nameof(CanValidateBackup))]
    private async Task ValidateBackupAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在生成恢复预览...",
            successMessage: "恢复预览已更新",
            errorTitle: "生成恢复预览失败",
            errorStatusPrefix: "生成恢复预览失败",
            action: async () =>
            {
                var preview = await _backupService.PreviewRestoreAsync(SelectedBackupPath, createdBy: "p2.9");
                ApplyRestorePreview(preview, updateStatusText: true, resetConfirmation: true);
            });
    }

    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private async Task RestoreBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var preview = RestorePreview;
        if (preview is null || !string.Equals(preview.BackupPath, SelectedBackupPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请先为当前备份生成恢复预览。");
        }

        if (!preview.CanRestore)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(preview.RefuseReason)
                ? "当前预览不允许恢复。"
                : preview.RefuseReason);
        }

        if (!IsRestoreRiskConfirmed)
        {
            throw new InvalidOperationException("请先勾选恢复风险确认。");
        }

        try
        {
            IsSaving = true;
            StatusMessage = "正在执行受控恢复...";

            var result = await _backupService.RestoreBackupAsync(
                SelectedBackupPath,
                clearQaDataIfNeeded: preview.WillClearQaData,
                createdBy: "p2.9");

            await LoadAsync();
            UpdateRestoreResultStatus(result);
            await RefreshRestoreRuntimeStatusAsync(updateStatusText: false);
            StatusMessage = "备份恢复完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复备份失败：{ex.Message}";
            await RefreshRestoreRuntimeStatusAsync(updateStatusText: false);
            ShowErrorMessage("恢复备份失败", ex);
        }
        finally
        {
            IsSaving = false;
        }
    }

    partial void OnSelectedBackupPathChanged(string value)
    {
        ApplyRestorePreview(preview: null, updateStatusText: false, resetConfirmation: true);

        if (string.IsNullOrWhiteSpace(value))
        {
            RestoreStatusText = "未选择恢复备份";
            RestoreDetailText = "先选择备份文件，再生成恢复预览。";
            NotifyRestorePreviewStateChanged();
            return;
        }

        RestoreStatusText = $"已选择：{Path.GetFileName(value)}";
        RestoreDetailText = "已切换备份文件，请先生成恢复预览；旧确认状态已清空。";
        NotifyRestorePreviewStateChanged();
    }

    partial void OnRestorePreviewChanged(BackupRestorePreviewResult? value)
    {
        NotifyRestorePreviewStateChanged();
    }

    partial void OnIsRestoreRiskConfirmedChanged(bool value)
    {
        NotifyRestorePreviewStateChanged();
    }

    private bool CanManageBackup()
    {
        return IsCurrentUserOwner && !IsBusy;
    }

    private bool CanValidateBackup()
    {
        return IsCurrentUserOwner && !IsBusy && !string.IsNullOrWhiteSpace(SelectedBackupPath);
    }

    private bool CanRestoreBackup()
    {
        return IsCurrentUserOwner
            && !IsBusy
            && !string.IsNullOrWhiteSpace(SelectedBackupPath)
            && RestorePreview is not null
            && string.Equals(RestorePreview.BackupPath, SelectedBackupPath, StringComparison.OrdinalIgnoreCase)
            && CanRestoreWithConfirmation;
    }
}
