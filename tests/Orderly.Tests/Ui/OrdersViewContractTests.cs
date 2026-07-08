using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 订单页（OrdersView）硬编码扫描与 Binding 契约核验测试（tasks.md 任务 6.2）。
///
/// 本测试对 commerce-settings-ui-rebuild 视觉重建后的
/// <c>src/Orderly.App/Views/Sections/OrdersView.xaml</c> 做静态解析校验，覆盖两类验收：
///
///   1. 硬编码扫描（需求 3.1、1.12）：
///      - 无 <c>#RRGGBB</c> / <c>#AARRGGBB</c> 等颜色字面量（颜色一律走 DynamicResource 语义画刷）。
///      - 无裸 <c>FontSize</c> 数值字面量（字号一律引用 Typography 令牌）。
///      - 无裸 <c>CornerRadius</c> 数值字面量（圆角一律引用 Shape 令牌）。
///      - <c>Margin</c> / <c>Padding</c> 一律引用具名令牌（StaticResource/DynamicResource），不出现裸数值间距。
///
///   2. Binding 契约核验（需求 3.2、10.4、10.5、10.6）：
///      - XAML 中出现的全部 <c>{Binding}</c> 路径均属于既有契约白名单（页面访问器 OrdersPage、
///        集合 Orders、命令 RefreshCommand、状态四标志 ShowLoading/HasError/IsEmpty/ShowContent、
///        ErrorMessage，以及 OrderRow 行字段 OrderNo/SalesStage/PaymentStage/FulfillmentStage/
///        Total/PaidAmount/ReceivableAmount/OrderedAt），不出现任何可疑新增 / 重命名路径。
///      - 既有关键绑定（集合、刷新命令、状态四标志、行字段）确实被消费（防止"白名单通过但实际未绑定"）。
///      - 代码后置 <c>OrdersView.xaml.cs</c> 仅含构造函数 + InitializeComponent，
///        无新增"事件转命令"或"状态计算"逻辑。
///
/// 设计文档 §Testing Strategy 明确本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT；这里采用作用于固定有限输入（单个 View 文件）的枚举型 / 静态型断言。
/// </summary>
public sealed class OrdersViewContractTests
{
    private static readonly string OrdersViewXamlPath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "OrdersView.xaml");

    private static readonly string OrdersViewCodeBehindPath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "OrdersView.xaml.cs");

    /// <summary>
    /// 既有 Binding 契约白名单：OrdersView 仅允许消费以下绑定路径。
    /// 任一不在此集合内的路径都视为"可疑新增 / 重命名"，判定不达标（需求 3.2、10.4、10.6）。
    /// </summary>
    private static readonly HashSet<string> AllowedBindingPaths = new(StringComparer.Ordinal)
    {
        // 父级 DataContext 上的页面访问器（MainViewModel.OrdersPage）。
        "OrdersPage",

        // 页面 VM（OrdersPageViewModel）与基类（CommercePageViewModel）暴露的成员。
        "Orders",
        "RefreshCommand",
        "ShowLoading",
        "HasError",
        "IsEmpty",
        "ShowContent",
        "ErrorMessage",
        "SelectedOrder",
        "Data",

        // OrderRow 行字段。
        "OrderNo",
        "SalesStage",
        "PaymentStage",
        "FulfillmentStage",
        "Total",
        "PaidAmount",
        "ReceivableAmount",
        "OrderedAt",
    };

    /// <summary>必须被实际消费的核心绑定（确保白名单不是"通过却没绑定"）。</summary>
    private static readonly string[] RequiredBindingPaths =
    {
        "OrdersPage",
        "Orders",
        "RefreshCommand",
        "ShowLoading",
        "HasError",
        "IsEmpty",
        "ShowContent",
        "ErrorMessage",
        "OrderNo",
        "SalesStage",
        "PaymentStage",
        "FulfillmentStage",
        "Total",
        "PaidAmount",
        "ReceivableAmount",
        "OrderedAt",
    };

    // ==================== 1. 硬编码扫描：颜色字面量（需求 3.1、1.12） ====================

    [Fact]
    public void OrdersView_has_no_hardcoded_color_literals()
    {
        var offenders = new List<string>();
        var colorRegex = new Regex(@"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})\b");

        foreach (var (element, attribute) in EnumerateAttributes(OrdersViewXamlPath))
        {
            if (colorRegex.IsMatch(attribute.Value))
            {
                offenders.Add($"<{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"OrdersView.xaml 出现硬编码颜色字面量（应改用 DynamicResource 语义画刷）：\n  {string.Join("\n  ", offenders)}");
    }

    // ==================== 2. 硬编码扫描：裸 FontSize 数值（需求 3.1、1.12） ====================

    [Fact]
    public void OrdersView_FontSize_attributes_all_reference_tokens()
    {
        var offenders = new List<string>();

        foreach (var (element, attribute) in EnumerateAttributes(OrdersViewXamlPath))
        {
            if (attribute.Name.LocalName == "FontSize" && !IsResourceReference(attribute.Value))
            {
                offenders.Add($"<{element.Name.LocalName} FontSize=\"{attribute.Value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"OrdersView.xaml 出现裸 FontSize 数值（应引用 Typography 字号令牌）：\n  {string.Join("\n  ", offenders)}");
    }

    // ==================== 3. 硬编码扫描：裸 CornerRadius 数值（需求 3.1、1.12） ====================

    [Fact]
    public void OrdersView_CornerRadius_attributes_all_reference_tokens()
    {
        var offenders = new List<string>();

        foreach (var (element, attribute) in EnumerateAttributes(OrdersViewXamlPath))
        {
            if (attribute.Name.LocalName == "CornerRadius" && !IsResourceReference(attribute.Value))
            {
                offenders.Add($"<{element.Name.LocalName} CornerRadius=\"{attribute.Value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"OrdersView.xaml 出现裸 CornerRadius 数值（应引用 Shape 圆角令牌）：\n  {string.Join("\n  ", offenders)}");
    }

    // ==================== 4. 硬编码扫描：Margin / Padding 必须引用具名令牌（需求 3.1、1.12） ====================

    [Fact]
    public void OrdersView_Margin_and_Padding_reference_named_tokens()
    {
        var offenders = new List<string>();

        foreach (var (element, attribute) in EnumerateAttributes(OrdersViewXamlPath))
        {
            if ((attribute.Name.LocalName == "Margin" || attribute.Name.LocalName == "Padding")
                && !IsResourceReference(attribute.Value))
            {
                offenders.Add($"<{element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\">");
            }
        }

        Assert.True(offenders.Count == 0,
            $"OrdersView.xaml 的 Margin/Padding 出现裸数值间距（应引用 Shape 间距令牌）：\n  {string.Join("\n  ", offenders)}");
    }

    // ==================== 5. Binding 契约：无可疑新增 / 重命名路径（需求 3.2、10.4、10.6） ====================

    [Fact]
    public void OrdersView_binding_paths_are_within_existing_contract()
    {
        var actual = ExtractBindingPaths(OrdersViewXamlPath);

        var suspicious = actual.Where(p => !AllowedBindingPaths.Contains(p)).OrderBy(p => p).ToList();

        Assert.True(suspicious.Count == 0,
            $"OrdersView.xaml 出现不属于既有契约的可疑绑定路径（疑似新增 / 重命名）：{string.Join(", ", suspicious)}");
    }

    // ==================== 6. Binding 契约：核心既有绑定确实被消费（需求 3.2） ====================

    [Fact]
    public void OrdersView_consumes_required_existing_bindings()
    {
        var actual = ExtractBindingPaths(OrdersViewXamlPath);

        var missing = RequiredBindingPaths.Where(p => !actual.Contains(p)).OrderBy(p => p).ToList();

        Assert.True(missing.Count == 0,
            $"OrdersView.xaml 未消费以下应有的既有绑定：{string.Join(", ", missing)}");
    }

    // ==================== 7. 代码后置无新增逻辑（需求 10.5） ====================

    [Fact]
    public void OrdersView_code_behind_has_no_added_logic()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), OrdersViewCodeBehindPath);
        Assert.True(File.Exists(fullPath), $"未找到代码后置文件：{OrdersViewCodeBehindPath}");

        var text = File.ReadAllText(fullPath);

        // 必须保留标准结构。
        Assert.Contains("InitializeComponent();", text);
        Assert.Contains("public OrdersView()", text);

        // 不得新增"事件转命令"/"状态计算"等逻辑标志（需求 10.5）。
        var forbidden = new[]
        {
            "private void", "public void", "internal void", "protected void",
            "private async", "public async", "internal async", "protected async",
            "event ", "DependencyProperty", "+=", "RelayCommand", "ICommand",
            " if (", " for (", " foreach (", " switch (", " while (",
        };

        var hits = forbidden.Where(token => text.Contains(token, StringComparison.Ordinal)).ToList();

        Assert.True(hits.Count == 0,
            $"OrdersView.xaml.cs 出现疑似新增逻辑标志（代码后置应保持仅 InitializeComponent）：{string.Join(", ", hits)}");
    }

    // ==================== 辅助方法 ====================

    /// <summary>枚举指定 XAML 文件全部元素的全部属性（注释为 XComment 节点，天然被排除）。</summary>
    private static IEnumerable<(XElement Element, XAttribute Attribute)> EnumerateAttributes(string relativePath)
    {
        var doc = LoadXaml(relativePath);
        foreach (var element in doc.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                yield return (element, attribute);
            }
        }
    }

    /// <summary>解析全部属性值中的 <c>{Binding ...}</c> 路径（取关键字后、首个逗号或右花括号前的部分）。</summary>
    private static HashSet<string> ExtractBindingPaths(string relativePath)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var bindingRegex = new Regex(@"\{\s*Binding\b");

        foreach (var (_, attribute) in EnumerateAttributes(relativePath))
        {
            var value = attribute.Value;
            foreach (Match match in bindingRegex.Matches(value))
            {
                var remainder = value.Substring(match.Index + match.Length);
                var path = ParseBindingPath(remainder);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// 从 <c>{Binding</c> 之后的剩余片段中取出绑定路径：截取到首个逗号 / 右花括号，
    /// 去除可选的 <c>Path=</c> 前缀；空路径（如 <c>{Binding}</c>、<c>{Binding Path=.}</c>）返回空串。
    /// </summary>
    private static string ParseBindingPath(string remainder)
    {
        int end = remainder.Length;
        int comma = remainder.IndexOf(',');
        int brace = remainder.IndexOf('}');
        if (comma >= 0)
        {
            end = Math.Min(end, comma);
        }

        if (brace >= 0)
        {
            end = Math.Min(end, brace);
        }

        var token = remainder.Substring(0, end).Trim();

        if (token.StartsWith("Path=", StringComparison.Ordinal))
        {
            token = token.Substring("Path=".Length).Trim();
        }

        // 仅取顶层属性名（剥离形如 .Sub / [idx] 的子路径），用于契约比对。
        int dot = token.IndexOf('.');
        if (dot >= 0)
        {
            token = token.Substring(0, dot);
        }

        return token == "." ? string.Empty : token;
    }

    /// <summary>判断属性值是否为 <c>{StaticResource ...}</c> / <c>{DynamicResource ...}</c> 资源引用（即具名令牌）。</summary>
    private static bool IsResourceReference(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("{StaticResource", StringComparison.Ordinal)
               || trimmed.StartsWith("{DynamicResource", StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(string relativePath)
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"未找到被测 XAML 文件：{relativePath}");
        return XDocument.Load(fullPath);
    }

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根，
    /// 与既有 UI 令牌测试（ColorTokenEnumerationTests / NonColorTokenTests）保持一致。
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
