using Orderly.App.ViewModels.Helpers;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Boundary unit tests for <see cref="PasswordStrengthEvaluator.Evaluate"/> (设计 §9.2 / §11).
///
/// <para>覆盖 §9.2 打分规则的档位边界：</para>
/// <list type="bullet">
/// <item>空串 → <see cref="PasswordStrength.Empty"/>；长度 &lt; 8 → <see cref="PasswordStrength.Weak"/>（硬下限）。</item>
/// <item>长度边界 8 / 12 / 16 各 +1，字符类别数 2 / 3 / 4 各 +1；<c>score &lt;= 2</c> → Weak、
/// <c>3</c> → Fair、<c>4</c> → Good、其余 → Strong。</item>
/// <item>弱模式（长重复串 / 简单顺序模式）触发 <c>WeakPatternPenalty</c> 降档。</item>
/// </list>
///
/// **Validates: Requirements 8.5**
/// </summary>
public sealed class PasswordStrengthBoundaryTests
{
    /// <summary>
    /// 空串与长度 &lt; 8 的硬下限：空串恒 <see cref="PasswordStrength.Empty"/>，
    /// 长度不足 8（即便四类别齐全）一律 <see cref="PasswordStrength.Weak"/>，先于正向打分判定。
    /// </summary>
    [Theory]
    [InlineData("", PasswordStrength.Empty)]            // 空串 → Empty
    [InlineData("Ab1!", PasswordStrength.Weak)]         // 长度 4 < 8 → Weak
    [InlineData("aB3!dE6", PasswordStrength.Weak)]      // 长度 7 < 8、四类别齐全仍 → Weak（硬下限先行）
    public void Evaluate_returns_empty_or_weak_below_length_floor(string password, PasswordStrength expected)
    {
        Assert.Equal(expected, PasswordStrengthEvaluator.Evaluate(password));
    }

    /// <summary>
    /// 长度边界（8 / 11 / 12 / 15 / 16）× 类别数（2 / 3 / 4）的档位矩阵。
    /// 所有用例均刻意避开弱模式（无 ≥3 连续重复、无 ≥4 顺序串），仅验证长度/类别的正向打分映射。
    /// </summary>
    [Theory]
    // 类别数 = 2（小写 + 大写）
    [InlineData("aBcDeFgH", PasswordStrength.Weak)]              // len8  cat2 → score 1+1=2 → Weak
    [InlineData("aBcDeFgHiJk", PasswordStrength.Weak)]          // len11 cat2 → score 1+1=2 → Weak（未达 12）
    [InlineData("aBcDeFgHiJkL", PasswordStrength.Fair)]         // len12 cat2 → score 2+1=3 → Fair
    [InlineData("aBcDeFgHiJkLmNo", PasswordStrength.Fair)]      // len15 cat2 → score 2+1=3 → Fair（未达 16）
    [InlineData("aBcDeFgHiJkLmNoP", PasswordStrength.Good)]     // len16 cat2 → score 3+1=4 → Good
    // 类别数 = 3（小写 + 大写 + 数字）
    [InlineData("aB3dE6gH", PasswordStrength.Fair)]             // len8  cat3 → score 1+2=3 → Fair
    [InlineData("aB3dE6gH9kM2", PasswordStrength.Good)]         // len12 cat3 → score 2+2=4 → Good
    [InlineData("aB3dE6gH9kM2pR5t", PasswordStrength.Strong)]   // len16 cat3 → score 3+2=5 → Strong
    // 类别数 = 4（小写 + 大写 + 数字 + 符号）
    [InlineData("aB3!dE6@", PasswordStrength.Good)]             // len8  cat4 → score 1+3=4 → Good
    [InlineData("aB3!dE6@gH9#", PasswordStrength.Strong)]       // len12 cat4 → score 2+3=5 → Strong
    [InlineData("aB3!dE6@gH9#kM2$", PasswordStrength.Strong)]   // len16 cat4 → score 3+3=6 → Strong
    public void Evaluate_maps_length_and_category_boundaries_to_expected_tier(string password, PasswordStrength expected)
    {
        Assert.Equal(expected, PasswordStrengthEvaluator.Evaluate(password));
    }

    /// <summary>
    /// 弱模式触发降档：在长度/类别正向打分之上叠加惩罚，使原本更高的档位被下调。
    /// </summary>
    [Theory]
    // 长重复串 "aaaa"(+2) 叠加顺序串 "1234"(+2)：len8 cat2 基础分 2 - 4 → 钳到 0 → Weak
    [InlineData("aaaa1234", PasswordStrength.Weak)]
    // 仅 3 连重复 "xxx"(+1)：len12 cat3 基础分 4(Good) - 1 → 3 → Fair（降一档）
    [InlineData("aB3dxxxkM2pq", PasswordStrength.Fair)]
    // 4 连顺序串 "defg"(+2)：len12 cat3 基础分 4(Good) - 2 → 2 → Weak（降两档）
    [InlineData("aB3defg9kM2x", PasswordStrength.Weak)]
    // 4 连重复 "wwww"(+2)：len12 cat3 基础分 4(Good) - 2 → 2 → Weak（降两档）
    [InlineData("aB3dwwww9kM2", PasswordStrength.Weak)]
    public void Evaluate_applies_weak_pattern_penalty_to_downgrade(string password, PasswordStrength expected)
    {
        Assert.Equal(expected, PasswordStrengthEvaluator.Evaluate(password));
    }

    /// <summary>
    /// 对照验证：在长度/类别完全相同的前提下，含弱模式的口令档位严格低于无弱模式的口令，
    /// 直接体现惩罚层的「降档」语义（而非偶然落在同一档）。
    /// </summary>
    [Fact]
    public void Evaluate_weak_pattern_variant_is_strictly_lower_than_clean_counterpart()
    {
        // 二者均为 len12 / cat3（小写+大写+数字），仅顺序串 "defg" 不同。
        PasswordStrength clean = PasswordStrengthEvaluator.Evaluate("aB3dW6gH9kM2");   // 无弱模式 → Good
        PasswordStrength weak = PasswordStrengthEvaluator.Evaluate("aB3defg9kM2x");    // 含 "defg" → Weak

        Assert.Equal(PasswordStrength.Good, clean);
        Assert.True(weak < clean, $"含弱模式口令档位({weak})应严格低于无弱模式对照({clean})");
    }
}
