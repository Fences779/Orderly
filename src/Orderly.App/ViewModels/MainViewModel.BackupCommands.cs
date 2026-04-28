using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using System.IO;
using System.Text.Json;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
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

    [RelayCommand(CanExecute = nameof(CanManageBackup))]
    private async Task ValidateBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "校验本地备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetDefaultBackupDirectory()
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在校验本地备份...",
            successMessage: "备份校验成功",
            errorTitle: "校验备份失败",
            errorStatusPrefix: "校验备份失败",
            action: async () =>
            {
                var result = await _backupService.ValidateAsync(dialog.FileName);
                if (!result.IsValid)
                {
                    throw new InvalidOperationException(string.Join("；", result.Errors));
                }
            });
    }

    private bool CanManageBackup()
    {
        return !IsBusy;
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
            "OcrResults"
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
}
