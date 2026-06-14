using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// ProductsView 硬编码扫描与 Binding 契约核验（spec: commerce-settings-ui-rebuild，任务 5.2）。
///
/// 本测试对任务 5.1 重建后的 <c>Views/Sections/ProductsView.xaml</c> 做静态/枚举型核验，
/// 不实例化 WPF 控件，仅以 <see cref="XDocument"/> 与正则解析 XAML 源文件，覆盖：
///
///  1. 硬编码视觉字面量扫描（需求 2.1、10.4）：
///     - 全文不含 <c>#RRGGBB</c> / <c>#AARRGGBB</c> 颜色字面量（颜色须经 DynamicResource 语义画刷）。
///     - <c>FontSize</c> 属性必须引用资源（StaticResource/DynamicResource），不得为裸字号数字。
///     - <c>CornerRadius</c> 属性必须引用资源，不得为裸圆角数字。
///     - <c>Margin</c> / <c>Padding</c> / <c>BorderThickness</c> 属性必须引用具名令牌（资源引用），
///       不得为散落魔法数；结构性布局尺寸（Grid 列宽 <c>Width="*"/"Auto"</c> 等）豁免。
///     - 视图内派生的具名 <c>Thickness</c> 间距令牌取值须对齐 4px 栅格（间距字面量的唯一合法承载处）。
///
///  2. Binding 契约核验（需求 2.2、10.5、10.6）：
///     - XAML 中出现的全部 Binding 路径属于既有契约允许集（消费 <c>ProductsPage</c> 的
///       <c>Products</c> / <c>RefreshCommand</c> / 状态四标志 / <c>ErrorMessage</c> 及 <c>ProductRow</c>
///       投影字段），无可疑新增/重命名路径。
///     - 关键绑定（数据集合、刷新命令、状态机四标志、错误文案）确实存在。
///     - 代码后置文件保持「仅 InitializeComponent」，未新增事件转命令 / 状态计算逻辑。
///
/// 设计文档 §Testing Strategy 明确本特性属 WPF View / XAML 视觉重建，不适用 PBT；
/// 这里采用作用于固定文件内容的静态/枚举型断言。
/// </summary>
public sealed class ProductsViewContractTests
{
    private static readonly string ViewRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "ProductsView.xaml");

    private static readonly string CodeBehindRelativePath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "ProductsView.xaml.cs");

    /// <summary>
    /// 既有 Binding 契约允许集：父级 ProductsPage + CommercePageViewModel 状态/命令 + ProductRow 投影字段。
    /// 任何不在此集合内的绑定路径都被视为新增/重命名的可疑路径。
    /// </summary>
    private static readonly HashSet<string> AllowedBindingPaths = new(System.StringComparer.Ordinal)
    {
        // 父级（MainViewModel）：页 ViewModel 入口。
        "ProductsPage",
        // 页级（ProductsPageViewModel / CommercePageViewModel）：集合 + 命令 + 状态机四标志 + 错误文案。
        "Products",
        "RefreshCommand",
        "ShowLoading",
        "ShowContent",
        "IsEmpty",
        "HasError",
        "ErrorMessage",
        // 行级（ProductRow 投影字段）：DataGrid 列绑定。
        "Name",
        "Code",
        "ProductType",
        "DefaultPrice",
        "DefaultCost",
    };

    // ==================== 1. 颜色字面量扫描（需求 2.1） ====================

    [Fact]
    public void Xaml_contains_no_hardcoded_hex_color_literals()
    {
        string raw = ReadView();

        // 匹配 #RGB / #RRGGBB / #AARRGGBB 等十六进制颜色字面量。
        var matches = Regex.Matches(raw, "#(?:[0-9a-fA-F]{3,4}){1,2}\\b")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        Assert.True(matches.Count == 0,
            $"ProductsView.xaml 不应包含硬编码颜色字面量，发现：{string.Join(", ", matches)}");
    }

    // ==================== 2. 字号字面量扫描（需求 2.1） ====================

    [Fact]
    public void FontSize_attributes_all_reference_resource_tokens()
    {
        var offenders = AttributeValues("FontSize")
            .Where(v => !IsResourceReference(v))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"FontSize 必须引用字号令牌（StaticResource/DynamicResource），发现裸值：{string.Join(", ", offenders)}");
    }

    // ==================== 3. 圆角字面量扫描（需求 2.1） ====================

    [Fact]
    public void CornerRadius_attributes_all_reference_resource_tokens()
    {
        var offenders = AttributeValues("CornerRadius")
            .Where(v => !IsResourceReference(v))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"CornerRadius 必须引用圆角令牌，发现裸值：{string.Join(", ", offenders)}");
    }

    // ==================== 4. 间距字面量扫描（需求 2.1） ====================

    [Theory]
    [InlineData("Margin")]
    [InlineData("Padding")]
    [InlineData("BorderThickness")]
    public void Spacing_attributes_all_reference_named_thickness_tokens(string attributeName)
    {
        var offenders = AttributeValues(attributeName)
            .Where(v => !IsResourceReference(v))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{attributeName} 必须引用具名 Thickness / 令牌，发现散落魔法数：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Derived_thickness_tokens_align_to_4px_grid()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = LoadView();

        var thicknessTokens = doc.Descendants()
            .Where(e => e.Name.LocalName == "Thickness" && e.Attribute(xaml + "Key") is not null)
            .ToList();

        Assert.NotEmpty(thicknessTokens);

        foreach (var token in thicknessTokens)
        {
            string key = token.Attribute(xaml + "Key")!.Value;
            var components = token.Value
                .Split(',')
                .Select(s => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));

            Assert.All(components, c => Assert.True(c % 4 == 0,
                $"派生间距令牌 '{key}' 的分量 {c} 应对齐 4px 栅格（取自 UiSpace），不得为任意魔法数。"));
        }
    }

    // ==================== 5. Binding 契约核验（需求 2.2、10.5） ====================

    [Fact]
    public void All_binding_paths_belong_to_existing_contract()
    {
        var bindingPaths = ExtractBindingPaths(ReadView());

        var suspicious = bindingPaths
            .Where(p => !AllowedBindingPaths.Contains(p))
            .Distinct()
            .ToList();

        Assert.True(suspicious.Count == 0,
            $"发现不在既有契约允许集内的可疑绑定路径（疑似新增/重命名）：{string.Join(", ", suspicious)}");
    }

    [Theory]
    [InlineData("Products")]       // 数据集合
    [InlineData("RefreshCommand")] // 刷新/重试命令
    [InlineData("ShowLoading")]    // 状态机：加载
    [InlineData("IsEmpty")]        // 状态机：空
    [InlineData("HasError")]       // 状态机：错误
    [InlineData("ShowContent")]    // 状态机：内容
    [InlineData("ErrorMessage")]   // 错误文案
    public void Key_contract_bindings_are_present(string expectedPath)
    {
        var bindingPaths = ExtractBindingPaths(ReadView());

        Assert.Contains(expectedPath, bindingPaths);
    }

    // ==================== 6. 代码后置无新增逻辑（需求 10.5、10.6） ====================

    [Fact]
    public void Code_behind_contains_only_initialize_component()
    {
        string codeBehind = File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), CodeBehindRelativePath));

        Assert.Contains("InitializeComponent();", codeBehind);

        // 不应出现事件处理器 / 状态计算逻辑的常见痕迹。
        Assert.DoesNotContain("private void", codeBehind);
        Assert.DoesNotContain("RoutedEventArgs", codeBehind);
        Assert.DoesNotContain("DependencyProperty", codeBehind);
        Assert.DoesNotContain("+=", codeBehind);
    }

    // ==================== 辅助方法 ====================

    private static bool IsResourceReference(string value)
    {
        string v = value.Trim();
        return v.StartsWith("{StaticResource", System.StringComparison.Ordinal)
            || v.StartsWith("{DynamicResource", System.StringComparison.Ordinal)
            || v.StartsWith("{TemplateBinding", System.StringComparison.Ordinal);
    }

    /// <summary>收集 XAML 中所有指定属性名的属性值（无命名空间属性）。</summary>
    private static IEnumerable<string> AttributeValues(string attributeName)
    {
        return LoadView()
            .Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName == attributeName && string.IsNullOrEmpty(a.Name.NamespaceName))
            .Select(a => a.Value);
    }

    /// <summary>
    /// 从 XAML 源文本中抽取所有 {Binding ...} 表达式的绑定路径（首段，去除 Path= 前缀与限定符）。
    /// </summary>
    private static List<string> ExtractBindingPaths(string raw)
    {
        var paths = new List<string>();

        foreach (Match m in Regex.Matches(raw, "\\{Binding\\s+([^,}]+)"))
        {
            string first = m.Groups[1].Value.Trim();

            // 去除显式 Path= 前缀。
            if (first.StartsWith("Path=", System.StringComparison.Ordinal))
            {
                first = first.Substring("Path=".Length).Trim();
            }

            if (first.Length == 0)
            {
                continue;
            }

            paths.Add(first);
        }

        return paths;
    }

    private static string ReadView() =>
        File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), ViewRelativePath));

    private static XDocument LoadView() =>
        XDocument.Load(Path.Combine(ResolveRepositoryRoot(), ViewRelativePath));

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 静态测试（ColorTokenEnumerationTests / NonColorTokenTests）保持一致。
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
