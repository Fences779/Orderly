using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 非颜色令牌枚举校验测试（tasks.md 任务 2.4）。
///
/// 本测试对 commerce-settings-ui-rebuild 视觉重建新增的两份非颜色令牌字典做静态/枚举型校验：
///   - <c>Views/Resources/Tokens/Typography.xaml</c>：字号阶 / 字重 / 字体族
///   - <c>Views/Resources/Tokens/Shape.xaml</c>：圆角 / 间距 / 阴影
///
/// 校验目标（对应需求）：
///   1. 设计文档要求的字号 / 圆角 / 间距 / 阴影 / 字体族令牌键全部存在（需求 1.5、1.6、1.7、1.8）。
///   2. 正文字号 ≥ 13px、辅助说明字号 ≥ 12px（需求 12.5）。
///   3. 新令牌键名（Ui* 前缀）与既有 <c>DesignTokens.xaml</c>（仍供我的页/设置页旧路径消费）键名不发生碰撞（需求 12.5 隔离约束）。
///
/// 设计文档 §Testing Strategy 明确本特性属 WPF View / XAML 视觉重建 + 设计令牌（配置资源），
/// 不适用 PBT；这里采用作用于固定有限令牌集合的枚举型断言。
/// </summary>
public sealed class NonColorTokenTests
{
    // XAML 命名空间：x:Key 等位于 xaml 命名空间。
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string TypographyPath =
        Path.Combine("src", "Orderly.App", "Views", "Resources", "Tokens", "Typography.xaml");

    private static readonly string ShapePath =
        Path.Combine("src", "Orderly.App", "Views", "Resources", "Tokens", "Shape.xaml");

    private static readonly string DesignTokensPath =
        Path.Combine("src", "Orderly.App", "Views", "Resources", "DesignTokens.xaml");

    // ==================== 1. 字号阶键齐备 + 取值（需求 1.5） ====================

    public static IEnumerable<object[]> FontSizeKeys()
    {
        yield return new object[] { "UiFontDisplay", 28d };
        yield return new object[] { "UiFontTitle", 20d };
        yield return new object[] { "UiFontSubtitle", 16d };
        yield return new object[] { "UiFontBody", 14d };
        yield return new object[] { "UiFontBodySm", 13d };
        yield return new object[] { "UiFontCaption", 12d };
    }

    [Theory]
    [MemberData(nameof(FontSizeKeys))]
    public void Typography_defines_required_font_size_tokens(string key, double expected)
    {
        var tokens = LoadKeyedElements(TypographyPath);

        Assert.True(tokens.ContainsKey(key), $"Typography.xaml 缺少字号令牌键 '{key}'。");
        Assert.Equal("Double", tokens[key].LocalName);
        Assert.Equal(expected, ParseDouble(tokens[key].Value), precision: 3);
    }

    [Fact]
    public void Typography_defines_at_least_six_font_size_levels()
    {
        var tokens = LoadKeyedElements(TypographyPath);
        var fontSizeKeys = tokens
            .Where(kv => kv.Value.LocalName == "Double" && kv.Key.StartsWith("UiFont", StringComparison.Ordinal)
                         && !kv.Key.StartsWith("UiFontWeight", StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();

        Assert.True(fontSizeKeys.Count >= 6, $"字号阶应 ≥ 6 级，实际 {fontSizeKeys.Count} 级：{string.Join(", ", fontSizeKeys)}");
    }

    // ==================== 2. 字重键齐备（需求 1.5） ====================

    [Theory]
    [InlineData("UiFontWeightRegular")]
    [InlineData("UiFontWeightMedium")]
    [InlineData("UiFontWeightSemiBold")]
    [InlineData("UiFontWeightBold")]
    public void Typography_defines_required_font_weight_tokens(string key)
    {
        var tokens = LoadKeyedElements(TypographyPath);

        Assert.True(tokens.ContainsKey(key), $"Typography.xaml 缺少字重令牌键 '{key}'。");
        Assert.Equal("FontWeight", tokens[key].LocalName);
    }

    // ==================== 3. 字体族键齐备（需求 1.5） ====================

    [Theory]
    [InlineData("UiFontFamilyCjk")]
    [InlineData("UiFontFamilyLatin")]
    public void Typography_defines_required_font_family_tokens(string key)
    {
        var tokens = LoadKeyedElements(TypographyPath);

        Assert.True(tokens.ContainsKey(key), $"Typography.xaml 缺少字体族令牌键 '{key}'。");
        Assert.Equal("FontFamily", tokens[key].LocalName);
        Assert.False(string.IsNullOrWhiteSpace(tokens[key].Value), $"字体族 '{key}' 不应为空（需配置回退链）。");
    }

    // ==================== 4. 字号下限（需求 12.5） ====================

    [Fact]
    public void Body_font_sizes_are_at_least_13px()
    {
        var tokens = LoadKeyedElements(TypographyPath);

        // 正文与正文小均属"正文"范畴，下限 13px。
        Assert.True(ParseDouble(tokens["UiFontBody"].Value) >= 13d,
            "UiFontBody 正文字号应 ≥ 13px（需求 12.5）。");
        Assert.True(ParseDouble(tokens["UiFontBodySm"].Value) >= 13d,
            "UiFontBodySm 正文小字号应 ≥ 13px（需求 12.5）。");
    }

    [Fact]
    public void Caption_font_size_is_at_least_12px()
    {
        var tokens = LoadKeyedElements(TypographyPath);

        Assert.True(ParseDouble(tokens["UiFontCaption"].Value) >= 12d,
            "UiFontCaption 辅助说明字号应 ≥ 12px（需求 12.5）。");
    }

    // ==================== 5. 圆角键齐备 + 取值（需求 1.6） ====================

    public static IEnumerable<object[]> RadiusKeys()
    {
        yield return new object[] { "UiRadiusSm", 2d };
        yield return new object[] { "UiRadiusMd", 4d };
        yield return new object[] { "UiRadiusLg", 8d };
    }

    [Theory]
    [MemberData(nameof(RadiusKeys))]
    public void Shape_defines_required_corner_radius_tokens(string key, double expected)
    {
        var tokens = LoadKeyedElements(ShapePath);

        Assert.True(tokens.ContainsKey(key), $"Shape.xaml 缺少圆角令牌键 '{key}'。");
        Assert.Equal("CornerRadius", tokens[key].LocalName);
        Assert.Equal(expected, ParseDouble(tokens[key].Value), precision: 3);
    }

    [Fact]
    public void Base_corner_radius_is_4px()
    {
        var tokens = LoadKeyedElements(ShapePath);

        Assert.Equal(4d, ParseDouble(tokens["UiRadiusMd"].Value), precision: 3);
    }

    // ==================== 6. 间距键齐备 + 取值（需求 1.7） ====================

    public static IEnumerable<object[]> SpaceKeys()
    {
        yield return new object[] { "UiSpaceXxs", 4d };
        yield return new object[] { "UiSpaceXs", 8d };
        yield return new object[] { "UiSpaceSm", 12d };
        yield return new object[] { "UiSpaceMd", 16d };
        yield return new object[] { "UiSpaceLg", 24d };
        yield return new object[] { "UiSpaceXl", 32d };
    }

    [Theory]
    [MemberData(nameof(SpaceKeys))]
    public void Shape_defines_required_spacing_tokens(string key, double expected)
    {
        var tokens = LoadKeyedElements(ShapePath);

        Assert.True(tokens.ContainsKey(key), $"Shape.xaml 缺少间距令牌键 '{key}'。");
        Assert.Equal("Double", tokens[key].LocalName);
        Assert.Equal(expected, ParseDouble(tokens[key].Value), precision: 3);
    }

    [Fact]
    public void Spacing_scale_has_at_least_six_levels_on_4px_grid()
    {
        var tokens = LoadKeyedElements(ShapePath);
        var spaceValues = tokens
            .Where(kv => kv.Key.StartsWith("UiSpace", StringComparison.Ordinal) && kv.Value.LocalName == "Double")
            .Select(kv => ParseDouble(kv.Value.Value))
            .ToList();

        Assert.True(spaceValues.Count >= 6, $"间距阶应 ≥ 6 级，实际 {spaceValues.Count} 级。");
        Assert.All(spaceValues, v => Assert.True(v % 4 == 0, $"间距值 {v} 应为 4px 基础单位的整数倍。"));
    }

    // ==================== 7. 阴影键齐备（需求 1.8） ====================

    [Theory]
    [InlineData("UiElevationCard")]
    [InlineData("UiElevationOverlay")]
    public void Shape_defines_required_elevation_tokens(string key)
    {
        var tokens = LoadKeyedElements(ShapePath);

        Assert.True(tokens.ContainsKey(key), $"Shape.xaml 缺少阴影令牌键 '{key}'。");
        Assert.Equal("DropShadowEffect", tokens[key].LocalName);
    }

    // ==================== 8. 键名碰撞校验（需求 12.5 隔离约束） ====================

    [Fact]
    public void New_token_keys_do_not_collide_with_DesignTokens()
    {
        var typographyKeys = LoadKeyedElements(TypographyPath).Keys.ToHashSet(StringComparer.Ordinal);
        var shapeKeys = LoadKeyedElements(ShapePath).Keys.ToHashSet(StringComparer.Ordinal);
        var designTokenKeys = LoadKeyedElements(DesignTokensPath).Keys.ToHashSet(StringComparer.Ordinal);

        var newKeys = new HashSet<string>(typographyKeys, StringComparer.Ordinal);
        newKeys.UnionWith(shapeKeys);

        var collisions = newKeys.Intersect(designTokenKeys, StringComparer.Ordinal).ToList();

        Assert.True(collisions.Count == 0,
            $"新令牌键与既有 DesignTokens.xaml 键名发生碰撞：{string.Join(", ", collisions)}");
    }

    [Fact]
    public void New_token_keys_all_use_Ui_prefix()
    {
        var typographyKeys = LoadKeyedElements(TypographyPath).Keys;
        var shapeKeys = LoadKeyedElements(ShapePath).Keys;

        var offenders = typographyKeys.Concat(shapeKeys)
            .Where(k => !k.StartsWith("Ui", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"新令牌键应统一使用 'Ui' 前缀以与既有键名隔离，违例键：{string.Join(", ", offenders)}");
    }

    // ==================== 辅助方法 ====================

    private sealed record KeyedElement(string LocalName, string Value);

    /// <summary>
    /// 解析指定 ResourceDictionary XAML 文件，返回 x:Key → (元素本地名, 内联文本值) 的映射。
    /// 仅收集带 x:Key 的直接资源元素。
    /// </summary>
    private static Dictionary<string, KeyedElement> LoadKeyedElements(string relativePath)
    {
        var fullPath = Path.Combine(ResolveRepositoryRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"未找到被测资源文件：{relativePath}");

        var doc = XDocument.Load(fullPath);
        var result = new Dictionary<string, KeyedElement>(StringComparer.Ordinal);

        foreach (var element in doc.Descendants())
        {
            var keyAttr = element.Attribute(Xaml + "Key");
            if (keyAttr is null)
            {
                continue;
            }

            // 取内联文本（去首尾空白）；带子元素的复杂资源（如 DropShadowEffect 用属性表达）值为空。
            var value = element.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault()?.Trim() ?? string.Empty;
            result[keyAttr.Value] = new KeyedElement(element.Name.LocalName, value);
        }

        return result;
    }

    private static double ParseDouble(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// 自测试程序集所在目录向上查找包含 <c>Orderly.sln</c> 的目录作为仓库根。
    /// 与既有测试（ForbiddenTermsRegressionTests）保持一致的定位方式。
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
