using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Orderly.Data.Sqlite;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」命令（任务 13.3，设计 §8.4 / §8.4.3）。自 <c>MainViewModel.SettingsP0.Commands.cs</c> 迁入的等价实现：
/// 数据目录 / 数据库健康检查 / 缓存清理 / 过期日志清理 / 诊断复制 / 失败类日志导出 / 日志目录 / 重启 / QA 工具
/// / 备份目录选择 / 自定义强调色等命令由 <see cref="SettingsViewModel"/> 承载；命令依赖的活动日志、剪贴板、
/// 会话上下文服务均经构造注入获取，<b>不反向依赖 <see cref="MainViewModel"/></b>（设计 §8.4.3）。
///
/// <para><b>差异说明（设计 §8.4.3 / P5）</b>：原 <see cref="MainViewModel"/> 在失败路径会经
/// <c>ShowErrorMessage</c> 弹出 <c>MessageBox</c> 并写入壳层 <c>StatusMessage</c>；本等价实现遵循「ViewModel 不直接
/// 操作控件」原则，仅写入设置页独占的 <see cref="SettingsStatusMessage"/>（壳层 Toast/弹窗由集成任务 21.1 经
/// <c>IToastService</c> 统一接线）。</para>
///
/// <para><b>共存说明（设计 §8.4.3）</b>：当前阶段 <see cref="MainViewModel"/> 仍承载 <c>SettingsView</c> 绑定与自身的
/// 命令实现；本实现为 <see cref="SettingsViewModel"/> 建立的等价副本，待 DataContext 切换（任务 21.1）后接管。</para>
/// </summary>
public partial class SettingsViewModel
{
    // 串行化「保存类」命令的执行（替代原壳层 IsBusy 闸门），避免设置页内并发触发。
    private bool _isSettingsActionRunning;

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
        }
    }

    [RelayCommand]
    private async Task RunDatabaseHealthCheckAsync()
    {
        await ExecuteSettingsActionAsync(
            busyMessage: "正在检查数据库健康状态...",
            successMessage: "数据库健康检查已完成",
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
        await ExecuteSettingsActionAsync(
            busyMessage: "正在清理过期操作日志...",
            // 详细结果由 action 写入 SettingsStatusMessage，故此处不再以通用文案覆盖（设计 §8.4.3）。
            successMessage: null,
            errorStatusPrefix: "清理过期日志失败",
            action: async () =>
            {
                if (_activityLogService is null)
                {
                    SettingsStatusMessage = "活动日志服务未接入，无法清理过期日志。";
                    return;
                }

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
            _clipboardService?.SetText(diagnostics);
            SettingsStatusMessage = "诊断信息已复制（已脱敏，不包含 token/key 明文）。";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"复制诊断信息失败：{ex.Message}";
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
            CreatedAt = log.CreatedAt.ToString("O"),
            log.CustomerId,
            log.OrderId,
            log.DealId,
            SensitiveDetailsRedacted = true
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
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetLocalTestState))]
    private void ResetLocalTestState()
    {
        SettingsStatusMessage = "重置本地测试状态未接入。";
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

    /// <summary>
    /// 设置页内「保存类」命令的统一执行包装（自 <see cref="MainViewModel"/> 的 <c>ExecuteSaveActionAsync</c> 迁入的
    /// 等价简化版）：串行化执行、起始/成功/失败写入 <see cref="SettingsStatusMessage"/>。不耦合壳层 <c>IsBusy</c> /
    /// <c>StatusMessage</c>，不弹出控件级对话框（P5，设计 §8.4.3）。<paramref name="successMessage"/> 为 <c>null</c>
    /// 时保留 <paramref name="action"/> 内已写入的详细文案。
    /// </summary>
    private async Task ExecuteSettingsActionAsync(
        string busyMessage,
        string? successMessage,
        string errorStatusPrefix,
        Func<Task> action)
    {
        if (_isSettingsActionRunning)
        {
            return;
        }

        try
        {
            _isSettingsActionRunning = true;
            SettingsStatusMessage = busyMessage;
            await action();
            if (!string.IsNullOrEmpty(successMessage))
            {
                SettingsStatusMessage = successMessage;
            }
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"{errorStatusPrefix}：{ex.Message}";
        }
        finally
        {
            _isSettingsActionRunning = false;
        }
    }

    private static System.Windows.Window? GetDialogOwner()
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow;
        return mainWindow is { IsVisible: true } ? mainWindow : null;
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
}
