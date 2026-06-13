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
        var fontPreset = NormalizeOption(FontSizePresetInput, FontPresetOptions, "标准");
        var themeMode = NormalizeOption(ThemeModeInput, ThemeModeOptions, "浅色");
        var accentColor = NormalizeOption(AccentColorInput, AccentColorOptions, "默认绿");

        var preferences = new AppPreferences
        {
            MainHotkey = Preferences.MainHotkey,
            FloatingHotkey = Preferences.FloatingHotkey,
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
            ApplyP1InputsFromPreferences(preferences);
        }
        finally
        {
            _isApplyingSettingsInputs = false;
        }
    }
}
