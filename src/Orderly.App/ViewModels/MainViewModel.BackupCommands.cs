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
        RestoreStatusText = $"已选择：{Path.GetFileName(dialog.FileName)}";
        RestoreDetailText = $"文件：{dialog.FileName}";
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
            busyMessage: "正在校验恢复备份...",
            successMessage: "备份校验完成",
            errorTitle: "校验备份失败",
            errorStatusPrefix: "校验备份失败",
            action: async () =>
            {
                var preview = await _backupService.PreviewRestoreAsync(SelectedBackupPath);
                UpdateRestorePreviewStatus(preview);
            });
    }

    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private async Task RestoreBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsSaving = true;
            StatusMessage = "正在校验恢复条件...";

            var preview = await _backupService.PreviewRestoreAsync(SelectedBackupPath);
            UpdateRestorePreviewStatus(preview);

            if (!preview.Validation.IsValid)
            {
                throw new InvalidOperationException(string.Join("；", preview.Validation.Errors));
            }

            if (!preview.CanRestore)
            {
                throw new InvalidOperationException(preview.Errors.Count > 0
                    ? string.Join("；", preview.Errors)
                    : "当前目标库不满足恢复条件。");
            }

            var confirmation = System.Windows.MessageBox.Show(
                GetDialogOwner(),
                preview.RequiresQaDataClear
                    ? "将先清理当前 QA/测试数据，再按备份覆盖恢复。仅支持空库或测试库恢复，不覆盖已有生产数据。是否继续？"
                    : "将按所选备份恢复当前空库。仅支持空库或测试库恢复，不覆盖已有生产数据。是否继续？",
                "确认恢复备份",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirmation != System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "已取消恢复备份";
                return;
            }

            StatusMessage = "正在执行受控恢复...";
            var result = await _backupService.RestoreBackupAsync(
                SelectedBackupPath,
                clearQaDataIfNeeded: preview.RequiresQaDataClear);

            await LoadAsync();
            UpdateRestoreResultStatus(result);
            StatusMessage = "备份恢复完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复备份失败：{ex.Message}";
            ShowErrorMessage("恢复备份失败", ex);
        }
        finally
        {
            IsSaving = false;
        }
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
        return !IsBusy && !string.IsNullOrWhiteSpace(SelectedBackupPath);
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

    private void UpdateRestorePreviewStatus(BackupRestorePreviewResult preview)
    {
        var fileName = string.IsNullOrWhiteSpace(preview.BackupPath)
            ? "未选择文件"
            : Path.GetFileName(preview.BackupPath);
        var backupCounts = preview.Validation.Manifest?.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal);
        var targetCountsText = FormatBackupCounts(preview.TargetCounts);
        var backupCountsText = FormatBackupCounts(backupCounts);

        RestoreStatusText = preview.Validation.IsValid
            ? $"校验完成：{fileName}"
            : $"校验失败：{fileName}";

        var detailLines = new List<string>
        {
            $"文件：{preview.BackupPath}",
            $"结果：{preview.Summary}"
        };

        if (!string.IsNullOrWhiteSpace(backupCountsText))
        {
            detailLines.Add($"备份范围：{backupCountsText}");
        }

        if (!string.IsNullOrWhiteSpace(targetCountsText))
        {
            detailLines.Add($"目标库现状：{targetCountsText}");
        }

        if (preview.Errors.Count > 0)
        {
            detailLines.Add($"问题：{string.Join("；", preview.Errors)}");
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
            $"目标：{GetRestoreTargetLabel(result.TargetState)}"
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
            "Orders",
            "Deals",
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
