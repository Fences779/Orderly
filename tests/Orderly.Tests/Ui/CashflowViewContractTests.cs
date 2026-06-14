using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 现金流页（CashflowView）硬编码扫描与 Binding 契约核验（tasks.md 任务 9.2）。
///
/// 本测试对 commerce-settings-ui-rebuild 视觉重建产出的
/// <c>src/Orderly.App/Views/Sections/CashflowView.xaml</c> 做静态解析与枚举型断言，
/// 不渲染、不实例化控件，覆盖以下交付门禁：
///
///   1. 硬编码扫描（需求 1.12、6.1）：
///      - 无 <c>#RRGGBB</c> / <c>#AARRGGBB</c> 颜色字面量（颜色一律走主题语义画刷 DynamicResource）。
///      - 无裸 <c>FontSize</c> 数值字面量（字号一律引用 Typography 令牌）。
///      - 无裸 <c>CornerRadius</c> 数值字面量（圆角一律引用 Shape 令牌）。
///      - 元素与 Setter 上的 <c>Margin</c>/<c>Padding</c> 一律引用具名令牌（StaticResource/DynamicResource）。
///   2. Binding 契约核验（需求 6.2、10.4、10.5、10.6）：
///      - 所有 <c>{Binding ...}</c> 路径均属于 CashflowPageViewModel（及其基类 CommercePageViewModel、
///        行投影 CashFlowRow）既有契约，无可疑新增/重命名路径。
///      - 代码后置文件仅含 <c>InitializeComponent()</c>，无"事件转命令/状态计算"新增逻辑。
///
/// 设计文档 §Testing Strategy 已明确本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT；此处为作用于固定文件的静态/枚举型断言。
/// </summary>
public sealed class CashflowViewContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string ViewRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "CashflowView.xaml");

    private static readonly string CodeBehindRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "CashflowView.xaml.cs");

    // ==================== Binding 契约白名单 ====================
    //
    // 仅以下绑定路径属于 CashflowView 既有契约（来自任务/设计文档与 ViewModel 源码核验）：
    //   * 根 DataContext：CashflowPage（MainViewModel 暴露的页面 VM）。
    //   * 状态四标志 + 错误/重试：ShowLoading / HasError / IsEmpty / ShowContent / ErrorMessage / RefreshCommand。
    //   * 概览数字：RealizedIncome / RealizedExpense / NetCashFlow / OutstandingReceivable /
    //               OutstandingPayable / HealthScore。
    //   * 明细集合：Entries。
    //   * 行字段（CashFlowRow）：OccurredAt / Direction / Amount / SettlementStatus / CategoryName。
    private static readonly HashSet<string> AllowedBindingPaths = new(StringComparer.Ordinal)
    {
        "CashflowPage",
        "ShowLoading",
        "HasError",
        "IsEmpty",
        "ShowContent",
        "ErrorMessage",
        "RefreshCommand",
        "RealizedIncome",
        "RealizedExpense",
        "NetCashFlow",
        "OutstandingReceivable",
        "OutstandingPayable",
        "HealthScore",
        "Entries",
        "OccurredAt",
        "Direction",
        "Amount",
        "SettlementStatus",
        "CategoryName",
    };

    // ==================== 1. 颜色字面量扫描（需求 1.12、6.1） ====================

    [Fact]
    public void Cashflow_view_contains_no_hex_color_literals()
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
            $"CashflowView.xaml 存在硬编码颜色字面量（应改用主题语义画刷）：{string.Join("; ", offenders)}");
    }

    // ==================== 2. 裸字号扫描（需求 1.12、6.1） ====================

    [Fact]
    public void Cashflow_view_has_no_bare_font_size_literals()
    {
        var offenders = CollectVisualLiteralOffenders("FontSize");

        Assert.True(offenders.Count == 0,
            $"CashflowView.xaml 存在裸 FontSize 数值字面量（应引用 Typography 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 3. 裸圆角扫描（需求 1.12、6.1） ====================

    [Fact]
    public void Cashflow_view_has_no_bare_corner_radius_literals()
    {
        var offenders = CollectVisualLiteralOffenders("CornerRadius");

        Assert.True(offenders.Count == 0,
            $"CashflowView.xaml 存在裸 CornerRadius 数值字面量（应引用 Shape 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 4. Margin/Padding 引用具名令牌（需求 1.12、6.1） ====================

    [Theory]
    [InlineData("Margin")]
    [InlineData("Padding")]
    public void Cashflow_view_margins_and_paddings_reference_named_tokens(string propertyName)
    {
        var doc = LoadView();
        var offenders = new List<string>();

        // 直接属性写法：<Element Margin="..." />
        foreach (var element in doc.Descendants())
        {
            var attr = element.Attribute(propertyName);
            if (attr is not null && !IsResourceReference(attr.Value))
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
            if (value is not null && !IsResourceReference(value))
            {
                offenders.Add($"<Setter Property=\"{propertyName}\" Value=\"{value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"CashflowView.xaml 的 {propertyName} 存在未引用具名令牌的字面量：{string.Join("; ", offenders)}");
    }

    // ==================== 5. Binding 契约核验（需求 6.2、10.4） ====================

    [Fact]
    public void Cashflow_view_binding_paths_are_within_contract()
    {
        var paths = ExtractBindingPaths(LoadViewText());

        Assert.NotEmpty(paths); // 防御：解析失败导致空集会让断言虚假通过。

        var unexpected = paths.Where(p => !AllowedBindingPaths.Contains(p)).OrderBy(p => p).ToList();

        Assert.True(unexpected.Count == 0,
            $"CashflowView.xaml 出现契约外的可疑绑定路径（疑似新增/重命名）：{string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Cashflow_view_uses_only_expected_command_bindings()
    {
        var paths = ExtractBindingPaths(LoadViewText());
        var commandPaths = paths.Where(p => p.EndsWith("Command", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

        // 既有契约仅暴露 RefreshCommand 作为重试通道，不得引入新命令路径。
        Assert.True(commandPaths.SetEquals(new[] { "RefreshCommand" }),
            $"CashflowView.xaml 命令绑定应仅为 RefreshCommand，实际：{string.Join(", ", commandPaths)}");
    }

    // ==================== 6. 代码后置无新增逻辑（需求 10.5、10.6） ====================

    [Fact]
    public void Cashflow_view_code_behind_has_no_added_logic()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), CodeBehindRelativePath);
        Assert.True(File.Exists(fullPath), $"未找到代码后置文件：{CodeBehindRelativePath}");

        var source = File.ReadAllText(fullPath);

        // 代码后置仅应调用 InitializeComponent()，不得新增事件处理器或状态计算逻辑。
        Assert.Contains("InitializeComponent();", source);

        // 不应存在事件转命令/状态计算的迹象：事件处理器签名、字段、属性。
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

    /// <summary>资源引用值（<c>{StaticResource ...}</c> / <c>{DynamicResource ...}</c>）。</summary>
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

    /// <summary>从 XAML 原文提取全部 <c>{Binding ...}</c> 的绑定路径首段。</summary>
    private static HashSet<string> ExtractBindingPaths(string xamlText)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);

        // 匹配 {Binding Path=Xxx} 或 {Binding Xxx ...}，捕获路径首标识符段。
        var regex = new Regex(@"\{Binding\s+(?:Path=)?([A-Za-z_][A-Za-z0-9_]*)");
        foreach (Match match in regex.Matches(xamlText))
        {
            paths.Add(match.Groups[1].Value);
        }

        return paths;
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
    /// 与既有 UI 静态检查测试（NonColorTokenTests / ColorTokenEnumerationTests）保持一致。
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
