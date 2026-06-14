using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 全局交付门禁：八页 + 设置子 Tab 硬编码扫描汇总测试（spec: commerce-settings-ui-rebuild，任务 15.1，需求 1.12）。
///
/// 本测试以 <see cref="Theory"/> 参数化覆盖全部 <b>16</b> 个被重建 XAML（均在
/// <c>src/Orderly.App/Views/Sections/</c>），对每个文件做确定性静态解析与枚举型断言，
/// 不渲染、不实例化 WPF 控件，与既有 <c>Ui/*ContractTests.cs</c> 的扫描方式保持一致：
///
///   ProductsView / OrdersView / InventoryView / CustomersView / CashflowView /
///   WorkbenchView / BusinessAdviceView / SettingsView /
///   SettingsTabAi / SettingsTabAiDiagnostics / SettingsTabAppearance /
///   SettingsTabData / SettingsTabDataAudit / SettingsTabDataSecurity /
///   SettingsTabHotkeys / SettingsTabNotify
///
/// 对每个文件断言：
///   1. 无 <c>#RGB / #ARGB / #RRGGBB / #AARRGGBB</c> 颜色字面量（颜色一律走主题语义画刷 DynamicResource）。
///   2. <c>FontSize</c> 无裸数值（含 <c>Setter Property="FontSize"</c> 写法），须引用 Typography 令牌。
///   3. <c>CornerRadius</c> 无裸数值（含 <c>Setter Property="CornerRadius"</c> 写法），须引用 Shape 令牌。
///   4. <c>Margin</c> / <c>Padding</c> 引用具名令牌（含 <c>Setter Property="Margin|Padding"</c> 写法）。
///
/// 豁免约定（与已有 Ui/*ContractTests.cs 一致，显式注释说明）：
///   · 全零 <c>Thickness</c>（<c>0</c> / <c>0,0,0,0</c>）豁免：表达「无外边距/无内边距」的语义零值，
///     不属需令牌化的视觉间距字面量（如 <c>SettingsView.xaml</c> 的 <c>&lt;Setter Property="Padding" Value="0"/&gt;</c>）。
///   · 结构性布局尺寸（<c>Width</c>/<c>Height</c>/<c>MaxWidth</c>/<c>MinWidth</c>/列宽 <c>Width="*"/"Auto"</c>）
///     与图标字体 <c>FontFamily</c>（<c>Segoe MDL2 Assets</c>）不属颜色/字号/圆角/间距视觉字面量，本测试不扫描这些属性。
///   · <c>UserControl.Resources</c> 内 <c>Thickness</c>/<c>Double</c> 令牌定义的元素文本数值是令牌定义本身、合法；
///     本测试只针对「属性值 / Setter Value」层面的字面量，<see cref="XDocument"/> 的属性遍历天然忽略资源定义的元素文本。
///
/// 设计文档 §Testing Strategy 已说明本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT，故此处为作用于固定文件集合的 Theory 参数化静态/枚举型检查。
/// </summary>
public sealed class RebuiltViewsHardcodeScanTests
{
    private static readonly string SectionsRelativeDir =
        Path.Combine("src", "Orderly.App", "Views", "Sections");

    /// <summary>全部 16 个被重建 View（文件名，不含路径）。</summary>
    public static IEnumerable<object[]> RebuiltViewFileNames => new[]
    {
        new object[] { "ProductsView.xaml" },
        new object[] { "OrdersView.xaml" },
        new object[] { "InventoryView.xaml" },
        new object[] { "CustomersView.xaml" },
        new object[] { "CashflowView.xaml" },
        new object[] { "WorkbenchView.xaml" },
        new object[] { "BusinessAdviceView.xaml" },
        new object[] { "SettingsView.xaml" },
        new object[] { "SettingsTabAi.xaml" },
        new object[] { "SettingsTabAiDiagnostics.xaml" },
        new object[] { "SettingsTabAppearance.xaml" },
        new object[] { "SettingsTabData.xaml" },
        new object[] { "SettingsTabDataAudit.xaml" },
        new object[] { "SettingsTabDataSecurity.xaml" },
        new object[] { "SettingsTabHotkeys.xaml" },
        new object[] { "SettingsTabNotify.xaml" },
    };

    // ==================== 0. 防御：被测文件清单与磁盘一致（16 个全覆盖） ====================

    [Fact]
    public void Scan_set_covers_all_sixteen_rebuilt_views()
    {
        Assert.Equal(16, RebuiltViewFileNames.Count());

        foreach (var row in RebuiltViewFileNames)
        {
            var fileName = (string)row[0];
            var fullPath = Path.Combine(ResolveRepositoryRoot(), SectionsRelativeDir, fileName);
            Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{fileName}");
        }
    }

    // ==================== 1. 颜色字面量扫描（需求 1.12） ====================

    [Theory]
    [MemberData(nameof(RebuiltViewFileNames))]
    public void View_contains_no_hex_color_literals(string fileName)
    {
        var doc = LoadView(fileName);
        var hexColor = new Regex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{3})\b");

        var offenders = new List<string>();
        foreach (var element in doc.Descendants())
        {
            foreach (var attr in element.Attributes())
            {
                if (hexColor.IsMatch(attr.Value))
                {
                    offenders.Add($"<{element.Name.LocalName} {attr.Name.LocalName}=\"{attr.Value}\">");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{fileName} 存在硬编码颜色字面量（应改用主题语义画刷 DynamicResource）：{string.Join("; ", offenders)}");
    }

    // ==================== 2. 裸字号扫描（需求 1.12） ====================

    [Theory]
    [MemberData(nameof(RebuiltViewFileNames))]
    public void View_has_no_bare_font_size_literals(string fileName)
    {
        var offenders = CollectVisualLiteralOffenders(fileName, "FontSize");

        Assert.True(offenders.Count == 0,
            $"{fileName} 存在裸 FontSize 数值字面量（应引用 Typography 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 3. 裸圆角扫描（需求 1.12） ====================

    [Theory]
    [MemberData(nameof(RebuiltViewFileNames))]
    public void View_has_no_bare_corner_radius_literals(string fileName)
    {
        var offenders = CollectVisualLiteralOffenders(fileName, "CornerRadius");

        Assert.True(offenders.Count == 0,
            $"{fileName} 存在裸 CornerRadius 数值字面量（应引用 Shape 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 4. Margin / Padding 引用具名令牌（需求 1.12） ====================

    [Theory]
    [MemberData(nameof(RebuiltViewFileNames))]
    public void View_margins_reference_named_tokens(string fileName)
        => AssertSpacingPropertyReferencesTokens(fileName, "Margin");

    [Theory]
    [MemberData(nameof(RebuiltViewFileNames))]
    public void View_paddings_reference_named_tokens(string fileName)
        => AssertSpacingPropertyReferencesTokens(fileName, "Padding");

    private static void AssertSpacingPropertyReferencesTokens(string fileName, string propertyName)
    {
        var doc = LoadView(fileName);
        var offenders = new List<string>();

        // 直接属性写法：<Element Margin="..." /> / <Element Padding="..." />
        foreach (var element in doc.Descendants())
        {
            var attr = element.Attribute(propertyName);
            if (attr is not null && !IsResourceReference(attr.Value) && !IsZeroThickness(attr.Value))
            {
                offenders.Add($"<{element.Name.LocalName} {propertyName}=\"{attr.Value}\">");
            }
        }

        // Setter 写法：<Setter Property="Margin|Padding" Value="..." />
        foreach (var setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            if (setter.Attribute("Property")?.Value != propertyName)
            {
                continue;
            }

            var value = setter.Attribute("Value")?.Value;
            if (value is not null && !IsResourceReference(value) && !IsZeroThickness(value))
            {
                offenders.Add($"<Setter Property=\"{propertyName}\" Value=\"{value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{fileName} 的 {propertyName} 存在未引用具名令牌、且非全零豁免的字面量：{string.Join("; ", offenders)}");
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 收集指定视觉属性（FontSize/CornerRadius）中「裸数值字面量」违例（既非资源引用、又可解析为数值）。
    /// 同时覆盖直接属性写法与 Setter 写法。
    /// </summary>
    private static List<string> CollectVisualLiteralOffenders(string fileName, string propertyName)
    {
        var doc = LoadView(fileName);
        var offenders = new List<string>();

        foreach (var element in doc.Descendants())
        {
            var attr = element.Attribute(propertyName);
            if (attr is not null && IsBareNumericLiteral(attr.Value))
            {
                offenders.Add($"<{element.Name.LocalName} {propertyName}=\"{attr.Value}\">");
            }
        }

        foreach (var setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            if (setter.Attribute("Property")?.Value != propertyName)
            {
                continue;
            }

            var value = setter.Attribute("Value")?.Value;
            if (value is not null && IsBareNumericLiteral(value))
            {
                offenders.Add($"<Setter Property=\"{propertyName}\" Value=\"{value}\">");
            }
        }

        return offenders;
    }

    /// <summary>资源引用值（<c>{StaticResource ...}</c> / <c>{DynamicResource ...}</c> / <c>{TemplateBinding ...}</c>）。</summary>
    private static bool IsResourceReference(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("{StaticResource", StringComparison.Ordinal)
            || trimmed.StartsWith("{DynamicResource", StringComparison.Ordinal)
            || trimmed.StartsWith("{TemplateBinding", StringComparison.Ordinal);
    }

    /// <summary>
    /// 全零 Thickness 豁免：表达「无外边距 / 无内边距」的语义零值，与既有 *ContractTests 约定一致。
    /// 允许写法：<c>0</c> / <c>0,0</c> / <c>0,0,0,0</c>（含空白）。
    /// </summary>
    private static bool IsZeroThickness(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        var components = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
        {
            return false;
        }

        return components.All(c =>
            double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == 0);
    }

    /// <summary>判定是否为裸数值字面量：非 markup-extension（不以 '{' 开头）且首段可解析为数值。</summary>
    private static bool IsBareNumericLiteral(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        // CornerRadius 可为 "4" 或 "4,4,4,4"；取首段尝试解析。
        var firstSegment = trimmed.Split(',', ' ')[0];
        return double.TryParse(firstSegment, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static XDocument LoadView(string fileName)
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), SectionsRelativeDir, fileName);
        Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{fileName}");
        return XDocument.Load(fullPath);
    }

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 静态检查测试（ProductsViewContractTests / SettingsTabsContractTests 等）保持一致。
    /// </summary>
    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orderly.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "无法通过向上查找 Orderly.sln 定位仓库根目录，起点：" + AppContext.BaseDirectory + "。");
    }
}
