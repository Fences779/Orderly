using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」状态文案与运行态摘要（任务 13.3，设计 §8.4 / §8.4.3）。自 <c>MainViewModel.SettingsP0.Status.cs</c>
/// 迁入的等价实现：<c>SettingsStatusMessage</c> 与数据库/安全/应用信息等运行态文案由 <see cref="SettingsViewModel"/>
/// 独占持有；数据库健康检查所需数据密钥、安全运行态与诊断身份改由构造注入的 <c>ISessionContextService</c> 获取，
/// <b>不反向依赖 <see cref="MainViewModel"/></b>（设计 §8.4.3，消除循环引用）。
///
/// <para><b>渐进迁移边界（设计 §8.4.4）</b>：本步迁入 P0 运行态刷新（数据库 / 安全 / 应用信息）。AI 助手与通知
/// 提醒的运行态刷新随任务 13.4 迁入，故 <see cref="RefreshSettingsRuntimeStatusAsync"/> 暂只协调已迁入的 P0 子集。</para>
///
/// <para><b>共存说明（设计 §8.4.3）</b>：当前阶段 <see cref="MainViewModel"/> 仍承载 <c>SettingsView</c> 绑定与自身的
/// 状态文案；本实现为 <see cref="SettingsViewModel"/> 建立的等价副本，待 DataContext 切换（任务 21.1）后接管。</para>
/// </summary>
public partial class SettingsViewModel
{
    /// <summary>当前数据库文件路径（构造注入）。用于数据目录解析、大小与健康检查。</summary>
    private readonly string _databasePath;

    // ── 状态文案（自 SettingsP0.cs / SettingsP0.Status.cs 迁入）──────────────────────────────

    [ObservableProperty]
    private string settingsStatusMessage = "设置未保存";

    [ObservableProperty]
    private string databaseSizeText = "未知";

    [ObservableProperty]
    private string databaseHealthStatusText = "未检查";

    [ObservableProperty]
    private string databaseHealthDetailText = "请点击“数据完整性检查”。";

    [ObservableProperty]
    private string databaseEncryptionStatusText = "未启用全库加密（文件层明文）；敏感字段通过会话密钥加密列保护。";

    [ObservableProperty]
    private string backupEncryptionStatusText = "本地备份为 JSON 文件，未加密。";

    [ObservableProperty]
    private string localAccessProtectionStatusText = "未登录";

    [ObservableProperty]
    private string appVersionText = "未知";

    [ObservableProperty]
    private string appBuildTimeText = "未记录";

    [ObservableProperty]
    private string runtimeEnvironmentText = "未加载";

    [ObservableProperty]
    private string updateCheckStatusText = "未接入更新服务";

    [ObservableProperty]
    private string exportCapabilityStatusText = "导出订单/客户/操作日志与历史导入待接入。";

    [ObservableProperty]
    private string qaToolsStatusText = "仅开发/QA 环境可用。";

    /// <summary>当前数据库文件路径（只读展示，自构造注入值解析）。</summary>
    public string DatabasePath => _databasePath;

    /// <summary>数据库所在目录（用于「打开数据目录」命令）。</summary>
    public string DataDirectoryPath
    {
        get
        {
            try
            {
                return Path.GetDirectoryName(_databasePath) ?? _databasePath;
            }
            catch (Exception)
            {
                return _databasePath;
            }
        }
    }

    /// <summary>「重置本地测试状态」命令是否可用（与原 <see cref="MainViewModel"/> 一致，恒为 <c>false</c>）。</summary>
    public bool CanResetLocalTestState => false;

    // ── 运行态刷新（自 SettingsP0.Status.cs 迁入，P0 子集）──────────────────────────────────

    /// <summary>
    /// 刷新设置页运行态摘要（自 SettingsP0.Status.cs 迁入的 P0 子集）。
    /// AI 助手 / 通知提醒的运行态刷新随任务 13.4 迁入后并入此协调点。
    /// </summary>
    public async Task RefreshSettingsRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await RefreshDatabaseRuntimeStatusAsync(cancellationToken);
        RefreshSecurityRuntimeStatus();
        RefreshAppInfoRuntimeStatus();
    }

    private async Task RefreshDatabaseRuntimeStatusAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var info = new FileInfo(_databasePath);
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
            $"数据库: {_databasePath}"
        };
        RuntimeEnvironmentText = string.Join(Environment.NewLine, lines);
    }

    private async Task<(string Status, string Detail)> CheckDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath))
        {
            return ("异常", "数据库文件不存在。");
        }

        var requiredTables = new[] { "AppSettings", "Customers", "Orders", "ActivityLogs", "SyncRecords" };
        var missingTables = new List<string>();
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
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

    private async Task<IReadOnlyList<ActivityLog>> GetFailureActivityLogsAsync(CancellationToken cancellationToken = default)
    {
        if (_activityLogService is null)
        {
            return Array.Empty<ActivityLog>();
        }

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

    private string BuildDiagnosticSummary()
    {
        var session = _sessionContextService?.Current;
        var currentAccountDisplayName = session?.DisplayName ?? string.Empty;
        var isCurrentUserOwner = session?.Role == LocalAccountRole.Owner;

        var diagnosticDatabasePath = BuildDiagnosticDatabasePath(_databasePath);
        var diagnosticRuntimeEnvironmentText = string.IsNullOrWhiteSpace(_databasePath)
            ? RuntimeEnvironmentText
            : RuntimeEnvironmentText.Replace(_databasePath, diagnosticDatabasePath, StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            $"应用版本: {AppVersionText}",
            $"构建时间: {AppBuildTimeText}",
            $"数据库文件: {diagnosticDatabasePath}",
            $"数据库大小: {DatabaseSizeText}",
            $"数据库健康: {DatabaseHealthStatusText}",
            $"数据库详情: {DatabaseHealthDetailText}",
            $"本机访问保护: {LocalAccessProtectionStatusText}",
            $"运行时环境: {diagnosticRuntimeEnvironmentText.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}",
            $"当前账号: {currentAccountDisplayName}",
            $"当前角色: {(isCurrentUserOwner ? "Owner" : "Member/Unknown")}"
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
