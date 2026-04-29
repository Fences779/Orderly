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
                var result = await _backupService.ExportAsync(dialog.FileName);
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
            return;
        }

        RestoreStatusText = $"已选择：{Path.GetFileName(value)}";
        RestoreDetailText = "已切换备份文件，请先生成恢复预览；旧确认状态已清空。";
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
        return !IsBusy;
    }

    private bool CanValidateBackup()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SelectedBackupPath);
    }

    private bool CanRestoreBackup()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(SelectedBackupPath)
            && RestorePreview is not null
            && string.Equals(RestorePreview.BackupPath, SelectedBackupPath, StringComparison.OrdinalIgnoreCase)
            && CanRestoreWithConfirmation;
    }

    private async Task LoadRecentBackupStatusAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _backupService.GetLatestBackupAsync(cancellationToken);
        if (latest is null)
        {
            RecentBackupStatusText = "暂无本地备份";
            RecentBackupDetailText = "导出后会在这里显示最近一次本地备份状态。";
            return;
        }

        UpdateRecentBackupStatus(latest);
    }

    private async Task RefreshRestoreRuntimeStatusAsync(bool updateStatusText)
    {
        await LoadRecentBackupStatusAsync();

        if (string.IsNullOrWhiteSpace(SelectedBackupPath))
        {
            ApplyRestorePreview(preview: null, updateStatusText: updateStatusText, resetConfirmation: true);
            return;
        }

        try
        {
            var preview = await _backupService.PreviewRestoreAsync(SelectedBackupPath, createdBy: "p2.9");
            ApplyRestorePreview(preview, updateStatusText, resetConfirmation: true);
        }
        catch
        {
            ApplyRestorePreview(preview: null, updateStatusText: false, resetConfirmation: true);

            if (updateStatusText)
            {
                RestoreStatusText = "恢复预览刷新失败";
                RestoreDetailText = "当前文件的恢复预览未能刷新，请重新生成恢复预览。";
            }
        }
    }

    private void UpdateRecentBackupStatus(BackupResult result)
    {
        var exportedAt = result.Manifest.ExportedAt == default
            ? string.Empty
            : result.Manifest.ExportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var fileName = string.IsNullOrWhiteSpace(result.BackupPath)
            ? "未记录文件名"
            : Path.GetFileName(result.BackupPath);

        if (result.SyncStatus == SyncStatus.Synced)
        {
            var countsText = FormatBackupCounts(result.Manifest.Counts);
            RecentBackupStatusText = string.IsNullOrWhiteSpace(exportedAt)
                ? $"最近备份：成功 · {fileName}"
                : $"最近备份：{exportedAt} · 成功";
            RecentBackupDetailText = string.IsNullOrWhiteSpace(countsText)
                ? $"文件：{result.BackupPath}"
                : $"文件：{result.BackupPath}\n范围：{countsText}";
            return;
        }

        RecentBackupStatusText = "最近备份：失败";
        RecentBackupDetailText = string.IsNullOrWhiteSpace(result.ErrorSummary)
            ? $"文件：{result.BackupPath}"
            : $"文件：{result.BackupPath}\n原因：{result.ErrorSummary}";
    }

    private void ApplyRestorePreview(BackupRestorePreviewResult? preview, bool updateStatusText, bool resetConfirmation)
    {
        if (resetConfirmation)
        {
            IsRestoreRiskConfirmed = false;
        }

        RestorePreview = preview;

        if (!updateStatusText)
        {
            return;
        }

        if (preview is null)
        {
            RestoreStatusText = string.IsNullOrWhiteSpace(SelectedBackupPath)
                ? "未选择恢复备份"
                : $"已选择：{Path.GetFileName(SelectedBackupPath)}";
            RestoreDetailText = string.IsNullOrWhiteSpace(SelectedBackupPath)
                ? "先选择备份文件，再生成恢复预览。"
                : "已切换备份文件，请先生成恢复预览。";
            return;
        }

        UpdateRestorePreviewStatus(preview);
    }

    private void UpdateRestorePreviewStatus(BackupRestorePreviewResult preview)
    {
        RestoreStatusText = preview.CanRestore
            ? $"恢复预览已就绪：{preview.FileName}"
            : $"恢复预览已拒绝：{preview.FileName}";

        var detailLines = new List<string>
        {
            $"目标状态：{GetRestoreTargetCode(preview.TargetState)} / {GetRestoreTargetLabel(preview.TargetState)}",
            $"是否允许恢复：{(preview.CanRestore ? "是" : "否")}",
            $"结果：{preview.Summary}"
        };

        if (!string.IsNullOrWhiteSpace(preview.RefuseReason))
        {
            detailLines.Add($"拒绝原因：{preview.RefuseReason}");
        }

        RestoreDetailText = string.Join("\n", detailLines);
    }

    private void UpdateRestoreResultStatus(BackupResult result)
    {
        var fileName = string.IsNullOrWhiteSpace(result.BackupPath)
            ? "未记录文件名"
            : Path.GetFileName(result.BackupPath);
        var countsText = FormatBackupCounts(result.Manifest.Counts);
        var restoredAt = result.CompletedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

        RestoreStatusText = string.IsNullOrWhiteSpace(restoredAt)
            ? $"恢复成功：{fileName}"
            : $"恢复成功：{restoredAt}";

        var detailLines = new List<string>
        {
            $"文件：{result.BackupPath}",
            $"目标：{GetRestoreTargetCode(result.TargetState)} / {GetRestoreTargetLabel(result.TargetState)}"
        };

        if (!string.IsNullOrWhiteSpace(countsText))
        {
            detailLines.Add($"恢复范围：{countsText}");
        }

        if (result.QaDataCleared)
        {
            detailLines.Add("已先清理 QA/测试数据。");
        }

        RestoreDetailText = string.Join("\n", detailLines);
    }

    private void NotifyRestorePreviewStateChanged()
    {
        OnPropertyChanged(nameof(HasRestorePreview));
        OnPropertyChanged(nameof(CanConfirmRestoreRisk));
        OnPropertyChanged(nameof(RestorePreviewFileName));
        OnPropertyChanged(nameof(RestorePreviewExportedAtText));
        OnPropertyChanged(nameof(RestorePreviewSchemaVersionText));
        OnPropertyChanged(nameof(RestorePreviewChecksumText));
        OnPropertyChanged(nameof(RestorePreviewChecksumStatusText));
        OnPropertyChanged(nameof(RestorePreviewCountsText));
        OnPropertyChanged(nameof(RestorePreviewTargetCountsText));
        OnPropertyChanged(nameof(RestorePreviewTargetStateCodeText));
        OnPropertyChanged(nameof(RestorePreviewTargetStateText));
        OnPropertyChanged(nameof(RestorePreviewWillClearQaDataText));
        OnPropertyChanged(nameof(RestorePreviewCanRestoreText));
        OnPropertyChanged(nameof(RestorePreviewRefuseReasonText));
        OnPropertyChanged(nameof(RestoreRiskPromptText));
        OnPropertyChanged(nameof(RestoreRiskConfirmationText));
        OnPropertyChanged(nameof(CanRestoreWithConfirmation));
    }

    private static string GetDefaultBackupDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Orderly",
            "Backups");

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FormatBackupCounts(IReadOnlyDictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return string.Empty;
        }

        var prioritizedKeys = new[]
        {
            "Customers",
            "Deals",
            "Orders",
            "FollowUps",
            "CustomerNotes",
            "PriceAdjustments",
            "ConversationMessages",
            "AiSuggestions",
            "OcrResults",
            "ActivityLogs",
            "SyncRecords"
        };

        var parts = new List<string>();
        foreach (var key in prioritizedKeys)
        {
            if (counts.TryGetValue(key, out var count))
            {
                parts.Add($"{key}:{count}");
            }
        }

        return string.Join(" / ", parts);
    }

    private static string GetRestoreTargetCode(BackupRestoreTargetState targetState)
    {
        return targetState switch
        {
            BackupRestoreTargetState.EmptyDatabase => "Empty",
            BackupRestoreTargetState.QaDatabase => "QaOnly",
            BackupRestoreTargetState.NonEmptyProductionDatabase => "ProductionNonEmpty",
            _ => "Unknown"
        };
    }

    private static string GetRestoreTargetLabel(BackupRestoreTargetState targetState)
    {
        return targetState switch
        {
            BackupRestoreTargetState.EmptyDatabase => "空库",
            BackupRestoreTargetState.QaDatabase => "QA/测试库",
            BackupRestoreTargetState.NonEmptyProductionDatabase => "非空生产库",
            _ => "未知"
        };
    }
}
