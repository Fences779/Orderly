using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 静态契约核验测试（spec: commerce-settings-ui-rebuild，任务 8.2）。
///
/// 针对客户页 <c>Views/Sections/CustomersView.xaml</c> 做两类确定性静态检查，
/// 设计文档 §Testing Strategy 已说明本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT，故此处采用作用于固定文件的枚举 / 静态断言：
///
///   1. 硬编码视觉字面量扫描（需求 5.1、1.12）：
///      · 不含颜色字面量（#RGB / #RRGGBB / #AARRGGBB）。
///      · 不含裸 FontSize / CornerRadius 数值字面量（必须引用令牌资源）。
///      · 所有 Margin / Padding 引用具名令牌资源（StaticResource / DynamicResource），不写散值。
///
///   2. Binding 契约核验（需求 5.2、10.4、10.5、10.6）：
///      · 所有 <c>{Binding ...}</c> 路径都属于 CustomersPageViewModel / 基类 / CustomerRow
///        既有契约白名单，无可疑新增路径。
///      · 代码后置文件不新增"事件转命令 / 状态计算"逻辑（仅 InitializeComponent）。
///
/// 契约白名单来源（均为既有、不新增不重命名）：
///   · 绑定根：MainViewModel.CustomersPage（<c>{Binding CustomersPage}</c>）。
///   · CommercePageViewModel 基类：RefreshCommand、ShowLoading、HasError、ErrorMessage、IsEmpty、ShowContent。
///   · CustomersPageViewModel：Customers 集合。
///   · CustomerRow：Name、Phone、RecencyDays、Frequency、Monetary（CustomerId 未在 View 消费）。
/// </summary>
public sealed class CustomersViewContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string CustomersViewXamlPath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "CustomersView.xaml");

    private static readonly string CustomersViewCodeBehindPath =
        Path.Combine("src", "Orderly.App", "Views", "Sections", "CustomersView.xaml.cs");

    /// <summary>CustomersView 允许消费的全部既有绑定路径（不新增、不重命名）。</summary>
    private static readonly HashSet<string> AllowedBindingPaths = new(System.StringComparer.Ordinal)
    {
        // 绑定根（MainViewModel）
        "CustomersPage",
        // CommercePageViewModel 基类暴露的状态四标志 + 错误文案 + 重试命令
        "RefreshCommand",
        "ShowLoading",
        "HasError",
        "ErrorMessage",
        "IsEmpty",
        "ShowContent",
        // CustomersPageViewModel 数据集合
        "Customers",
        // CustomerRow 行字段
        "Name",
        "Phone",
        "RecencyDays",
        "Frequency",
        "Monetary",
    };

    // ==================== 1. 硬编码颜色字面量扫描（需求 5.1 / 1.12） ====================

    [Fact]
    public void CustomersView_contains_no_hardcoded_color_literals()
    {
        string xaml = ReadCustomersViewXaml();

        // #RGB / #ARGB / #RRGGBB / #AARRGGBB 形式的颜色字面量。
        var colorMatches = Regex.Matches(
            xaml,
            "#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})\\b");

        var literals = colorMatches.Select(m => m.Value).Distinct().ToList();

        Assert.True(
            literals.Count == 0,
            $"CustomersView.xaml 含硬编码颜色字面量（应改用 DynamicResource 语义画刷）：{string.Join(", ", literals)}");
    }

    // ==================== 2. 裸 FontSize / CornerRadius 数值字面量扫描（需求 5.1 / 1.12） ====================

    [Fact]
    public void CustomersView_has_no_bare_FontSize_or_CornerRadius_literals()
    {
        var doc = LoadCustomersViewDocument();
        var offenders = new List<string>();

        foreach (var element in doc.Descendants())
        {
            // 直接以属性形式设置的 FontSize / CornerRadius。
            foreach (var attr in element.Attributes())
            {
                if ((attr.Name.LocalName == "FontSize" || attr.Name.LocalName == "CornerRadius")
                    && !IsResourceReference(attr.Value))
                {
                    offenders.Add($"<{element.Name.LocalName} {attr.Name.LocalName}=\"{attr.Value}\">");
                }
            }

            // 以 Setter Property="FontSize|CornerRadius" Value="..." 形式设置的。
            if (element.Name.LocalName == "Setter")
            {
                var prop = element.Attribute("Property")?.Value;
                var val = element.Attribute("Value")?.Value;
                if ((prop == "FontSize" || prop == "CornerRadius") && val is not null && !IsResourceReference(val))
                {
                    offenders.Add($"<Setter Property=\"{prop}\" Value=\"{val}\">");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"CustomersView.xaml 含裸 FontSize/CornerRadius 数值字面量（应引用令牌资源）：{string.Join("; ", offenders)}");
    }

    // ==================== 3. Margin / Padding 必须引用具名令牌（需求 5.1 / 1.12） ====================

    [Fact]
    public void CustomersView_margins_and_paddings_reference_named_tokens()
    {
        var doc = LoadCustomersViewDocument();
        var offenders = new List<string>();

        foreach (var element in doc.Descendants())
        {
            foreach (var attr in element.Attributes())
            {
                if (attr.Name.LocalName is "Margin" or "Padding" && !IsResourceReference(attr.Value))
                {
                    offenders.Add($"<{element.Name.LocalName} {attr.Name.LocalName}=\"{attr.Value}\">");
                }
            }

            if (element.Name.LocalName == "Setter")
            {
                var prop = element.Attribute("Property")?.Value;
                var val = element.Attribute("Value")?.Value;
                if (prop is "Margin" or "Padding" && val is not null && !IsResourceReference(val))
                {
                    offenders.Add($"<Setter Property=\"{prop}\" Value=\"{val}\">");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"CustomersView.xaml 的 Margin/Padding 存在散值字面量（应引用具名 Thickness 令牌）：{string.Join("; ", offenders)}");
    }

    // ==================== 4. Binding 路径属于既有契约（需求 5.2 / 10.4 / 10.6） ====================

    [Fact]
    public void CustomersView_only_binds_to_existing_contract_paths()
    {
        string xaml = ReadCustomersViewXaml();
        var paths = ExtractBindingPaths(xaml);

        Assert.True(paths.Count > 0, "未从 CustomersView.xaml 解析到任何 Binding，解析逻辑或文件可能异常。");

        var suspicious = paths.Where(p => !AllowedBindingPaths.Contains(p)).Distinct().ToList();

        Assert.True(
            suspicious.Count == 0,
            $"CustomersView.xaml 出现契约外（疑似新增/重命名）的绑定路径：{string.Join(", ", suspicious)}。" +
            $"允许集合：{string.Join(", ", AllowedBindingPaths)}");
    }

    [Fact]
    public void CustomersView_binds_core_state_machine_and_row_fields()
    {
        string xaml = ReadCustomersViewXaml();
        var paths = ExtractBindingPaths(xaml).ToHashSet(System.StringComparer.Ordinal);

        // 状态机骨架四标志 + 错误文案 + 重试命令 + 集合 + 关键行字段均应被消费。
        string[] mustBind =
        {
            "CustomersPage", "RefreshCommand", "ShowLoading", "HasError", "ErrorMessage",
            "IsEmpty", "ShowContent", "Customers", "Name", "Phone", "RecencyDays", "Frequency", "Monetary",
        };

        var missing = mustBind.Where(p => !paths.Contains(p)).ToList();

        Assert.True(
            missing.Count == 0,
            $"CustomersView.xaml 缺少预期的既有契约绑定：{string.Join(", ", missing)}");
    }

    // ==================== 5. 代码后置无新增逻辑（需求 10.5） ====================

    [Fact]
    public void CustomersView_code_behind_has_no_added_logic()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), CustomersViewCodeBehindPath);
        Assert.True(File.Exists(fullPath), $"未找到代码后置文件：{CustomersViewCodeBehindPath}");

        string code = File.ReadAllText(fullPath);

        // 仅允许构造函数内调用 InitializeComponent；不应出现事件处理器/状态计算等新增逻辑。
        Assert.Contains("InitializeComponent();", code);

        // 不应出现事件订阅、命令、依赖属性、字段或异步逻辑等"事件转命令/状态计算"信号。
        string[] forbiddenSignals =
        {
            "+=",                 // 事件订阅
            "RelayCommand",       // 命令定义
            "ICommand",
            "DependencyProperty",
            "async ",
            "PropertyChanged",
        };

        var hits = forbiddenSignals.Where(s => code.Contains(s)).ToList();

        Assert.True(
            hits.Count == 0,
            $"CustomersView.xaml.cs 出现疑似新增逻辑信号（应仅保留 InitializeComponent）：{string.Join(", ", hits)}");
    }

    // ==================== 辅助方法 ====================

    private static bool IsResourceReference(string value)
    {
        var v = value.Trim();
        return v.StartsWith("{StaticResource", System.StringComparison.Ordinal)
            || v.StartsWith("{DynamicResource", System.StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 XAML 文本中提取所有 <c>{Binding ...}</c> 的绑定路径（首个位置参数或 <c>Path=</c> 段）。
    /// 采用花括号深度感知扫描，避免被嵌套 markup extension（如 Converter={StaticResource ...}）干扰。
    /// </summary>
    private static List<string> ExtractBindingPaths(string xaml)
    {
        var paths = new List<string>();

        int index = 0;
        while ((index = xaml.IndexOf("{Binding", index, System.StringComparison.Ordinal)) >= 0)
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

            // inner = "{Binding" 与匹配 "}" 之间的内容。
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

        // 优先 Path= 段。
        foreach (var seg in segments)
        {
            var trimmed = seg.Trim();
            if (trimmed.StartsWith("Path=", System.StringComparison.Ordinal))
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

    private static string ReadCustomersViewXaml()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), CustomersViewXamlPath);
        Assert.True(File.Exists(fullPath), $"未找到被测视图文件：{CustomersViewXamlPath}");
        return File.ReadAllText(fullPath);
    }

    private static XDocument LoadCustomersViewDocument()
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), CustomersViewXamlPath);
        Assert.True(File.Exists(fullPath), $"未找到被测视图文件：{CustomersViewXamlPath}");
        return XDocument.Load(fullPath);
    }

    /// <summary>自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根。</summary>
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
