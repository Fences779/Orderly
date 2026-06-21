using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace Orderly.App.Helpers;

public static class ThemeHelper
{
    private static string _currentThemeMode = "浅色";
    private static string _currentAccentColor = "默认绿";

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
        ApplyAccentColor(_currentAccentColor);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyAccentColor(string accentColor)
    {
        _currentAccentColor = string.IsNullOrWhiteSpace(accentColor) ? "默认绿" : accentColor.Trim();
        var app = System.Windows.Application.Current;
        if (app == null) return;

        var baseColor = ResolveAccentColor(_currentAccentColor);
        SetBrush(app.Resources, "PrimaryBrush", baseColor);
        SetBrush(app.Resources, "PrimaryHoverBrush", Scale(baseColor, 0.88));
        SetBrush(app.Resources, "PrimaryPressedBrush", Scale(baseColor, 0.68));
        SetBrush(app.Resources, "PrimaryLightBrush", Mix(baseColor, MediaColors.White, 0.86));
        SetBrush(app.Resources, "AccentTextBrush", Scale(baseColor, 0.78));
        SetBrush(app.Resources, "AccentSoftBrush", Mix(baseColor, MediaColors.White, 0.88));

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

    private static MediaColor ResolveAccentColor(string value)
    {
        try
        {
            if (value.StartsWith('#') && (value.Length == 7 || value.Length == 9))
            {
                return (MediaColor)MediaColorConverter.ConvertFromString(value);
            }
        }
        catch
        {
            // fallback below
        }

        return value switch
        {
            "茶金" => MediaColor.FromRgb(154, 91, 0),
            "雾蓝" => MediaColor.FromRgb(26, 95, 180),
            _ => MediaColor.FromRgb(23, 107, 71)
        };
    }

    private static void SetBrush(ResourceDictionary resources, string key, MediaColor color)
    {
        resources[key] = new SolidColorBrush(color);
    }

    private static MediaColor Scale(MediaColor color, double factor)
    {
        return MediaColor.FromArgb(
            color.A,
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
    }

    private static MediaColor Mix(MediaColor color, MediaColor target, double targetWeight)
    {
        var sourceWeight = 1d - targetWeight;
        return MediaColor.FromArgb(
            color.A,
            (byte)Math.Clamp(color.R * sourceWeight + target.R * targetWeight, 0, 255),
            (byte)Math.Clamp(color.G * sourceWeight + target.G * targetWeight, 0, 255),
            (byte)Math.Clamp(color.B * sourceWeight + target.B * targetWeight, 0, 255));
    }
}
