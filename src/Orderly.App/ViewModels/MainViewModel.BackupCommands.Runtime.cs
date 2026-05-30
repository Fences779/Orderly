using Orderly.Core.Models;
using System.IO;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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

    private string GetDefaultBackupDirectory()
    {
        var directory = ResolveBackupDirectory(BackupDirectoryInput);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
