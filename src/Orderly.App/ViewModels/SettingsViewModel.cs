using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」专属 ViewModel（设计 §8.4，BC-8 / BC-9）。从 <see cref="MainViewModel"/> 完整抽出设置相关状态与逻辑。
///
/// <para><b>本骨架（任务 13.1）范围</b>：仅迁入 <c>MainViewModel.SettingsP0.cs</c> 的 P0 偏好 <c>*Input</c> 状态
/// 与 <c>MainViewModel.SettingsP0.Mapping.cs</c> 的 <c>*Input</c>↔<see cref="AppPreferences"/> 规范化映射，
/// 作为这些状态的「新家」，保持整个解决方案可编译、可运行。</para>
///
/// <para><b>分步迁移边界（设计 §8.4.4）</b>：自动保存引擎（<c>ProcessQueuedSettingsAutoSaveAsync</c> /
/// <c>SaveP0SettingsAsync</c>）已由任务 13.2 迁入（见 <c>SettingsViewModel.AutoSave.cs</c>）；设置命令与状态文案
/// （备份/校验/导入恢复触发、<c>SettingsStatusMessage</c>）已由任务 13.3 迁入（见 <c>SettingsViewModel.Commands.cs</c> /
/// <c>SettingsViewModel.Status.cs</c>）；P1 / AI 诊断 / 快捷键与通知分部由任务 13.4 迁入；设置搜索状态与命中跳转
/// 由任务 13.5 迁入；离开页保存结果聚合与导航闸门由任务 13.6 迁入。</para>
///
/// <para><b>当前阶段</b>：<see cref="MainViewModel"/> 仍持有上述 P0 状态并承载现有 <c>SettingsView</c> 绑定与自动保存引擎，
/// 直至后续步骤完成搬迁并将 <c>SettingsView.DataContext</c> 改绑到 <c>{Binding Settings}</c>（集成任务 21.1）。本骨架不
/// 反向依赖 <see cref="MainViewModel"/> 实例（共享只读上下文按需经构造注入服务获取），消除循环引用。</para>
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingRepository? _settingRepository;
    private readonly ISettingsSearchIndex? _searchIndex;
    private readonly IToastService? _toast;

    // 命令与状态文案（任务 13.3）所需的现有服务，按构造注入获取，不反向依赖 MainViewModel（设计 §8.4.3）。
    private readonly IActivityLogService? _activityLogService;
    private readonly IClipboardService? _clipboardService;
    private readonly ISessionContextService? _sessionContextService;

    // 应用 *Input 时抑制由属性变更触发的自动保存（自动保存引擎在任务 13.2 迁入；此处保留标志位以保持映射语义一致）。
    private bool _isApplyingSettingsInputs;

    /// <summary>是否正处于「从偏好批量应用 <c>*Input</c>」的过程中；自动保存引擎（任务 13.2）据此抑制变更回写。</summary>
    internal bool IsApplyingSettingsInputs => _isApplyingSettingsInputs;

    /// <summary>
    /// 构造 <see cref="SettingsViewModel"/>（设计 §8.4.1）。
    /// </summary>
    /// <param name="settingRepository">应用设置仓储（偏好读写）。</param>
    /// <param name="searchIndex">设置搜索索引（命中过滤；搜索接线见任务 13.5）。</param>
    /// <param name="toast">壳层通用 Toast 服务（离开页结果提示见任务 13.6）。</param>
    /// <param name="activityLogService">活动日志服务（清理过期日志 / 导出失败类日志，任务 13.3）。</param>
    /// <param name="clipboardService">剪贴板服务（复制脱敏诊断信息，任务 13.3）。</param>
    /// <param name="sessionContextService">会话上下文服务（数据库健康检查数据密钥 / 安全运行态 / 诊断身份，任务 13.3）。</param>
    /// <param name="databasePath">当前数据库文件路径（数据目录 / 大小 / 健康检查，任务 13.3）。</param>
    public SettingsViewModel(
        IAppSettingRepository? settingRepository = null,
        ISettingsSearchIndex? searchIndex = null,
        IToastService? toast = null,
        IActivityLogService? activityLogService = null,
        IClipboardService? clipboardService = null,
        ISessionContextService? sessionContextService = null,
        string? databasePath = null)
    {
        _settingRepository = settingRepository;
        _searchIndex = searchIndex;
        _toast = toast;
        _activityLogService = activityLogService;
        _clipboardService = clipboardService;
        _sessionContextService = sessionContextService;
        _databasePath = databasePath ?? string.Empty;
    }

    // ── 选项集合（自 SettingsP0.cs 迁入；标签为中文，英文注释仅供开发参考） ──

    public ObservableCollection<string> StartupSectionOptions { get; } = new([
        MainViewModel.SectionWorkbench,
        MainViewModel.SectionOrders,
        MainViewModel.SectionProducts,
        MainViewModel.SectionInventory,
        MainViewModel.SectionCustomers,
        MainViewModel.SectionCashflow,
        MainViewModel.SectionBusinessAdvice,
        MainViewModel.SectionSettings,
        MainViewModel.SectionMe]);

    public ObservableCollection<string> WindowModeOptions { get; } = new(["普通", "最大化"]);
    public ObservableCollection<string> FontPresetOptions { get; } = new(["小", "标准", "大"]);
    public ObservableCollection<string> ThemeModeOptions { get; } = new(["浅色", "深色", "跟随系统"]);
    public ObservableCollection<string> AccentColorOptions { get; } = new(["默认绿", "茶金", "雾蓝"]);
    public ObservableCollection<string> AutoBackupFrequencyOptions { get; } = new(["手动", "每日", "每周"]);

    // ── P0 偏好 *Input 暂存状态（外观与启动 / 数据与备份 / 安全与日志，按六大分类成组，§8.4.2） ──

    // 外观与启动
    [ObservableProperty]
    private string startupDefaultSectionInput = MainViewModel.SectionWorkbench;

    [ObservableProperty]
    private bool rememberLastSectionInput;

    [ObservableProperty]
    private string lastSectionInput = MainViewModel.SectionWorkbench;

    [ObservableProperty]
    private bool startWithWindowsInput;

    [ObservableProperty]
    private bool showFloatingWindowOnStartupInput;

    [ObservableProperty]
    private bool startMinimizedToTrayInput;

    [ObservableProperty]
    private bool rememberWindowBoundsInput;

    [ObservableProperty]
    private string defaultWindowModeInput = "普通";

    [ObservableProperty]
    private bool sidebarDefaultExpandedInput = true;

    [ObservableProperty]
    private string fontSizePresetInput = "标准";

    [ObservableProperty]
    private bool showWindowsScaleHintInput = true;

    [ObservableProperty]
    private string themeModeInput = "浅色";

    [ObservableProperty]
    private string accentColorInput = "默认绿";

    [ObservableProperty]
    private bool enableLightAnimationInput;

    // 数据与备份
    [ObservableProperty]
    private string backupDirectoryInput = BuildDefaultBackupDirectory();

    [ObservableProperty]
    private bool autoBackupEnabledInput;

    [ObservableProperty]
    private string autoBackupFrequencyInput = "手动";

    [ObservableProperty]
    private int backupRetentionCountInput = 10;

    // 安全与日志
    [ObservableProperty]
    private bool maskPhoneByDefaultInput = true;

    [ObservableProperty]
    private bool maskAddressByDefaultInput = true;

    [ObservableProperty]
    private bool includeSensitiveInExportInput;

    [ObservableProperty]
    private bool maskOrderSummaryOnCopyInput = true;

    [ObservableProperty]
    private bool operationLogEnabledInput = true;

    [ObservableProperty]
    private int operationLogRetentionDaysInput = 180;

    [ObservableProperty]
    private bool debugModeEnabledInput;

    // ── P0 派生只读状态 ──

    /// <summary>开机自启在当前环境是否不可用（与 <see cref="StartWithWindowsInput"/> 关联）。</summary>
    public bool IsStartWithWindowsUnavailable => StartWithWindowsInput;

    /// <summary>规范化后的有效备份目录（用于 UI 展示）。</summary>
    public string EffectiveBackupDirectory => ResolveBackupDirectory(BackupDirectoryInput);

    /// <summary>当前主题文案标签。</summary>
    public string CurrentThemeLabel => ThemeModeInput switch
    {
        "浅色" => "浅色模式",
        "深色" => "深色模式",
        _ => "跟随系统"
    };

    /// <summary>当前主题图标字形。</summary>
    public string CurrentThemeIcon => ThemeModeInput switch
    {
        "浅色" => "\xE706",
        "深色" => "\xE708",
        _ => "\xE7F4"
    };

    partial void OnStartWithWindowsInputChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStartWithWindowsUnavailable));
    }

    partial void OnBackupDirectoryInputChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveBackupDirectory));
    }

    partial void OnThemeModeInputChanged(string value)
    {
        Orderly.App.Helpers.ThemeHelper.ApplyTheme(value);
        OnPropertyChanged(nameof(CurrentThemeLabel));
        OnPropertyChanged(nameof(CurrentThemeIcon));
    }

    // ── *Input ↔ AppPreferences 规范化映射（自 SettingsP0.Mapping.cs 迁入，P0 部分） ──

    /// <summary>
    /// 由当前 <c>*Input</c> 暂存值构建规范化后的 <see cref="AppPreferences"/>（自 SettingsP0.Mapping.cs 迁入；
    /// P1 映射于任务 13.4 接管）。
    ///
    /// 映射范围（设计 §8.4.4）：P0 字段由本方法直接规范化覆盖；快捷键 / AI 助手 / 通知提醒等 P1 字段于任务 13.4
    /// 起改由 <see cref="ApplyP1InputsToPreferences"/> 以当前 P1 <c>*Input</c> 覆盖，不再依赖
    /// <paramref name="basePreferences"/> 保留这些字段。<paramref name="basePreferences"/> 现仅作为头像引用
    /// （<see cref="AppPreferences.AvatarReference"/>，由我的页任务 14.4 接管）等尚未迁移字段的基线来源。
    /// </summary>
    /// <param name="basePreferences">当前生效的偏好，作为尚未迁移字段（如头像引用）的基线来源。</param>
    public AppPreferences BuildPreferencesFromInputs(AppPreferences basePreferences)
    {
        ArgumentNullException.ThrowIfNull(basePreferences);

        var startupDefaultSection = NormalizeOption(StartupDefaultSectionInput, StartupSectionOptions, MainViewModel.SectionWorkbench);
        var lastSection = NormalizeOption(LastSectionInput, StartupSectionOptions, startupDefaultSection);
        var backupRetention = Math.Clamp(BackupRetentionCountInput, 1, 100);
        var retentionDays = Math.Clamp(OperationLogRetentionDaysInput, 7, 3650);
        var autoBackupFrequency = NormalizeOption(AutoBackupFrequencyInput, AutoBackupFrequencyOptions, "手动");
        var windowMode = NormalizeOption(DefaultWindowModeInput, WindowModeOptions, "普通");
        var fontPreset = NormalizeOption(FontSizePresetInput, FontPresetOptions, "标准");
        var themeMode = NormalizeOption(ThemeModeInput, ThemeModeOptions, "浅色");
        var accentColor = NormalizeOption(AccentColorInput, AccentColorOptions, "默认绿");

        var preferences = new AppPreferences
        {
            // ── 尚未迁移字段：原样保留基线值（头像引用由我的页任务 14.4 接管）──────────
            AvatarReference = basePreferences.AvatarReference,

            // ── P0 字段：由当前 *Input 规范化覆盖（任务 13.1）───────────────────────
            ShowFloatingWindowOnStartup = ShowFloatingWindowOnStartupInput,
            StartMinimizedToTray = StartMinimizedToTrayInput,
            StartupDefaultSection = startupDefaultSection,
            RememberLastSection = RememberLastSectionInput,
            LastSection = lastSection,
            StartWithWindows = StartWithWindowsInput,
            RememberWindowBounds = RememberWindowBoundsInput,
            DefaultWindowMode = windowMode,
            SidebarDefaultExpanded = SidebarDefaultExpandedInput,
            FontSizePreset = fontPreset,
            ShowWindowsScaleHint = ShowWindowsScaleHintInput,
            ThemeMode = themeMode,
            AccentColor = accentColor,
            EnableLightAnimation = EnableLightAnimationInput,
            BackupDirectory = ResolveBackupDirectory(BackupDirectoryInput),
            AutoBackupEnabled = AutoBackupEnabledInput,
            AutoBackupFrequency = autoBackupFrequency,
            BackupRetentionCount = backupRetention,
            MaskPhoneByDefault = MaskPhoneByDefaultInput,
            MaskAddressByDefault = MaskAddressByDefaultInput,
            IncludeSensitiveInExport = IncludeSensitiveInExportInput,
            MaskOrderSummaryOnCopy = MaskOrderSummaryOnCopyInput,
            OperationLogEnabled = OperationLogEnabledInput,
            OperationLogRetentionDays = retentionDays,
            DebugModeEnabled = DebugModeEnabledInput
        };

        // ── P1 字段：由当前快捷键 / AI / 通知 *Input 规范化覆盖（任务 13.4 接管，完成完整映射）──
        return ApplyP1InputsToPreferences(preferences);
    }

    /// <summary>
    /// 将 <see cref="AppPreferences"/> 中的字段回填到 <c>*Input</c> 暂存属性（自 SettingsP0.Mapping.cs 迁入；
    /// P1 回填于任务 13.4 接管）。
    ///
    /// 应用期间置位 <see cref="_isApplyingSettingsInputs"/>，以便（任务 13.2 迁入的）自动保存引擎
    /// 不因回填触发保存。P0 字段由本方法回填，快捷键 / AI / 通知等 P1 字段经 <see cref="ApplyP1InputsFromPreferences"/>
    /// 回填。
    /// </summary>
    public void ApplySettingsInputsFromPreferences(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        // 刷新尚未迁移字段（头像引用等）的基线来源，供自动保存引擎（任务 13.2）在规范化时原样保留。
        _baselinePreferences = preferences;

        _isApplyingSettingsInputs = true;
        try
        {
            StartupDefaultSectionInput = NormalizeOption(preferences.StartupDefaultSection, StartupSectionOptions, MainViewModel.SectionWorkbench);
            RememberLastSectionInput = preferences.RememberLastSection;
            LastSectionInput = NormalizeOption(preferences.LastSection, StartupSectionOptions, StartupDefaultSectionInput);
            StartWithWindowsInput = preferences.StartWithWindows;
            ShowFloatingWindowOnStartupInput = preferences.ShowFloatingWindowOnStartup;
            StartMinimizedToTrayInput = preferences.StartMinimizedToTray;
            RememberWindowBoundsInput = preferences.RememberWindowBounds;
            DefaultWindowModeInput = NormalizeOption(preferences.DefaultWindowMode, WindowModeOptions, "普通");
            SidebarDefaultExpandedInput = preferences.SidebarDefaultExpanded;
            FontSizePresetInput = NormalizeOption(preferences.FontSizePreset, FontPresetOptions, "标准");
            ShowWindowsScaleHintInput = preferences.ShowWindowsScaleHint;
            ThemeModeInput = NormalizeOption(preferences.ThemeMode, ThemeModeOptions, "浅色");

            var loadedColor = preferences.AccentColor;
            if (!string.IsNullOrWhiteSpace(loadedColor) && loadedColor.StartsWith('#'))
            {
                for (int i = AccentColorOptions.Count - 1; i >= 0; i--)
                {
                    if (AccentColorOptions[i].StartsWith('#'))
                    {
                        AccentColorOptions.RemoveAt(i);
                    }
                }
                AccentColorOptions.Add(loadedColor);
            }
            AccentColorInput = NormalizeOption(loadedColor, AccentColorOptions, "默认绿");

            EnableLightAnimationInput = preferences.EnableLightAnimation;

            BackupDirectoryInput = ResolveBackupDirectory(preferences.BackupDirectory);
            AutoBackupEnabledInput = preferences.AutoBackupEnabled;
            AutoBackupFrequencyInput = NormalizeOption(preferences.AutoBackupFrequency, AutoBackupFrequencyOptions, "手动");
            BackupRetentionCountInput = Math.Clamp(preferences.BackupRetentionCount, 1, 100);

            MaskPhoneByDefaultInput = preferences.MaskPhoneByDefault;
            MaskAddressByDefaultInput = preferences.MaskAddressByDefault;
            IncludeSensitiveInExportInput = preferences.IncludeSensitiveInExport;
            MaskOrderSummaryOnCopyInput = preferences.MaskOrderSummaryOnCopy;
            OperationLogEnabledInput = preferences.OperationLogEnabled;
            OperationLogRetentionDaysInput = Math.Clamp(preferences.OperationLogRetentionDays, 7, 3650);
            DebugModeEnabledInput = preferences.DebugModeEnabled;

            // P1 字段（快捷键 / AI / 通知）回填（任务 13.4 接管）。
            ApplyP1InputsFromPreferences(preferences);
        }
        finally
        {
            _isApplyingSettingsInputs = false;
        }
    }

    // ── 规范化辅助（自 SettingsP0.Status.cs 迁入，映射所需的纯函数副本）──────────────────
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
}
