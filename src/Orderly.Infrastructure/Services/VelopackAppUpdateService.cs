using System.IO;
using System.Reflection;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Velopack;
using Velopack.Sources;

namespace Orderly.Infrastructure.Services;

public sealed class VelopackAppUpdateService : IAppUpdateService
{
    private const string StableChannel = "stable";
    private const string DefaultGithubRepoUrl = "https://github.com/Fences779/Orderly";
    private const string UpdateSourceUrlEnvName = "ORDERLY_UPDATE_SOURCE_URL";

    private UpdateInfo? _cachedUpdateInfo;
    private VelopackAsset? _downloadedUpdate;

    public AppUpdateSupportInfo GetSupportInfo()
    {
        try
        {
            var source = ResolveSourceConfiguration();
            var manager = CreateUpdateManager(source);
            if (!manager.IsInstalled)
            {
                return new AppUpdateSupportInfo(
                    IsSupported: false,
                    Channel: StableChannel,
                    SourceDescription: source.Description,
                    StatusText: "当前为未安装开发版，需通过 Setup 安装后才能检查更新。");
            }

            return new AppUpdateSupportInfo(
                IsSupported: true,
                Channel: StableChannel,
                SourceDescription: source.Description,
                StatusText: $"已接入 {StableChannel} 更新源：{source.Description}");
        }
        catch (Exception ex)
        {
            return new AppUpdateSupportInfo(
                IsSupported: false,
                Channel: StableChannel,
                SourceDescription: "无效更新源配置",
                StatusText: ex.Message);
        }
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = ResolveCurrentVersion(manager: null);
        try
        {
            var source = ResolveSourceConfiguration();
            var manager = CreateUpdateManager(source);
            currentVersion = ResolveCurrentVersion(manager);
            if (!manager.IsInstalled)
            {
                _cachedUpdateInfo = null;
                _downloadedUpdate = null;
                return new AppUpdateCheckResult(
                    State: AppUpdateState.Unsupported,
                    StatusText: "当前为未安装开发版，需通过 Setup 安装后才能检查更新。",
                    CurrentVersion: currentVersion);
            }

            var pendingUpdate = manager.UpdatePendingRestart;
            if (pendingUpdate is not null)
            {
                _cachedUpdateInfo = null;
                _downloadedUpdate = pendingUpdate;
                var pendingVersion = NormalizeDisplayVersion(pendingUpdate.Version?.ToString());
                return new AppUpdateCheckResult(
                    State: AppUpdateState.PendingRestart,
                    StatusText: $"更新 {pendingVersion} 已下载完成，重启后完成安装。",
                    CurrentVersion: currentVersion,
                    AvailableVersion: pendingVersion,
                    ReleaseNotesMarkdown: pendingUpdate.NotesMarkdown);
            }

            var updateInfo = await manager.CheckForUpdatesAsync();
            _cachedUpdateInfo = updateInfo;
            _downloadedUpdate = null;
            if (updateInfo is null)
            {
                return new AppUpdateCheckResult(
                    State: AppUpdateState.UpToDate,
                    StatusText: $"已是最新版（{currentVersion}）。",
                    CurrentVersion: currentVersion);
            }

            var targetVersion = NormalizeDisplayVersion(updateInfo.TargetFullRelease.Version?.ToString());
            return new AppUpdateCheckResult(
                State: AppUpdateState.UpdateAvailable,
                StatusText: $"发现新版本 {targetVersion}，可立即下载。",
                CurrentVersion: currentVersion,
                AvailableVersion: targetVersion,
                ReleaseNotesMarkdown: updateInfo.TargetFullRelease.NotesMarkdown);
        }
        catch (Exception ex)
        {
            _cachedUpdateInfo = null;
            _downloadedUpdate = null;
            return new AppUpdateCheckResult(
                State: AppUpdateState.Failed,
                StatusText: ex.Message,
                CurrentVersion: currentVersion);
        }
    }

    public async Task<AppUpdateDownloadResult> DownloadPendingUpdateAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_downloadedUpdate is not null)
        {
            return new AppUpdateDownloadResult(
                IsSuccess: true,
                StatusText: $"更新 {NormalizeDisplayVersion(_downloadedUpdate.Version?.ToString())} 已下载完成，重启后完成安装。",
                TargetVersion: NormalizeDisplayVersion(_downloadedUpdate.Version?.ToString()));
        }

        var updateInfo = _cachedUpdateInfo ?? throw new InvalidOperationException("当前没有可下载的更新，请先执行检查更新。");
        var source = ResolveSourceConfiguration();
        var manager = CreateUpdateManager(source);
        if (!manager.IsInstalled)
        {
            throw new InvalidOperationException("当前为未安装开发版，需通过 Setup 安装后才能下载更新。");
        }

        await manager.DownloadUpdatesAsync(updateInfo, progress ?? (_ => { }), cancellationToken);
        _downloadedUpdate = manager.UpdatePendingRestart ?? updateInfo.TargetFullRelease;
        _cachedUpdateInfo = null;
        var targetVersion = NormalizeDisplayVersion(_downloadedUpdate.Version?.ToString());
        return new AppUpdateDownloadResult(
            IsSuccess: true,
            StatusText: $"更新 {targetVersion} 已下载完成，重启后完成安装。",
            TargetVersion: targetVersion);
    }

    public void ApplyPendingUpdateAndRestart(string[]? restartArgs = null)
    {
        var source = ResolveSourceConfiguration();
        var manager = CreateUpdateManager(source);
        if (!manager.IsInstalled)
        {
            throw new InvalidOperationException("当前为未安装开发版，需通过 Setup 安装后才能安装更新。");
        }

        var pendingUpdate = manager.UpdatePendingRestart ?? _downloadedUpdate;
        if (pendingUpdate is null)
        {
            throw new InvalidOperationException("当前没有待安装的更新。");
        }

        manager.ApplyUpdatesAndRestart(pendingUpdate, restartArgs ?? Array.Empty<string>());
    }

    private static UpdateManager CreateUpdateManager(UpdateSourceConfiguration source)
    {
        return new UpdateManager(
            source.Source,
            new UpdateOptions
            {
                ExplicitChannel = StableChannel
            },
            locator: null);
    }

    private static UpdateSourceConfiguration ResolveSourceConfiguration()
    {
        var customSourceUrl = Environment.GetEnvironmentVariable(UpdateSourceUrlEnvName)?.Trim();
        if (!string.IsNullOrWhiteSpace(customSourceUrl))
        {
            if (Uri.TryCreate(customSourceUrl, UriKind.Absolute, out var absoluteUri))
            {
                if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return new UpdateSourceConfiguration(
                        Source: new SimpleWebSource(absoluteUri, null!, 30d),
                        Description: $"自定义 Web 更新源（{absoluteUri}）",
                        UsesGithubReleases: false);
                }

                if (absoluteUri.IsFile)
                {
                    return CreateLocalSourceConfiguration(absoluteUri.LocalPath, customSourceUrl);
                }
            }

            if (Path.IsPathRooted(customSourceUrl))
            {
                return CreateLocalSourceConfiguration(customSourceUrl, customSourceUrl);
            }

            throw new InvalidOperationException(
                $"环境变量 {UpdateSourceUrlEnvName} 配置无效：仅支持 http/https、本地绝对路径或 file URI。当前值：{customSourceUrl}");
        }

        return new UpdateSourceConfiguration(
            Source: new GithubSource(DefaultGithubRepoUrl, string.Empty, false, null!),
            Description: $"GitHub Releases（{DefaultGithubRepoUrl}）",
            UsesGithubReleases: true);
    }

    private static UpdateSourceConfiguration CreateLocalSourceConfiguration(string directoryPath, string originalValue)
    {
        var fullPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"环境变量 {UpdateSourceUrlEnvName} 指向的本地更新目录不存在：{originalValue}（解析后：{fullPath}）");
        }

        var releasesJsonPath = Path.Combine(fullPath, $"releases.{StableChannel}.json");
        if (!File.Exists(releasesJsonPath))
        {
            throw new InvalidOperationException(
                $"本地更新目录缺少 Velopack 清单 releases.{StableChannel}.json：{fullPath}");
        }

        return new UpdateSourceConfiguration(
            Source: new SimpleFileSource(new DirectoryInfo(fullPath)),
            Description: $"本地文件更新源（{fullPath}）",
            UsesGithubReleases: false);
    }

    private static string ResolveCurrentVersion(UpdateManager? manager)
    {
        if (manager is not null)
        {
            var managerVersion = NormalizeDisplayVersion(manager.CurrentVersion?.ToString());
            if (!string.IsNullOrWhiteSpace(managerVersion))
            {
                return managerVersion;
            }
        }

        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return NormalizeDisplayVersion(
            entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entryAssembly.GetName().Version?.ToString())
            ?? "未知";
    }

    private static string? NormalizeDisplayVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        var metadataSeparatorIndex = trimmed.IndexOf('+');
        if (metadataSeparatorIndex >= 0)
        {
            trimmed = trimmed[..metadataSeparatorIndex];
        }

        return trimmed;
    }

    private sealed record UpdateSourceConfiguration(
        IUpdateSource Source,
        string Description,
        bool UsesGithubReleases);
}
