using System;
using System.Windows;

namespace Orderly.App.Helpers;

/// <summary>
/// 全局字号缩放管理器（防抖、全局热重载）
/// </summary>
public static class FontSizeHelper
{
    private static double _currentScale = 1.0;

    private static readonly (string Key, double BaseSize, double MinimumSize)[] ScaledFontSizes =
    [
        ("UiFontDisplay", 28, 0),
        ("UiFontTitle", 20, 0),
        ("UiFontSubtitle", 16, 0),
        ("UiFontBody", 14, 13),
        ("UiFontBodySm", 13, 13),
        ("UiFontCaption", 12, 12),
        ("UiFontSize11", 11, 0),
        ("UiFontSize12", 12, 0),
        ("UiFontSize12_5", 12.5, 0),
        ("UiFontSize13", 13, 0),
        ("UiFontSize13_5", 13.5, 0),
        ("UiFontSize14", 14, 0),
        ("UiFontSize15", 15, 0),
        ("UiFontSize16", 16, 0),
        ("UiFontSize18", 18, 0),
        ("UiFontSize22", 22, 0),
        ("UiFontSize24", 24, 0),
        ("UiFontSize26", 26, 0),
        ("UiFontSize29", 29, 0),
        ("UiFontSize30", 30, 0),
    ];

    public static double CurrentScale => _currentScale;

    /// <summary>
    /// 解析偏好配置中的字号预设，并应用缩放
    /// </summary>
    /// <param name="preset">配置中的字符串值，可能为 "小"、"标准"、"大" 或数字字面量</param>
    public static void ApplyFontScale(string preset)
    {
        double scale = 1.0;
        if (string.Equals(preset, "小", StringComparison.Ordinal))
        {
            scale = 0.8;
        }
        else if (string.Equals(preset, "标准", StringComparison.Ordinal))
        {
            scale = 1.0;
        }
        else if (string.Equals(preset, "大", StringComparison.Ordinal))
        {
            scale = 1.2;
        }
        else if (double.TryParse(preset, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            scale = parsed;
        }
        ApplyFontScale(scale);
    }

    /// <summary>
    /// 实时按比例缩放全局字号阶
    /// </summary>
    /// <param name="scaleFactor">字号缩放比例</param>
    public static void ApplyFontScale(double scaleFactor)
    {
        scaleFactor = Math.Clamp(scaleFactor, 0.8, 1.3);
        _currentScale = scaleFactor;

        var app = System.Windows.Application.Current;
        if (app == null) return;

        var newDict = new ResourceDictionary();
        // 添加专门的元标识，供查找定位
        newDict.Add("FontSizeScaleFactor", scaleFactor);
        foreach (var (key, baseSize, minimumSize) in ScaledFontSizes)
        {
            var scaledSize = Math.Round(baseSize * scaleFactor, 2, MidpointRounding.AwayFromZero);
            newDict.Add(key, Math.Max(minimumSize, scaledSize));
        }

        var mergedDicts = app.Resources.MergedDictionaries;

        // 查找是否已有覆盖字号的资源字典
        ResourceDictionary? oldDict = null;
        foreach (var dict in mergedDicts)
        {
            if (dict.Contains("FontSizeScaleFactor"))
            {
                oldDict = dict;
                break;
            }
        }

        if (oldDict != null)
        {
            int index = mergedDicts.IndexOf(oldDict);
            mergedDicts.RemoveAt(index);
            mergedDicts.Insert(index, newDict);
        }
        else
        {
            // 插入到最后，确保能完全覆盖 Typography.xaml 中定义的原尺寸
            mergedDicts.Add(newDict);
        }
    }
}
