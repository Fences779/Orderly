using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private bool isCheckingForAppUpdate;

    private bool CanCheckForAppUpdate()
    {
        return !IsCheckingForAppUpdate && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanCheckForAppUpdate))]
    private async Task CheckForAppUpdateAsync()
    {
        if (_appUpdateService is null)
        {
            ApplyUpdateStatus("当前版本未接入更新服务。");
            return;
        }

        IsCheckingForAppUpdate = true;
        CheckForAppUpdateCommand.NotifyCanExecuteChanged();

        try
        {
            ApplyUpdateStatus("正在检查更新...");
            var result = await _appUpdateService.CheckForUpdatesAsync();
            ApplyUpdateStatus(result.StatusText);

            switch (result.State)
            {
                case AppUpdateState.UpdateAvailable:
                    if (ShouldDownloadUpdate(result))
                    {
                        await DownloadAndApplyUpdateAsync(result);
                    }
                    break;
                case AppUpdateState.PendingRestart:
                    if (ShouldRestartToApplyUpdate(result.AvailableVersion))
                    {
                        ApplyUpdateStatus(BuildRestartingStatus(result.AvailableVersion));
                        _appUpdateService.ApplyPendingUpdateAndRestart();
                    }
                    break;
            }
        }
        finally
        {
            IsCheckingForAppUpdate = false;
            CheckForAppUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task DownloadAndApplyUpdateAsync(AppUpdateCheckResult result)
    {
        try
        {
            ApplyUpdateStatus($"正在下载更新 {result.AvailableVersion}...");
            var downloadResult = await _appUpdateService!.DownloadPendingUpdateAsync(ReportUpdateDownloadProgress);
            ApplyUpdateStatus(downloadResult.StatusText);
            if (downloadResult.IsSuccess && ShouldRestartToApplyUpdate(downloadResult.TargetVersion))
            {
                ApplyUpdateStatus(BuildRestartingStatus(downloadResult.TargetVersion));
                _appUpdateService.ApplyPendingUpdateAndRestart();
            }
        }
        catch (Exception ex)
        {
            ApplyUpdateStatus($"下载失败：{ex.Message}");
            ShowErrorMessage("下载更新失败", ex);
        }
    }

    private void ReportUpdateDownloadProgress(int progress)
    {
        var normalizedProgress = Math.Clamp(progress, 0, 100);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        void ApplyProgress()
        {
            ApplyUpdateStatus($"正在下载更新... {normalizedProgress}%");
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyProgress();
            return;
        }

        dispatcher.Invoke(ApplyProgress);
    }

    private void ApplyUpdateStatus(string status)
    {
        UpdateCheckStatusText = status;
        SettingsStatusMessage = status;
    }

    private bool ShouldDownloadUpdate(AppUpdateCheckResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前版本：{result.CurrentVersion}");
        builder.AppendLine($"发现新版本：{result.AvailableVersion}");
        builder.AppendLine();
        builder.AppendLine("是否现在开始下载？");

        var releaseNotes = BuildReleaseNotesPreview(result.ReleaseNotesMarkdown);
        if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            builder.AppendLine();
            builder.AppendLine("更新说明：");
            builder.AppendLine(releaseNotes);
        }

        return System.Windows.MessageBox.Show(
            GetDialogOwner(),
            builder.ToString(),
            "发现新版本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information) == MessageBoxResult.Yes;
    }

    private static bool ShouldRestartToApplyUpdate(string? targetVersion)
    {
        var versionText = string.IsNullOrWhiteSpace(targetVersion)
            ? "新版本"
            : $"版本 {targetVersion}";
        return System.Windows.MessageBox.Show(
            GetDialogOwner(),
            $"{versionText} 已下载完成，是否现在重启并完成更新？\n\n未保存的工作将被关闭。",
            "重启安装更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private static string BuildRestartingStatus(string? targetVersion)
    {
        return string.IsNullOrWhiteSpace(targetVersion)
            ? "正在重启并安装更新..."
            : $"正在重启并安装 {targetVersion}...";
    }

    private static string? BuildReleaseNotesPreview(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = markdown
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(6)
            .ToArray();

        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }
}
