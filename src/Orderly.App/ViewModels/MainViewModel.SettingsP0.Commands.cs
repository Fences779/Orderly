using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Orderly.Data.Sqlite;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void OpenDataDirectory()
    {
        try
        {
            var directory = DataDirectoryPath;
            Directory.CreateDirectory(directory);
            OpenDirectory(directory);
            SettingsStatusMessage = $"已打开数据目录：{directory}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"打开数据目录失败：{ex.Message}";
            ShowErrorMessage("打开数据目录失败", ex);
        }
    }

    [RelayCommand]
    private async Task RunDatabaseHealthCheckAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在检查数据库健康状态...",
            successMessage: "数据库健康检查已完成",
            errorTitle: "数据库健康检查失败",
            errorStatusPrefix: "数据库健康检查失败",
            action: async () =>
            {
                var (status, detail) = await CheckDatabaseHealthAsync();
                DatabaseHealthStatusText = status;
                DatabaseHealthDetailText = detail;
            });
    }

    [RelayCommand]
    private void ClearCacheFiles()
    {
        try
        {
            var cachePath = Path.Combine(DatabasePaths.GetAppRootPath(), "cache");
            if (!Directory.Exists(cachePath))
            {
                SettingsStatusMessage = "未发现缓存目录，无需清理。";
                return;
            }
            if (HasReparsePoint(cachePath))
            {
                SettingsStatusMessage = "缓存目录为链接目录，已跳过清理。";
                return;
            }

            var removedFiles = 0;
            var removedDirectories = 0;
            foreach (var file in EnumerateCacheFiles(cachePath))
            {
                File.Delete(file);
                removedFiles++;
            }

            foreach (var directory in EnumerateCacheDirectories(cachePath).OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    removedDirectories++;
                }
            }

            SettingsStatusMessage = $"缓存清理完成：删除 {removedFiles} 个文件，清理 {removedDirectories} 个空目录。";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"清理缓存失败：{ex.Message}";
            ShowErrorMessage("清理缓存失败", ex);
        }
    }

    private static IEnumerable<string> EnumerateCacheFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", GetCacheEnumerationOptions()))
        {
            if (!HasReparsePoint(file))
            {
                yield return file;
            }
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", GetCacheEnumerationOptions()))
        {
            if (HasReparsePoint(subdirectory))
            {
                continue;
            }

            foreach (var file in EnumerateCacheFiles(subdirectory))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateCacheDirectories(string directory)
    {
        foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", GetCacheEnumerationOptions()))
        {
            if (HasReparsePoint(subdirectory))
            {
                continue;
            }

            yield return subdirectory;

            foreach (var child in EnumerateCacheDirectories(subdirectory))
            {
                yield return child;
            }
        }
    }

    private static EnumerationOptions GetCacheEnumerationOptions()
    {
        return new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false
        };
    }

    private static bool HasReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    [RelayCommand]
    private async Task ClearExpiredOperationLogsAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在清理过期操作日志...",
            successMessage: "过期操作日志清理完成",
            errorTitle: "清理过期日志失败",
            errorStatusPrefix: "清理过期日志失败",
            action: async () =>
            {
                var retentionDays = Math.Clamp(OperationLogRetentionDaysInput, 7, 3650);
                var deletedCount = await _activityLogService.CleanupExpiredActivitiesAsync(retentionDays);
                SettingsStatusMessage = $"已清理 {deletedCount} 条超过 {retentionDays} 天的操作日志。";
            });
    }

    [RelayCommand]
    private void CopyDiagnosticInfo()
    {
        try
        {
            var diagnostics = BuildDiagnosticSummary();
            _clipboardService.SetText(diagnostics);
            SettingsStatusMessage = "诊断信息已复制（已脱敏，不包含 token/key 明文）。";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"复制诊断信息失败：{ex.Message}";
            ShowErrorMessage("复制诊断信息失败", ex);
        }
    }

    [RelayCommand]
    private async Task ExportFailureLogsAsync()
    {
        var logs = await GetFailureActivityLogsAsync();
        if (logs.Count == 0)
        {
            SettingsStatusMessage = "暂无可导出的失败类日志。";
            return;
        }

        var logDirectory = GetLogDirectoryPath();
        var dialog = new SaveFileDialog
        {
            Title = "导出失败类操作日志",
            Filter = "JSON 文件|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
            InitialDirectory = logDirectory,
            FileName = $"orderly-failure-logs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        var payload = logs.Select(log => new
        {
            log.Id,
            Type = log.Type.ToString(),
            log.TypeLabel,
            log.Title,
            log.Description,
            log.Operator,
            CreatedAt = log.CreatedAt.ToString("O"),
            log.CustomerId,
            log.OrderId,
            log.DealId
        }).ToArray();

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await WriteJsonExportFileAtomicallyAsync(dialog.FileName, json);
        SettingsStatusMessage = $"失败类日志已导出：{dialog.FileName}";
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        try
        {
            var path = GetLogDirectoryPath();
            OpenDirectory(path);
            SettingsStatusMessage = $"已打开日志目录：{path}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"打开日志目录失败：{ex.Message}";
            ShowErrorMessage("打开日志目录失败", ex);
        }
    }

    [RelayCommand]
    private void RestartApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                throw new InvalidOperationException("未找到当前应用可执行文件路径。");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"重启应用失败：{ex.Message}";
            ShowErrorMessage("重启应用失败", ex);
        }
    }

    [RelayCommand]
    private void OpenQaToolsDirectory()
    {
        try
        {
            var qaPath = ResolveQaToolsPath();
            if (!Directory.Exists(qaPath))
            {
                throw new InvalidOperationException($"未检测到 QA 脚本目录：{qaPath}");
            }

            OpenDirectory(qaPath);
            QaToolsStatusText = $"已打开 QA 工具目录：{qaPath}";
        }
        catch (Exception ex)
        {
            QaToolsStatusText = $"QA 数据入口不可用：{ex.Message}";
            ShowErrorMessage("QA 数据入口不可用", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetLocalTestState))]
    private void ResetLocalTestState()
    {
        SettingsStatusMessage = "重置本地测试状态未接入。";
    }

    [RelayCommand]
    private async Task CheckSnConnectionAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在检查 SN 网关连接...",
            successMessage: "SN 网关检查完成",
            errorTitle: "SN 网关检查失败",
            errorStatusPrefix: "SN 网关检查失败",
            action: async () =>
            {
                var now = DateTime.Now;
                SnLastConnectionTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");

                if (!IsStringNarrationGatewayEndpointConfigured || !IsStringNarrationGatewayTokenConfigured)
                {
                    SnConnectionStatusText = "配置不完整（仅做配置检查，未发起真实连接）";
                    SnLastConnectionResultText = "配置不完整";
                    await SaveSnConnectionResultAsync(now, SnConnectionStatusText);
                    return;
                }

                await TestStringNarrationGatewayAsync();
                if (string.IsNullOrWhiteSpace(StringNarrationError))
                {
                    SnConnectionStatusText = "连接检查成功";
                    SnLastConnectionResultText = StringNarrationStatusMessage;
                }
                else
                {
                    SnConnectionStatusText = "连接检查失败";
                    SnLastConnectionResultText = StringNarrationError;
                }

                await SaveSnConnectionResultAsync(now, SnLastConnectionResultText);
            });
    }

    [RelayCommand]
    private async Task RunSnManualSyncAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在手动同步 SN 订单（只读拉取）...",
            successMessage: "SN 手动同步流程完成",
            errorTitle: "SN 手动同步失败",
            errorStatusPrefix: "SN 手动同步失败",
            action: async () =>
            {
                var now = DateTime.Now;
                await _syncService.MarkPendingAsync(
                    SnSyncEntityType,
                    SnSyncEntityId,
                    JsonSerializer.Serialize(new
                    {
                        mode = "manual",
                        at = now.ToString("O")
                    }));

                if (!IsStringNarrationGatewayEndpointConfigured || !IsStringNarrationGatewayTokenConfigured)
                {
                    var reason = "同步服务未接入：网关 endpoint/token 未配置。";
                    await _syncService.MarkFailedAsync(SnSyncEntityType, SnSyncEntityId, reason);
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = reason;
                    await SaveSnSyncResultAsync(now, reason);
                    await RefreshSnSyncStatusAsync();
                    return;
                }

                await LoadStringNarrationOrdersAsync();
                if (string.IsNullOrWhiteSpace(StringNarrationError))
                {
                    var message = $"同步完成：已拉取 {StringNarrationOrders.Count} 单。";
                    await _syncService.MarkSyncedAsync(
                        SnSyncEntityType,
                        SnSyncEntityId,
                        metadataJson: JsonSerializer.Serialize(new
                        {
                            mode = "manual",
                            result = message,
                            orders = StringNarrationOrders.Count,
                            at = now.ToString("O")
                        }));
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = message;
                    await SaveSnSyncResultAsync(now, message);
                }
                else
                {
                    await _syncService.MarkFailedAsync(
                        SnSyncEntityType,
                        SnSyncEntityId,
                        StringNarrationError,
                        JsonSerializer.Serialize(new
                        {
                            mode = "manual",
                            result = "failed",
                            error = StringNarrationError,
                            at = now.ToString("O")
                        }));
                    SnLastSyncTimeText = now.ToString("yyyy-MM-dd HH:mm:ss");
                    SnLastSyncResultText = $"同步失败：{StringNarrationError}";
                    await SaveSnSyncResultAsync(now, SnLastSyncResultText);
                }

                await RefreshSnSyncStatusAsync();
            });
    }

    [RelayCommand]
    private async Task RefreshSnSyncLogsAsync()
    {
        await RefreshSnSyncStatusAsync();
        SettingsStatusMessage = "SN 同步状态已刷新。";
    }

    [RelayCommand]
    private async Task ExportSnSyncLogsAsync()
    {
        var latest = await _syncRecordRepository.GetLatestByEntityTypeAsync(SnSyncEntityType);
        var failures = await GetSnSyncFailureLogsAsync();

        if (latest is null && failures.Count == 0)
        {
            SettingsStatusMessage = "暂无可导出的 SN 同步日志。";
            return;
        }

        var directory = GetLogDirectoryPath();
        var dialog = new SaveFileDialog
        {
            Title = "导出 SN 同步日志",
            Filter = "JSON 文件|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            OverwritePrompt = true,
            InitialDirectory = directory,
            FileName = $"sn-sync-logs-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        var payload = new
        {
            exportedAt = DateTimeOffset.Now.ToString("O"),
            latestSyncRecord = latest is null ? null : new
            {
                latest.Id,
                latest.EntityType,
                latest.EntityId,
                syncStatus = latest.SyncStatus.ToString(),
                lastSyncedAt = latest.LastSyncedAt?.ToString("O"),
                latest.ErrorMessage,
                latest.UpdatedAt,
                latest.CreatedAt
            },
            failedActivities = failures.Select(item => new
            {
                item.Id,
                type = item.Type.ToString(),
                item.Title,
                item.Description,
                item.Operator,
                createdAt = item.CreatedAt.ToString("O")
            })
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await WriteJsonExportFileAtomicallyAsync(dialog.FileName, json);

        SettingsStatusMessage = $"SN 同步日志已导出：{dialog.FileName}";
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择备份目录",
            SelectedPath = ResolveBackupDirectory(BackupDirectoryInput),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            BackupDirectoryInput = dialog.SelectedPath;
        }
    }

    private static async Task WriteJsonExportFileAtomicallyAsync(string outputPath, string json)
    {
        var fullPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("日志导出文件必须是 .json 文件。");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "日志导出目录");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(tempPath, "日志临时导出文件");
            await File.WriteAllTextAsync(tempPath, json);
            LocalDataFileSecurity.HardenFile(tempPath);

            LocalDataFileSecurity.EnsureFileIsNotLinked(fullPath, "日志导出文件");
            File.Move(tempPath, fullPath, overwrite: true);
            LocalDataFileSecurity.EnsureFileIsNotLinked(fullPath, "日志导出文件");
            LocalDataFileSecurity.HardenFile(fullPath);
        }
        catch
        {
            DeleteTemporaryExportFile(tempPath);
            throw;
        }
    }

    private static void DeleteTemporaryExportFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private void ChooseCustomAccentColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.FullOpen = true;
        if (!string.IsNullOrWhiteSpace(AccentColorInput) && AccentColorInput.StartsWith('#') && (AccentColorInput.Length == 7 || AccentColorInput.Length == 9))
        {
            try
            {
                dialog.Color = System.Drawing.ColorTranslator.FromHtml(AccentColorInput);
            }
            catch { }
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = dialog.Color;
            var hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            for (int i = AccentColorOptions.Count - 1; i >= 0; i--)
            {
                if (AccentColorOptions[i].StartsWith('#'))
                {
                    AccentColorOptions.RemoveAt(i);
                }
            }

            AccentColorOptions.Add(hexColor);
            AccentColorInput = hexColor;
        }
    }
}
