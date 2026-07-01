using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 设置页主框架（SettingsView）硬编码扫描与 Binding 契约核验（tasks.md 任务 13.2）。
///
/// 本测试对 commerce-settings-ui-rebuild 视觉重建产出的
/// <c>src/Orderly.App/Views/Sections/SettingsView.xaml</c> 做静态解析与枚举型断言，
/// 不渲染、不实例化控件，覆盖以下交付门禁（需求 9.1 / 9.2 / 10.4 / 10.5 / 10.6）：
///
///   1. 硬编码扫描（需求 9.1、1.12）：
///      - 无 <c>#RRGGBB</c> / <c>#AARRGGBB</c> 颜色字面量（颜色一律走主题语义画刷 DynamicResource）。
///      - 无裸 <c>FontSize</c> 数值字面量（字号一律引用 Typography 令牌）。
///      - 无裸 <c>CornerRadius</c> 数值字面量（圆角一律引用 Shape 令牌）。
///      - 元素与 Setter 上的 <c>Margin</c>/<c>Padding</c> 一律引用具名令牌（StaticResource/DynamicResource）；
///        唯一例外是「全零 Thickness」（<c>0</c> / <c>0,0,0,0</c>），其语义为「无间距/重置」而非间距量级，
///        不属于间距视觉字面量。
///   2. Binding 契约核验（需求 9.2、10.4）：
///      - 所有 <c>{Binding ...}</c> 路径均属于 SettingsViewModel / MainViewModel 设置分部
///        （及搜索结果行投影）的既有契约，无可疑新增/重命名路径。
///      - 命令绑定仅有既有的 <c>ActivateSearchResultCommand</c>，未引入新命令通道。
///   3. 代码后置无新增逻辑（需求 10.5、10.6）：
///      - <c>SettingsView.xaml.cs</c> 仅含 <c>InitializeComponent()</c>，无「事件转命令/状态计算」逻辑。
///
/// 结构性/图标字体豁免（design.md §Testing Strategy + 任务 13.2 说明）：
///   导航列宽 220、内容区 MaxWidth/MinWidth、Popup MaxHeight、图标列 Width（sys:Double 令牌）、
///   Segoe MDL2 Assets 图标字体的 FontFamily 字面量，均属结构性布局尺寸 / 图标字体，
///   不计入颜色/字号/圆角/间距视觉字面量；本测试仅扫描 FontSize/CornerRadius/Margin/Padding
///   与十六进制颜色，故天然不触及上述豁免项。
///
/// 设计文档 §Testing Strategy 已明确本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT；此处为作用于固定文件的静态/枚举型断言。
/// </summary>
public sealed class SettingsViewContractTests
{
    private static readonly string ViewRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "SettingsView.xaml");

    private static readonly string CodeBehindRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "SettingsView.xaml.cs");

    // ==================== Binding 契约白名单（完整点分路径，按出现原样收录） ====================
    //
    // 下列路径构成 SettingsView 的既有 Binding 契约（来自任务/设计文档与 ViewModel 源码核验）：
    //   * Settings                              —— 根 DataContext 下设置分部（MainViewModel.Settings）。
    //   * SettingsSearchQuery                   —— 搜索框文本（Settings 作用域）。
    //   * SelectedCategoryKey                   —— 当前分类 key（左导航选中态 + 右内容区切换，Settings 作用域）。
    //   * SearchResults / SearchResults.Count   —— 搜索结果集合及其计数（驱动浮层弹出与列表）。
    //   * IsSearchResultsTruncated              —— 搜索结果超限提示可见性。
    //   * DataContext.ActivateSearchResultCommand —— 命中跳转命令（经 ItemsControl 祖先回到 Settings 作用域）。
    //   * Title / CategoryKey                   —— 搜索结果行投影字段（命中项标题与所属分类）。
    //   * Settings.PendingScrollAnchorId        —— 锚点式滚动定位字段（右内容区附加行为，双向）。
    //   * Settings.SelectedCategoryKey          —— 右内容区按分类 key 切换可见性。
    //   * ActualWidth                           —— ElementName 绑定的控件属性（浮层宽度对齐搜索框），
    //                                              属控件自身结构属性，非 ViewModel 契约新增路径。
    //   * ActualHeight                          —— ElementName 绑定的控件属性（内层滚动区高度约束），
    //                                              属控件自身结构属性，非 ViewModel 契约新增路径。
    private static readonly HashSet<string> AllowedBindingPaths = new(StringComparer.Ordinal)
    {
        "Settings",
        "SettingsSearchQuery",
        "SelectedCategoryKey",
        "SearchResults",
        "SearchResults.Count",
        "IsSearchResultsTruncated",
        "DataContext.ActivateSearchResultCommand",
        "Title",
        "CategoryKey",
        "Settings.PendingScrollAnchorId",
        "Settings.SelectedCategoryKey",
        "ActualWidth",
        "ActualHeight",
        "(helpers:SettingsHelper.IsSelectingStartupSection)",
    };

    // ==================== 1. 颜色字面量扫描（需求 9.1、1.12） ====================

    [Fact]
    public void Settings_view_contains_no_hex_color_literals()
    {
        var doc = LoadView();
        var hexColor = new Regex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3,4})\b");

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
            $"SettingsView.xaml 存在硬编码颜色字面量（应改用主题语义画刷）：{string.Join("; ", offenders)}");
    }

    // ==================== 2. 裸字号扫描（需求 9.1、1.12） ====================

    [Fact]
    public void Settings_view_has_no_bare_font_size_literals()
    {
        var offenders = CollectVisualLiteralOffenders("FontSize");

        Assert.True(offenders.Count == 0,
            $"SettingsView.xaml 存在裸 FontSize 数值字面量（应引用 Typography 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 3. 裸圆角扫描（需求 9.1、1.12） ====================

    [Fact]
    public void Settings_view_has_no_bare_corner_radius_literals()
    {
        var offenders = CollectVisualLiteralOffenders("CornerRadius");

        Assert.True(offenders.Count == 0,
            $"SettingsView.xaml 存在裸 CornerRadius 数值字面量（应引用 Shape 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 4. Margin/Padding 引用具名令牌（需求 9.1、1.12） ====================

    [Theory]
    [InlineData("Margin")]
    [InlineData("Padding")]
    public void Settings_view_margins_and_paddings_reference_named_tokens(string propertyName)
    {
        var doc = LoadView();
        var offenders = new List<string>();

        // 直接属性写法：<Element Margin="..." />
        foreach (var element in doc.Descendants())
        {
            var attr = element.Attribute(propertyName);
            if (attr is not null && !IsResourceReference(attr.Value) && !IsAllZeroThickness(attr.Value))
            {
                offenders.Add($"<{element.Name.LocalName} {propertyName}=\"{attr.Value}\">");
            }
        }

        // Setter 写法：<Setter Property="Margin" Value="..." />
        foreach (var setter in doc.Descendants().Where(e => e.Name.LocalName == "Setter"))
        {
            if (setter.Attribute("Property")?.Value != propertyName)
            {
                continue;
            }

            var value = setter.Attribute("Value")?.Value;
            if (value is not null && !IsResourceReference(value) && !IsAllZeroThickness(value))
            {
                offenders.Add($"<Setter Property=\"{propertyName}\" Value=\"{value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"SettingsView.xaml 的 {propertyName} 存在未引用具名令牌的非零字面量：{string.Join("; ", offenders)}");
    }

    // ==================== 5. Binding 契约核验（需求 9.2、10.4） ====================

    [Fact]
    public void Settings_view_binding_paths_are_within_contract()
    {
        var paths = ExtractBindingPaths(LoadViewText());

        Assert.NotEmpty(paths); // 防御：解析失败导致空集会让断言虚假通过。

        var unexpected = paths.Where(p => !AllowedBindingPaths.Contains(p)).OrderBy(p => p).ToList();

        Assert.True(unexpected.Count == 0,
            $"SettingsView.xaml 出现契约外的可疑绑定路径（疑似新增/重命名）：{string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Settings_view_uses_only_expected_command_bindings()
    {
        var paths = ExtractBindingPaths(LoadViewText());

        // 取每条路径的叶段，筛出以 Command 结尾者（兼容 DataContext.ActivateSearchResultCommand 写法）。
        var commandLeaves = paths
            .Select(p => p.Split('.').Last())
            .Where(leaf => leaf.EndsWith("Command", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        // 既有契约仅暴露 ActivateSearchResultCommand（搜索命中跳转），不得引入新命令路径。
        Assert.True(commandLeaves.SetEquals(new[] { "ActivateSearchResultCommand" }),
            $"SettingsView.xaml 命令绑定应仅为 ActivateSearchResultCommand，实际：{string.Join(", ", commandLeaves)}");
    }

    // ==================== 6. 代码后置无新增逻辑（需求 10.5、10.6） ====================

    [Fact]
    public void Settings_view_code_behind_has_no_added_logic()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), CodeBehindRelativePath);
        Assert.True(File.Exists(fullPath), $"未找到代码后置文件：{CodeBehindRelativePath}");

        var source = File.ReadAllText(fullPath);

        // 代码后置仅应调用 InitializeComponent()，不得新增事件处理器或状态计算逻辑。
        Assert.Contains("InitializeComponent();", source);

        // 不应存在事件转命令/状态计算的迹象：事件处理器签名、私有成员、状态字段。
        Assert.DoesNotContain("EventArgs", source);
        Assert.DoesNotContain("private ", source);
        Assert.DoesNotContain("void On", source);
    }

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 收集指定视觉属性（FontSize/CornerRadius）中"裸数值字面量"违例（既非资源引用、又可解析为数值）。
    /// 同时覆盖直接属性写法与 Setter 写法。
    /// </summary>
    private static List<string> CollectVisualLiteralOffenders(string propertyName)
    {
        var doc = LoadView();
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
    /// 判定是否为「全零 Thickness」：<c>0</c> / <c>0,0,0,0</c> / <c>0 0 0 0</c> 等所有分量皆为 0。
    /// 零表示「无间距/重置」而非间距量级，不属于间距视觉字面量，故对 Margin/Padding 予以豁免。
    /// </summary>
    private static bool IsAllZeroThickness(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        return segments.All(s =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == 0d);
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

    /// <summary>
    /// 从 XAML 原文提取全部 <c>{Binding ...}</c> 的绑定路径（含点分多段，如 SearchResults.Count）。
    /// 采用花括号深度感知扫描 + 顶层逗号切分：
    ///   · 含 <c>Path=</c> 段时取其值；否则取首个位置参数；
    ///   · 首段形如 <c>标识符=...</c>（命名参数，如 <c>ElementName=...</c>/<c>RelativeSource=...</c>/<c>Source=...</c>）
    ///     视为「无位置路径」并跳过，避免把纯元素引用绑定的命名参数误判为 VM 绑定路径。
    /// 与 CustomersViewContractTests 的解析做法保持一致。
    /// </summary>
    private static HashSet<string> ExtractBindingPaths(string xamlText)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        int index = 0;
        while ((index = xamlText.IndexOf("{Binding", index, StringComparison.Ordinal)) >= 0)
        {
            int contentStart = index + "{Binding".Length;
            int depth = 1;
            int i = contentStart;
            for (; i < xamlText.Length && depth > 0; i++)
            {
                if (xamlText[i] == '{')
                {
                    depth++;
                }
                else if (xamlText[i] == '}')
                {
                    depth--;
                }
            }

            // inner = "{Binding" 与匹配 "}" 之间的内容。
            string inner = xamlText.Substring(contentStart, (i - 1) - contentStart);
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
    ///   · 否则取首个位置参数（第一个深度 0 逗号之前的片段）；
    ///   · 首段为命名参数（含 '='，如 <c>ElementName=...</c>）或 <c>{Binding}</c>（无路径）时返回 null。
    /// </summary>
    private static string? ParseBindingPath(string inner)
    {
        var segments = SplitTopLevel(inner);
        if (segments.Count == 0)
        {
            return null;
        }

        // 优先 Path= 段。
        foreach (var seg in segments)
        {
            var trimmed = seg.Trim();
            if (trimmed.StartsWith("Path=", StringComparison.Ordinal))
            {
                return trimmed.Substring("Path=".Length).Trim();
            }
        }

        // 否则首段若不是 name=value 形式，则视为位置参数路径。
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

    private static XDocument LoadView()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), ViewRelativePath);
        Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{ViewRelativePath}");
        return XDocument.Load(fullPath);
    }

    private static string LoadViewText()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), ViewRelativePath);
        Assert.True(File.Exists(fullPath), $"未找到被测 View 文件：{ViewRelativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 静态检查测试（CashflowViewContractTests / NonColorTokenTests）保持一致。
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
