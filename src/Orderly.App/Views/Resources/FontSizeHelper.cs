using System;
using System.Windows;

namespace Orderly.App.Helpers;

/// <summary>
/// 全局字号缩放管理器（防抖、全局热重载）
/// </summary>
public static class FontSizeHelper
{
    private static double _currentScale = 1.0;

    // 默认基准字号定义
    private const double BaseDisplay = 28;
    private const double BaseTitle = 20;
    private const double BaseSubtitle = 16;
    private const double BaseBody = 14;
    private const double BaseBodySm = 13;
    private const double BaseCaption = 12;

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

        // 计算新字号
        double fontDisplay = Math.Round(BaseDisplay * scaleFactor);
        double fontTitle = Math.Round(BaseTitle * scaleFactor);
        double fontSubtitle = Math.Round(BaseSubtitle * scaleFactor);
        
        // 满足可达性约束：正文字号 >= 13px，说明字号 >= 12px
        double fontBody = Math.Max(13, Math.Round(BaseBody * scaleFactor));
        double fontBodySm = Math.Max(13, Math.Round(BaseBodySm * scaleFactor));
        double fontCaption = Math.Max(12, Math.Round(BaseCaption * scaleFactor));

        var newDict = new ResourceDictionary();
        // 添加专门的元标识，供查找定位
        newDict.Add("FontSizeScaleFactor", scaleFactor);
        newDict.Add("UiFontDisplay", fontDisplay);
        newDict.Add("UiFontTitle", fontTitle);
        newDict.Add("UiFontSubtitle", fontSubtitle);
        newDict.Add("UiFontBody", fontBody);
        newDict.Add("UiFontBodySm", fontBodySm);
        newDict.Add("UiFontCaption", fontCaption);

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
