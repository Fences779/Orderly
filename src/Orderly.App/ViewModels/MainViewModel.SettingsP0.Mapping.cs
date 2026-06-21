using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private AppPreferences BuildPreferencesFromInputs()
    {
        var startupDefaultSection = NormalizeOption(StartupDefaultSectionInput, StartupSectionOptions, SectionWorkbench);
        var lastSection = NormalizeOption(LastSectionInput, StartupSectionOptions, startupDefaultSection);
        var backupRetention = Math.Clamp(BackupRetentionCountInput, 1, 100);
        var retentionDays = Math.Clamp(OperationLogRetentionDaysInput, 7, 3650);
        var autoBackupFrequency = NormalizeOption(AutoBackupFrequencyInput, AutoBackupFrequencyOptions, "手动");
        var windowMode = NormalizeOption(DefaultWindowModeInput, WindowModeOptions, "普通");
        var fontPreset = FontSizePresetInput.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var themeMode = NormalizeOption(ThemeModeInput, ThemeModeOptions, "浅色");
        var accentColor = NormalizeOption(AccentColorInput, AccentColorOptions, "默认绿");

        var preferences = new AppPreferences
        {
            MainHotkey = Preferences.MainHotkey,
            FloatingHotkey = Preferences.FloatingHotkey,
            ShowFloatingWindowOnStartup = ShowFloatingWindowOnStartupInput,
            FloatingBallLeft = Preferences.FloatingBallLeft,
            FloatingBallTop = Preferences.FloatingBallTop,
            FloatingBallOpacity = Math.Clamp(FloatingBallOpacityInput, 0.35, 1.0),
            StartMinimizedToTray = StartMinimizedToTrayInput,
            StartupDefaultSection = startupDefaultSection,
            RememberLastSection = RememberLastSectionInput,
            LastSection = lastSection,
            StartWithWindows = StartWithWindowsInput,
            RememberWindowBounds = RememberWindowBoundsInput,
            WindowLeft = Preferences.WindowLeft,
            WindowTop = Preferences.WindowTop,
            WindowWidth = Preferences.WindowWidth,
            WindowHeight = Preferences.WindowHeight,
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
            LastAutoBackupAt = Preferences.LastAutoBackupAt,
            MaskPhoneByDefault = MaskPhoneByDefaultInput,
            MaskAddressByDefault = MaskAddressByDefaultInput,
            IncludeSensitiveInExport = IncludeSensitiveInExportInput,
            MaskOrderSummaryOnCopy = MaskOrderSummaryOnCopyInput,
            OperationLogEnabled = OperationLogEnabledInput,
            OperationLogRetentionDays = retentionDays,
            DebugModeEnabled = DebugModeEnabledInput
        };

        return ApplyP1InputsToPreferences(preferences);
    }

    private void ApplySettingsInputsFromPreferences(AppPreferences preferences)
    {
        _isApplyingSettingsInputs = true;
        try
        {
            StartupDefaultSectionInput = NormalizeOption(preferences.StartupDefaultSection, StartupSectionOptions, SectionWorkbench);
            RememberLastSectionInput = preferences.RememberLastSection;
            LastSectionInput = NormalizeOption(preferences.LastSection, StartupSectionOptions, StartupDefaultSectionInput);
            StartWithWindowsInput = preferences.StartWithWindows;
            ShowFloatingWindowOnStartupInput = preferences.ShowFloatingWindowOnStartup;
            FloatingBallOpacityInput = Math.Clamp(preferences.FloatingBallOpacity, 0.35, 1.0);
            StartMinimizedToTrayInput = preferences.StartMinimizedToTray;
            RememberWindowBoundsInput = preferences.RememberWindowBounds;
            DefaultWindowModeInput = NormalizeOption(preferences.DefaultWindowMode, WindowModeOptions, "普通");
            SidebarDefaultExpandedInput = preferences.SidebarDefaultExpanded;
            var fontPresetStr = preferences.FontSizePreset;
            if (string.Equals(fontPresetStr, "小", StringComparison.Ordinal)) FontSizePresetInput = 0.8;
            else if (string.Equals(fontPresetStr, "标准", StringComparison.Ordinal)) FontSizePresetInput = 1.0;
            else if (string.Equals(fontPresetStr, "大", StringComparison.Ordinal)) FontSizePresetInput = 1.2;
            else if (double.TryParse(fontPresetStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedVal))
            {
                FontSizePresetInput = Math.Clamp(parsedVal, 0.8, 1.3);
            }
            else
            {
                FontSizePresetInput = 1.0;
            }
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
            ApplyP1InputsFromPreferences(preferences);
        }
        finally
        {
            _isApplyingSettingsInputs = false;
        }
    }
}
