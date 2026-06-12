using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private async Task RefreshSettingsRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await RefreshDatabaseRuntimeStatusAsync(cancellationToken);
        await RefreshSnSyncStatusAsync(cancellationToken);
        RefreshSecurityRuntimeStatus();
        RefreshAiSettingsRuntimeStatus();
        RefreshNotificationSettingsRuntimeStatus();
        RefreshAppInfoRuntimeStatus();
    }

    private async Task RefreshDatabaseRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var info = new FileInfo(DatabasePath);
            DatabaseSizeText = info.Exists ? FormatFileSize(info.Length) : "数据库文件不存在";
        }
        catch (Exception ex)
        {
            DatabaseSizeText = $"读取失败：{ex.Message}";
        }

        var (status, detail) = await CheckDatabaseHealthAsync(cancellationToken);
        DatabaseHealthStatusText = status;
        DatabaseHealthDetailText = detail;
    }

    private void RefreshSecurityRuntimeStatus()
    {
        DatabaseEncryptionStatusText = "已启用 SQLCipher 全库加密（AES-256；账号库用会话数据密钥，启动器库用本机 DPAPI 密钥）；敏感字段额外列级加密。";
        BackupEncryptionStatusText = "本地备份为加密文件（AES-GCM 信封，配合 HMAC-SHA256 完整性校验，使用会话/本机保护密钥）。";
        LocalAccessProtectionStatusText = _sessionContextService?.IsSignedIn == true
            ? "已启用本地账号登录与 PIN 锁定链路。"
            : "未登录，无法确认本机访问保护状态。";
    }

    private void RefreshAppInfoRuntimeStatus()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        AppVersionText = entryAssembly.GetName().Version?.ToString() ?? "未知";

        var location = entryAssembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            var writeTime = File.GetLastWriteTime(location);
            AppBuildTimeText = $"{writeTime:yyyy-MM-dd HH:mm:ss}（文件时间）";
        }
        else
        {
            AppBuildTimeText = "未记录";
        }

        var lines = new List<string>
        {
            $"OS: {RuntimeInformation.OSDescription}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"进程架构: {RuntimeInformation.ProcessArchitecture}",
            $"数据库: {DatabasePath}",
            $"网关 endpoint: {(IsStringNarrationGatewayEndpointConfigured ? "已配置" : "未配置")}",
            $"网关 token: {(IsStringNarrationGatewayTokenConfigured ? "已配置" : "未配置")}"
        };
        RuntimeEnvironmentText = string.Join(Environment.NewLine, lines);
        SnCloudEnvironmentIdText = ResolveCloudEnvironmentId();
    }

    private async Task<(string Status, string Detail)> CheckDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath) || !File.Exists(DatabasePath))
        {
            return ("异常", "数据库文件不存在。");
        }

        var requiredTables = new[] { "AppSettings", "Customers", "Orders", "ActivityLogs", "SyncRecords" };
        var missingTables = new List<string>();
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            SqliteConnectionKeying.ApplyRawKey(connection, _sessionContextService?.Current?.DataKey?.ToArray());

            foreach (var table in requiredTables)
            {
                await using var tableCommand = connection.CreateCommand();
                tableCommand.CommandText = """
                    SELECT COUNT(1)
                    FROM sqlite_master
                    WHERE type = 'table' AND name = $name;
                    """;
                tableCommand.Parameters.AddWithValue("$name", table);
                var exists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                if (!exists)
                {
                    missingTables.Add(table);
                }
            }

            await using var quickCheckCommand = connection.CreateCommand();
            quickCheckCommand.CommandText = "PRAGMA quick_check;";
            var result = Convert.ToString(await quickCheckCommand.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
            if (!string.Equals(result.Trim(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                return ("异常", $"PRAGMA quick_check 返回：{result}");
            }
        }
        catch (Exception ex)
        {
            return ("异常", $"连接或校验失败：{ex.Message}");
        }

        return missingTables.Count > 0
            ? ("异常", $"缺少关键表：{string.Join(", ", missingTables)}")
            : ("正常", "数据库文件存在、可读、关键表齐全。");
    }

    private async Task RefreshSnSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _syncRecordRepository.GetLatestByEntityTypeAsync(SnSyncEntityType, cancellationToken);
        if (latest is not null)
        {
            SnLastSyncTimeText = latest.LastSyncedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? latest.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
            SnLastSyncResultText = latest.SyncStatus switch
            {
                SyncStatus.Synced => "成功",
                SyncStatus.Failed => $"失败：{latest.ErrorMessage}",
                _ => "进行中"
            };
            SnSyncLogSummaryText = $"SyncRecord#{latest.Id} / {latest.SyncStatus} / 更新时间 {latest.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
        }
        else
        {
            SnLastSyncTimeText = TryFormatSavedTime(Preferences.SnLastSyncAt, "未执行");
            SnLastSyncResultText = string.IsNullOrWhiteSpace(Preferences.SnLastSyncResult) ? "未执行" : Preferences.SnLastSyncResult;
            SnSyncLogSummaryText = "未发现 SyncRecords 同步记录。";
        }

        SnLastConnectionTimeText = TryFormatSavedTime(Preferences.SnLastConnectionCheckAt, "未检查");
        SnLastConnectionResultText = string.IsNullOrWhiteSpace(Preferences.SnLastConnectionResult) ? "未检查" : Preferences.SnLastConnectionResult;

        var failures = await GetSnSyncFailureLogsAsync(cancellationToken);
        if (failures.Count == 0)
        {
            SnSyncFailureSummaryText = "暂无失败记录";
            return;
        }

        var top = failures
            .Take(3)
            .Select(item => $"{item.CreatedAt:MM-dd HH:mm} {item.Description}")
            .ToArray();
        SnSyncFailureSummaryText = string.Join(Environment.NewLine, top);
    }

    private async Task<IReadOnlyList<ActivityLog>> GetFailureActivityLogsAsync(CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogService.GetRecentActivitiesAsync(500, cancellationToken);
        return logs
            .Where(item =>
                item.Type is ActivityType.SyncFailed or ActivityType.BackupValidationFailed or ActivityType.BackupRestoreFailed or ActivityType.OcrTaskFailed
                || item.Title.Contains("失败", StringComparison.Ordinal)
                || item.Description.Contains("失败", StringComparison.Ordinal)
                || item.Description.Contains("错误", StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    private async Task<IReadOnlyList<ActivityLog>> GetSnSyncFailureLogsAsync(CancellationToken cancellationToken = default)
    {
        var logs = await _activityLogService.GetRecentActivitiesAsync(300, cancellationToken);
        return logs
            .Where(item => item.Type == ActivityType.SyncFailed
                && item.Description.Contains($"{SnSyncEntityType}#", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    private async Task SaveSnConnectionResultAsync(DateTime when, string result)
    {
        Preferences.SnLastConnectionCheckAt = when.ToString("O");
        Preferences.SnLastConnectionResult = result;
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastConnectionCheckAt, Preferences.SnLastConnectionCheckAt);
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastConnectionResult, Preferences.SnLastConnectionResult);
    }

    private async Task SaveSnSyncResultAsync(DateTime when, string result)
    {
        Preferences.SnLastSyncAt = when.ToString("O");
        Preferences.SnLastSyncResult = result;
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastSyncAt, Preferences.SnLastSyncAt);
        await _settingRepository.UpsertAsync(AppSettingKeys.SnLastSyncResult, Preferences.SnLastSyncResult);
    }

    private string BuildDiagnosticSummary()
    {
        var diagnosticDatabasePath = BuildDiagnosticDatabasePath(DatabasePath);
        var diagnosticRuntimeEnvironmentText = string.IsNullOrWhiteSpace(DatabasePath)
            ? RuntimeEnvironmentText
            : RuntimeEnvironmentText.Replace(DatabasePath, diagnosticDatabasePath, StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            $"应用版本: {AppVersionText}",
            $"构建时间: {AppBuildTimeText}",
            $"数据库文件: {diagnosticDatabasePath}",
            $"数据库大小: {DatabaseSizeText}",
            $"数据库健康: {DatabaseHealthStatusText}",
            $"数据库详情: {DatabaseHealthDetailText}",
            $"SN Endpoint: {(IsStringNarrationGatewayEndpointConfigured ? "已配置" : "未配置")}",
            $"SN Token: {(IsStringNarrationGatewayTokenConfigured ? "已配置" : "未配置")}",
            $"SN 最近连接: {SnLastConnectionTimeText}",
            $"SN 连接结果: {SnLastConnectionResultText}",
            $"SN 最近同步: {SnLastSyncTimeText}",
            $"SN 同步结果: {SnLastSyncResultText}",
            $"本机访问保护: {LocalAccessProtectionStatusText}",
            $"运行时环境: {diagnosticRuntimeEnvironmentText.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}",
            $"当前账号: {CurrentAccountDisplayName}",
            $"当前角色: {(IsCurrentUserOwner ? "Owner" : "Member/Unknown")}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDiagnosticDatabasePath(string databasePath)
    {
        var fileName = Path.GetFileName(databasePath);
        return string.IsNullOrWhiteSpace(fileName) ? "<local-database>" : fileName;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:F1} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:F2} MB";
        }

        var gb = mb / 1024d;
        return $"{gb:F2} GB";
    }

    private static string NormalizeOption(string? value, IEnumerable<string> options, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        return options.Contains(normalized, StringComparer.Ordinal) ? normalized : fallback;
    }

    private static string ResolveBackupDirectory(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ? BuildDefaultBackupDirectory() : path.Trim();
        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception)
        {
            return BuildDefaultBackupDirectory();
        }
    }

    private static string BuildDefaultBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Orderly",
            "Backups");
    }

    private static string ResolveCloudEnvironmentId()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("TENCENTCLOUD_ENV_ID"),
            Environment.GetEnvironmentVariable("SN_CLOUD_ENV_ID"),
            Environment.GetEnvironmentVariable("ADMIN_PC_GATEWAY_ENV_ID")
        };

        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "未配置";
    }

    private static string TryFormatSavedTime(string rawValue, string fallback)
    {
        if (DateTimeOffset.TryParse(rawValue, out var parsed))
        {
            return parsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return fallback;
    }

    private static void OpenDirectory(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static string ResolveQaToolsPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "qa"));
    }

    private static string GetLogDirectoryPath()
    {
        var logDirectory = Path.Combine(DatabasePaths.GetAppRootPath(), "logs");
        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(logDirectory, "日志目录");
        return logDirectory;
    }
}
