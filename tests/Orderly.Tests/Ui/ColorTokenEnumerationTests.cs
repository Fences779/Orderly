using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// Enumeration / static-verification tests for the commerce settings UI rebuild design tokens
/// (spec: commerce-settings-ui-rebuild, task 1.2). These are deterministic checks over a fixed,
/// finite token set — not property-based tests — and cover:
///
///  * Token key completeness: the required primary grades, seven neutral levels, four status
///    states (success/warning/error/info, each with a soft background + readable foreground),
///    and the focus brush are all present (Req 1.2, 1.3, 1.4, 12.3-focus).
///  * Light/Dark key-set parity: <c>ThemeLight.xaml</c> and <c>ThemeDark.xaml</c> expose exactly
///    the same set of brush keys, so the whole-dictionary swap in ThemeHelper keeps every
///    DynamicResource resolvable in both themes (Req 1.9).
///  * Contrast ratios: body text vs. background and each status badge foreground vs. its soft
///    background meet WCAG AA (≥ 4.5:1) using the relative-luminance formula, in both themes
///    (Req 12.1, 12.2).
/// </summary>
public sealed class ColorTokenEnumerationTests
{
    private static readonly XNamespace XamlX = "http://schemas.microsoft.com/winfx/2006/xaml";

    // ---- Required token keys (per design.md "Components and Interfaces" + Req 1.2-1.4) ----

    // Primary ink-blue grades: default / hover / pressed / disabled + light tint.
    private static readonly string[] PrimaryKeys =
    {
        "PrimaryBrush", "PrimaryHoverBrush", "PrimaryPressedBrush", "PrimaryDisabledBrush", "PrimaryLightBrush",
    };

    // Seven neutral levels: base background, surface, secondary surface, divider, primary text,
    // secondary text, caption text.
    private static readonly string[] NeutralKeys =
    {
        "PageBackgroundBrush",   // 底背景
        "SurfaceSoftBrush",      // 二级背景
        "SurfaceRaisedBrush",    // 表面背景
        "BorderBrushSoft",       // 分隔线
        "HeadingBrush",          // 主文字
        "SecondaryTextBrush",    // 次文字
        "CaptionTextBrush",      // 辅助文字
    };

    // Status four states, each a (soft background, readable foreground) pair:
    // success=Accent(green), warning=Warm(yellow), error=Danger(red), info=Blue(blue).
    private static readonly string[] StatusKeys =
    {
        "AccentSoftBrush", "AccentTextBrush",
        "WarmSoftBrush", "WarmTextBrush",
        "DangerSoftBrush", "DangerTextBrush",
        "BlueSoftBrush", "BlueTextBrush",
    };

    private const string FocusKey = "UiFocusBrush";

    // ---- Contrast pairs (foreground, background) that must reach >= 4.5:1 (Req 12.1, 12.2) ----
    private static readonly (string Foreground, string Background)[] ContrastPairs =
    {
        // Body text on the two primary surfaces.
        ("HeadingBrush", "PageBackgroundBrush"),
        ("HeadingBrush", "SurfaceRaisedBrush"),
        // Status badge foreground on its own soft background.
        ("AccentTextBrush", "AccentSoftBrush"),
        ("WarmTextBrush", "WarmSoftBrush"),
        ("DangerTextBrush", "DangerSoftBrush"),
        ("BlueTextBrush", "BlueSoftBrush"),
    };

    private const double MinContrast = 4.5;

    [Fact]
    public void Light_theme_defines_all_required_token_keys()
    {
        AssertRequiredKeysPresent(ThemeFile.Light);
    }

    [Fact]
    public void Dark_theme_defines_all_required_token_keys()
    {
        AssertRequiredKeysPresent(ThemeFile.Dark);
    }

    [Fact]
    public void Light_and_dark_themes_expose_identical_brush_key_sets()
    {
        var light = ParseBrushes(ThemeFile.Light).Keys.ToHashSet();
        var dark = ParseBrushes(ThemeFile.Dark).Keys.ToHashSet();

        var onlyInLight = light.Except(dark).OrderBy(k => k).ToArray();
        var onlyInDark = dark.Except(light).OrderBy(k => k).ToArray();

        Assert.True(
            onlyInLight.Length == 0 && onlyInDark.Length == 0,
            $"Theme key sets diverge.\n  Only in ThemeLight: {string.Join(", ", onlyInLight)}\n  Only in ThemeDark: {string.Join(", ", onlyInDark)}");
    }

    [Fact]
    public void Light_theme_contrast_pairs_meet_wcag_aa()
    {
        AssertContrastPairs(ThemeFile.Light);
    }

    [Fact]
    public void Dark_theme_contrast_pairs_meet_wcag_aa()
    {
        AssertContrastPairs(ThemeFile.Dark);
    }

    private static void AssertRequiredKeysPresent(ThemeFile theme)
    {
        var brushes = ParseBrushes(theme);
        var required = PrimaryKeys
            .Concat(NeutralKeys)
            .Concat(StatusKeys)
            .Append(FocusKey)
            .ToArray();

        var missing = required.Where(k => !brushes.ContainsKey(k)).OrderBy(k => k).ToArray();

        Assert.True(
            missing.Length == 0,
            $"{theme} is missing required token key(s): {string.Join(", ", missing)}");
    }

    private static void AssertContrastPairs(ThemeFile theme)
    {
        var brushes = ParseBrushes(theme);

        foreach (var (foreground, background) in ContrastPairs)
        {
            Assert.True(brushes.ContainsKey(foreground), $"{theme} missing key '{foreground}' for contrast check.");
            Assert.True(brushes.ContainsKey(background), $"{theme} missing key '{background}' for contrast check.");

            double ratio = ContrastRatio(brushes[foreground], brushes[background]);

            Assert.True(
                ratio >= MinContrast,
                $"{theme}: contrast of {foreground} ({brushes[foreground]}) on {background} ({brushes[background]}) " +
                $"is {ratio:F2}:1, below the required {MinContrast:F1}:1.");
        }
    }

    // ---- XAML parsing ----

    private enum ThemeFile
    {
        Light,
        Dark,
    }

    private static string ThemePath(ThemeFile theme)
    {
        var repoRoot = ResolveRepositoryRoot();
        string fileName = theme == ThemeFile.Light ? "ThemeLight.xaml" : "ThemeDark.xaml";
        return Path.Combine(repoRoot, "src", "Orderly.App", "Views", "Resources", "Themes", fileName);
    }

    /// <summary>
    /// Parses every <c>SolidColorBrush</c> entry keyed by <c>x:Key</c> into a key -&gt; #RRGGBB map.
    /// Only solid-color brushes with a resolvable hex color are returned.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseBrushes(ThemeFile theme)
    {
        var doc = XDocument.Load(ThemePath(theme));
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = element.Attribute(XamlX + "Key")?.Value;
            var color = element.Attribute("Color")?.Value;
            if (key is null || color is null)
            {
                continue;
            }

            map[key] = color;
        }

        return map;
    }

    // ---- WCAG relative-luminance contrast ----

    private static double ContrastRatio(string hexA, string hexB)
    {
        double lA = RelativeLuminance(hexA);
        double lB = RelativeLuminance(hexB);
        double lighter = System.Math.Max(lA, lB);
        double darker = System.Math.Min(lA, lB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        double rl = LinearizeChannel(r / 255.0);
        double gl = LinearizeChannel(g / 255.0);
        double bl = LinearizeChannel(b / 255.0);
        return (0.2126 * rl) + (0.7152 * gl) + (0.0722 * bl);
    }

    private static double LinearizeChannel(double channel)
        => channel <= 0.03928
            ? channel / 12.92
            : System.Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static (int R, int G, int B) ParseHex(string hex)
    {
        string h = hex.TrimStart('#');

        // Accept #AARRGGBB by dropping the leading alpha byte.
        if (h.Length == 8)
        {
            h = h.Substring(2);
        }

        Assert.True(h.Length == 6, $"Unsupported color literal '{hex}' (expected #RRGGBB or #AARRGGBB).");

        int r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (r, g, b);
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

        throw new InvalidOperationException(
            "Could not locate the repository root (Orderly.sln) by walking up from " + AppContext.BaseDirectory + ".");
    }
}
