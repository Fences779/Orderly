using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Orderly.App.Helpers;

public static class ThemeHelper
{
    private static string _currentThemeMode = "浅色";

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Shutdown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            if (_currentThemeMode == "跟随系统")
            {
                ApplyTheme("跟随系统");
            }
        }
    }

    public static event EventHandler? ThemeChanged;

    public static void ApplyTheme(string themeMode)
    {
        _currentThemeMode = themeMode;
        var app = System.Windows.Application.Current;
        if (app == null) return;

        var targetThemePath = GetThemeSource(themeMode);

        // 递归寻找包含主题的字典及其父集合并替换
        TryReplaceTheme(app.Resources, targetThemePath);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool TryReplaceTheme(ResourceDictionary parent, string targetThemePath)
    {
        var mergedDicts = parent.MergedDictionaries;

        // 1. 检查当前层的合并字典
        var existingTheme = mergedDicts.FirstOrDefault(d => 
            d.Source != null && 
            (d.Source.OriginalString.IndexOf("/Themes/ThemeLight.xaml", StringComparison.OrdinalIgnoreCase) >= 0 || 
             d.Source.OriginalString.IndexOf("/Themes/ThemeDark.xaml", StringComparison.OrdinalIgnoreCase) >= 0));

        if (existingTheme != null)
        {
            // 如果已经加载且相同，则跳过
            if (existingTheme.Source != null && 
                existingTheme.Source.OriginalString.Equals(targetThemePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var newTheme = new ResourceDictionary
            {
                Source = new Uri(targetThemePath, UriKind.RelativeOrAbsolute)
            };

            int index = mergedDicts.IndexOf(existingTheme);
            mergedDicts.RemoveAt(index);
            mergedDicts.Insert(index, newTheme);
            return true;
        }

        // 2. 递归检查合并的子资源字典
        foreach (var dict in mergedDicts)
        {
            if (TryReplaceTheme(dict, targetThemePath))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetThemeSource(string themeMode)
    {
        if (themeMode == "深色")
        {
            return "pack://application:,,,/Orderly.App;component/Views/Resources/Themes/ThemeDark.xaml";
        }
        else if (themeMode == "浅色")
        {
            return "pack://application:,,,/Orderly.App;component/Views/Resources/Themes/ThemeLight.xaml";
        }
        else // 跟随系统
        {
            bool isDark = IsWindowsSystemDarkTheme();
            return isDark 
                ? "pack://application:,,,/Orderly.App;component/Views/Resources/Themes/ThemeDark.xaml" 
                : "pack://application:,,,/Orderly.App;component/Views/Resources/Themes/ThemeLight.xaml";
        }
    }

    public static bool IsWindowsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false; // 默认浅色
        }
    }
}
