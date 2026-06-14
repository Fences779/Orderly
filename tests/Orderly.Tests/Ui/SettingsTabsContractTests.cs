using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 设置页全子 Tab 硬编码扫描与 Binding 契约核验（spec: commerce-settings-ui-rebuild，任务 14.4）。
///
/// 对以下八个 <c>SettingsTab*.xaml</c>（均在 <c>src/Orderly.App/Views/Sections/</c>）做确定性静态解析与
/// 枚举型断言，不渲染、不实例化控件。设计文档 §Testing Strategy 已说明本特性属 WPF View / XAML 视觉
/// 重建 + 设计令牌（配置资源），不适用 PBT，故此处为作用于固定文件集合的 Theory 参数化静态检查：
///
///   SettingsTabAppearance / SettingsTabNotify / SettingsTabHotkeys /
///   SettingsTabData / SettingsTabDataAudit / SettingsTabDataSecurity /
///   SettingsTabAi / SettingsTabAiDiagnostics
///
/// 覆盖的交付门禁：
///   1. 硬编码扫描（需求 9.1、9.2、1.12）：
///      · 无 <c>#RGB / #ARGB / #RRGGBB / #AARRGGBB</c> 颜色字面量（颜色一律走主题语义画刷 DynamicResource）。
///      · 无裸 <c>FontSize</c> 数值字面量（含 <c>Setter Property="FontSize"</c> 写法）。
///      · 无裸 <c>CornerRadius</c> 数值字面量（含 <c>Setter Property="CornerRadius"</c> 写法）。
///      · 所有 <c>Margin</c>/<c>Padding</c> 引用具名令牌（含 <c>Setter Property="Margin|Padding"</c> 写法）。
///      注意：这些子 Tab 在 <c>UserControl.Resources</c> 内定义了派生 Thickness/Double 具名令牌
///      （如 <c>&lt;Thickness x:Key="..."&gt;0,24,0,24&lt;/Thickness&gt;</c>），其元素文本含数值是令牌定义
///      本身、合法；本测试只针对「属性值 / Setter Value」层面的字面量，XDocument 的属性遍历天然忽略
///      资源定义的元素文本。
///   2. Binding 契约核验（需求 10.4、10.5、10.6，从宽）：
///      · 这些 Tab 消费大量既有 <c>*Input</c> 属性 / 命令 / 选项集合，故不建立严格白名单，
///        仅断言绑定均为「常规属性路径」、无明显越界标记（命令路径须为简单 PascalCase 标识符）。
///      · 代码后置文件未引入新的 <c>RelayCommand</c>/<c>ICommand</c>/<c>DependencyProperty</c> 定义；
///        既有 <c>SettingsTextInput_LostFocus</c> 失焦自动保存钩子属既有逻辑、不算新增（不作为违例）。
/// </summary>
public sealed class SettingsTabsContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>八个被核验的设置子 Tab（文件名，不含路径）。</summary>
    public static IEnumerable<object[]> SettingsTabFileNames => new[]
    {
        new object[] { "SettingsTabAppearance.xaml" },
        new object[] { "SettingsTabNotify.xaml" },
        new object[] { "SettingsTabHotkeys.xaml" },
        new object[] { "SettingsTabData.xaml" },
        new object[] { "SettingsTabDataAudit.xaml" },
        new object[] { "SettingsTabDataSecurity.xaml" },
        new object[] { "SettingsTabAi.xaml" },
        new object[] { "SettingsTabAiDiagnostics.xaml" },
    };

    private static readonly string SectionsRelativeDir =
        Path.Combine("src", "Orderly.App", "Views", "Sections");

    // ==================== 1. 颜色字面量扫描（需求 9.1、1.12） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_contains_no_hex_color_literals(string fileName)
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

    // ==================== 2. 裸字号扫描（需求 9.1、1.12） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_has_no_bare_font_size_literals(string fileName)
    {
        var offenders = CollectVisualLiteralOffenders(fileName, "FontSize");

        Assert.True(offenders.Count == 0,
            $"{fileName} 存在裸 FontSize 数值字面量（应引用 Typography 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 3. 裸圆角扫描（需求 9.1、1.12） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_has_no_bare_corner_radius_literals(string fileName)
    {
        var offenders = CollectVisualLiteralOffenders(fileName, "CornerRadius");

        Assert.True(offenders.Count == 0,
            $"{fileName} 存在裸 CornerRadius 数值字面量（应引用 Shape 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 4. Margin / Padding 引用具名令牌（需求 9.1、1.12） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_margins_reference_named_tokens(string fileName)
        => AssertSpacingPropertyReferencesTokens(fileName, "Margin");

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_paddings_reference_named_tokens(string fileName)
        => AssertSpacingPropertyReferencesTokens(fileName, "Padding");

    private static void AssertSpacingPropertyReferencesTokens(string fileName, string propertyName)
    {
        var doc = LoadView(fileName);
        var offenders = new List<string>();

        // 直接属性写法：<Element Margin="..." /> / <Element Padding="..." />
        foreach (var element in doc.Descendants())
        {
            var attr = element.Attribute(propertyName);
            if (attr is not null && !IsResourceReference(attr.Value))
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
            if (value is not null && !IsResourceReference(value))
            {
                offenders.Add($"<Setter Property=\"{propertyName}\" Value=\"{value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{fileName} 的 {propertyName} 存在未引用具名令牌的字面量（应引用具名 Thickness 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 5. Binding 契约核验（从宽，需求 10.4） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_bindings_are_regular_property_paths(string fileName)
    {
        var paths = ExtractBindingPaths(LoadViewText(fileName));

        Assert.NotEmpty(paths); // 防御：解析失败导致空集会让断言虚假通过。

        // 常规属性路径：单段或点分标识符（如 Foo、Foo.Bar）。
        // 行级错误模板里 AdornedElement.(Validation.Errors)[0].ErrorContent 这类附加属性表达式
        // 属 WPF 校验既有写法，首段仍是常规标识符，故仅校验首段形态即可（从宽）。
        var regularSegment = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        var offenders = paths.Where(p => !regularSegment.IsMatch(FirstSegment(p))).OrderBy(p => p).ToList();

        Assert.True(offenders.Count == 0,
            $"{fileName} 出现非常规属性路径的可疑绑定：{string.Join(", ", offenders)}");
    }

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_command_bindings_are_simple_pascalcase_paths(string fileName)
    {
        var paths = ExtractBindingPaths(LoadViewText(fileName));
        var commandPaths = paths
            .Where(p => p.EndsWith("Command", StringComparison.Ordinal))
            .ToList();

        // 命令绑定须为简单 PascalCase 标识符（常规 VM 命令路径），无复合表达式 / 越界标记。
        var pascalCommand = new Regex(@"^[A-Z][A-Za-z0-9]*Command$");
        var offenders = commandPaths.Where(p => !pascalCommand.IsMatch(p)).OrderBy(p => p).ToList();

        Assert.True(offenders.Count == 0,
            $"{fileName} 命令绑定存在非常规路径（疑似越界 / 新增）：{string.Join(", ", offenders)}");
    }

    // ==================== 6. 代码后置无新增命令逻辑（需求 10.5、10.6） ====================

    [Theory]
    [MemberData(nameof(SettingsTabFileNames))]
    public void SettingsTab_code_behind_has_no_added_command_logic(string fileName)
    {
        var codeBehindPath = Path.Combine(
            ResolveRepositoryRoot(), SectionsRelativeDir, fileName + ".cs");
        Assert.True(File.Exists(codeBehindPath), $"未找到代码后置文件：{fileName}.cs");

        var source = File.ReadAllText(codeBehindPath);

        // 必须仍调用 InitializeComponent()。
        Assert.Contains("InitializeComponent();", source);

        // 不得引入新的「事件转命令 / 状态计算」定义信号。
        // 注意：既有 SettingsTextInput_LostFocus 失焦自动保存钩子属既有逻辑，不在禁止之列。
        string[] forbiddenSignals =
        {
            "RelayCommand",
            "ICommand",
            "DependencyProperty",
            "INotifyPropertyChanged",
        };

        var hits = forbiddenSignals.Where(s => source.Contains(s)).ToList();

        Assert.True(hits.Count == 0,
            $"{fileName}.cs 出现疑似新增命令 / 状态逻辑信号：{string.Join(", ", hits)}");
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

    /// <summary>取点分路径的首段（如 "Foo.Bar" → "Foo"）。</summary>
    private static string FirstSegment(string path)
    {
        var dot = path.IndexOf('.');
        return dot >= 0 ? path[..dot] : path;
    }

    /// <summary>
    /// 从 XAML 文本中提取所有 <c>{Binding ...}</c> 的绑定路径（首个位置参数或 <c>Path=</c> 段）。
    /// 采用花括号深度感知扫描，避免被嵌套 markup extension（如 Converter={StaticResource ...}）干扰。
    /// </summary>
    private static List<string> ExtractBindingPaths(string xaml)
    {
        var paths = new List<string>();

        int index = 0;
        while ((index = xaml.IndexOf("{Binding", index, StringComparison.Ordinal)) >= 0)
        {
            int contentStart = index + "{Binding".Length;
            int depth = 1;
            int i = contentStart;
            for (; i < xaml.Length && depth > 0; i++)
            {
                if (xaml[i] == '{')
                {
                    depth++;
                }
                else if (xaml[i] == '}')
                {
                    depth--;
                }
            }

            string inner = xaml.Substring(contentStart, (i - 1) - contentStart);
            string? path = ParseBindingPath(inner);
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path!);
            }

            index = i;
        }

        return paths;
    }

    /// <summary>
    /// 解析单个 Binding 内容串，返回其绑定路径：
    ///   · 含 <c>Path=</c> 时取其值；
    ///   · 否则取首个位置参数（第一个深度 0 逗号之前的片段）。
    /// 形如 <c>{Binding}</c>（无路径，回退到 DataContext）返回 null。
    /// </summary>
    private static string? ParseBindingPath(string inner)
    {
        var segments = SplitTopLevel(inner);
        if (segments.Count == 0)
        {
            return null;
        }

        foreach (var seg in segments)
        {
            var trimmed = seg.Trim();
            if (trimmed.StartsWith("Path=", StringComparison.Ordinal))
            {
                return trimmed.Substring("Path=".Length).Trim();
            }
        }

        var first = segments[0].Trim();
        if (first.Length == 0)
        {
            return null;
        }

        int eq = first.IndexOf('=');
        if (eq >= 0)
        {
            // 首段是命名参数（如 ElementName=...），无位置路径。
            return null;
        }

        return first;
    }

    /// <summary>按深度 0 的逗号切分（忽略嵌套 {} 内的逗号）。</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        int depth = 0;

        foreach (char c in text)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }

            if (c == ',' && depth == 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            result.Add(sb.ToString());
        }

        return result;
    }

    private static XDocument LoadView(string fileName)
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), SectionsRelativeDir, fileName);
        Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{fileName}");
        return XDocument.Load(fullPath);
    }

    private static string LoadViewText(string fileName)
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), SectionsRelativeDir, fileName);
        Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{fileName}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 静态检查测试（CashflowViewContractTests / CustomersViewContractTests 等）保持一致。
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
