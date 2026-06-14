using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 库存页（InventoryView）硬编码扫描与 Binding 契约核验（spec: commerce-settings-ui-rebuild，任务 7.2）。
///
/// 本测试对重建后的 <c>Views/Sections/InventoryView.xaml</c> 做静态/枚举型校验，覆盖：
///
///  * 硬编码扫描（需求 4.1、10.x 交付门禁）：断言 XAML 不含
///    - 颜色字面量（#RGB / #RRGGBB / #AARRGGBB），颜色必须经 DynamicResource 引用主题语义画刷；
///    - 裸 <c>FontSize</c> / <c>CornerRadius</c> 数值字面量，字号/圆角必须引用全局令牌；
///    - 裸 <c>Margin</c> / <c>Padding</c> 字面量，间距必须引用具名令牌（StaticResource）。
///
///  * Binding 契约核验（需求 4.2、10.4、10.5、10.6）：断言 XAML 引用的所有绑定路径都属于
///    <see cref="InventoryPageViewModel"/> 既有契约（页级：Items / RefreshCommand / 状态四标志
///    ShowLoading·HasError·IsEmpty·ShowContent / ErrorMessage；行级 <c>InventoryRow</c> 字段：
///    Name / Sku / QuantityAvailable / ReorderThreshold / CoverageDays / SuggestedReorderQuantity /
///    IsLowStock / ShouldReorder；根投影 InventoryPage），不出现任何可疑新路径。
///
///  * 代码后置无新增逻辑（需求 10.5）：断言 <c>InventoryView.xaml.cs</c> 仅含构造函数 +
///    InitializeComponent，无事件转命令 / 状态计算逻辑。
///
/// 设计文档 §Testing Strategy 明确本特性属 WPF View / XAML 视觉重建，不适用 PBT；
/// 这里采用作用于单一固定文件的静态断言。
/// </summary>
public sealed class InventoryViewContractTests
{
    private static readonly string XamlRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "InventoryView.xaml");

    private static readonly string CodeBehindRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "InventoryView.xaml.cs");

    // 颜色字面量：#RGB / #RRGGBB / #AARRGGBB。
    private static readonly Regex ColorLiteral =
        new(@"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b", RegexOptions.Compiled);

    // {Binding <path> ...} 中的路径（去除 Path= 前缀；忽略空 {Binding}）。
    private static readonly Regex BindingPath =
        new(@"\{Binding\s+(?:Path=)?([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    // ============================================================
    //  1. 硬编码扫描：颜色字面量
    // ============================================================

    [Fact]
    public void Xaml_contains_no_color_literals()
    {
        string xaml = ReadXamlText();
        var offenders = xaml
            .Split('\n')
            .Select((line, idx) => (Line: line, Number: idx + 1))
            .Where(x => ColorLiteral.IsMatch(x.Line))
            .Select(x => $"L{x.Number}: {x.Line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "InventoryView.xaml 含颜色字面量（应改为 DynamicResource 引用主题语义画刷）：\n" +
            string.Join("\n", offenders));
    }

    // ============================================================
    //  2. 硬编码扫描：裸 FontSize / CornerRadius 数值字面量
    // ============================================================

    [Theory]
    [InlineData("FontSize")]
    [InlineData("CornerRadius")]
    public void Xaml_has_no_bare_numeric_visual_attribute(string attributeName)
    {
        var offenders = AllAttributes()
            .Where(a => a.Name.LocalName == attributeName && !IsResourceReference(a.Value))
            .Select(a => $"{a.Name.LocalName}=\"{a.Value}\"")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"InventoryView.xaml 含裸 {attributeName} 数值字面量（应引用令牌）：\n" +
            string.Join("\n", offenders));
    }

    // ============================================================
    //  3. 硬编码扫描：Margin / Padding 必须引用具名令牌
    // ============================================================

    [Theory]
    [InlineData("Margin")]
    [InlineData("Padding")]
    public void Spacing_attributes_reference_named_tokens(string attributeName)
    {
        var offenders = AllAttributes()
            .Where(a => a.Name.LocalName == attributeName && !IsResourceReference(a.Value))
            .Select(a => $"{a.Name.LocalName}=\"{a.Value}\"")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"InventoryView.xaml 的 {attributeName} 含字面量（应引用具名间距令牌 StaticResource）：\n" +
            string.Join("\n", offenders));
    }

    // ============================================================
    //  4. Binding 契约核验：所有绑定路径都属于既有契约
    // ============================================================

    // 页级契约（DataContext = {Binding InventoryPage}）：状态四标志 + ErrorMessage + RefreshCommand + Items。
    // 行级契约（InventoryRow）：Name/Sku/QuantityAvailable/ReorderThreshold/CoverageDays/
    //   SuggestedReorderQuantity/IsLowStock/ShouldReorder。
    // 根投影：InventoryPage（MainViewModel 暴露的页面投影）。
    private static readonly HashSet<string> AllowedBindingPaths = new(System.StringComparer.Ordinal)
    {
        // 根投影
        "InventoryPage",
        // 页级状态契约
        "ShowLoading", "HasError", "IsEmpty", "ShowContent", "ErrorMessage", "RefreshCommand", "Items",
        // 行级 InventoryRow 字段
        "Name", "Sku", "QuantityAvailable", "ReorderThreshold",
        "CoverageDays", "SuggestedReorderQuantity", "IsLowStock", "ShouldReorder",
    };

    [Fact]
    public void All_binding_paths_belong_to_existing_contract()
    {
        var referenced = ExtractBindingPaths();

        Assert.NotEmpty(referenced); // 自检：确实解析到了绑定

        var suspicious = referenced
            .Where(p => !AllowedBindingPaths.Contains(p))
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .ToList();

        Assert.True(
            suspicious.Count == 0,
            "InventoryView.xaml 出现不属于既有契约的可疑绑定路径（疑似新增/重命名 Binding）：\n" +
            string.Join("\n", suspicious));
    }

    // ============================================================
    //  5. 代码后置无新增逻辑
    // ============================================================

    [Fact]
    public void Code_behind_has_no_added_logic()
    {
        string code = File.ReadAllText(Resolve(CodeBehindRelativePath));

        // 仅允许构造函数中调用 InitializeComponent，不得出现事件处理 / 命令转发 / 状态计算等逻辑标志。
        Assert.Contains("InitializeComponent();", code);

        string[] forbidden =
        {
            "void On",        // 事件处理器
            "EventHandler",
            "RelayCommand",
            "ICommand",
            "PropertyChanged",
            "DependencyProperty",
            "_sender",
            "RoutedEventArgs",
        };

        var hits = forbidden.Where(token => code.Contains(token, System.StringComparison.Ordinal)).ToList();

        Assert.True(
            hits.Count == 0,
            "InventoryView.xaml.cs 出现疑似新增逻辑标志（应保持仅 InitializeComponent）：" +
            string.Join(", ", hits));
    }

    // ============================================================
    //  辅助方法
    // ============================================================

    private static IEnumerable<string> ExtractBindingPaths()
    {
        string xaml = ReadXamlText();
        return BindingPath.Matches(xaml)
            .Select(m => m.Groups[1].Value)
            .Distinct(System.StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<XAttribute> AllAttributes()
    {
        var doc = XDocument.Load(Resolve(XamlRelativePath));
        return doc.Descendants()
            .SelectMany(e => e.Attributes())
            .ToList();
    }

    private static bool IsResourceReference(string value)
    {
        string v = value.Trim();
        return v.StartsWith("{StaticResource", System.StringComparison.Ordinal)
            || v.StartsWith("{DynamicResource", System.StringComparison.Ordinal)
            || v.StartsWith("{TemplateBinding", System.StringComparison.Ordinal)
            || v.StartsWith("{Binding", System.StringComparison.Ordinal);
    }

    private static string ReadXamlText() => File.ReadAllText(Resolve(XamlRelativePath));

    private static string Resolve(string relativePath)
    {
        string fullPath = Path.Combine(ResolveRepositoryRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"未找到被测文件：{relativePath}");
        return fullPath;
    }

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

        throw new System.InvalidOperationException(
            "无法通过向上查找 Orderly.sln 定位仓库根目录，起点：" + AppContext.BaseDirectory + "。");
    }
}
