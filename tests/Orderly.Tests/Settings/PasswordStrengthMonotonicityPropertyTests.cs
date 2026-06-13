using System;
using CsCheck;
using Orderly.App.ViewModels.Helpers;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="PasswordStrengthEvaluator.Evaluate"/> conditional
/// strength monotonicity (design §9.2 / §11 Property 1).
///
/// <para><b>Property 1: 强度单调性（条件性）.</b>
/// 对任意密码 <c>p</c> 与任意追加串 <c>s</c>，<b>当追加内容不构成重复或可预测的弱模式时</b>，
/// 若 <c>len(p+s) &gt;= len(p)</c> 且字符类别集合不减少，则
/// <c>Evaluate(p+s) &gt;= Evaluate(p)</c>（强度档不下降）。</para>
///
/// <para>关键观察：在密码后<i>追加</i>字符这一操作下，长度只增不减、字符类别集合只增不减
/// （追加永远不会移除原有字符或类别），因此 Property 1 的两个长度/类别前提在追加场景下恒成立。
/// 唯一需要施加的「条件性」前提是「追加内容不引入重复 / 可预测弱模式」，即 <c>p+s</c> 不触发
/// <see cref="HasWeakPattern"/> 所刻画的弱模式（长重复串 / 简单顺序模式，对应 §9.2 的
/// <c>WeakPatternPenalty</c> 惩罚层）。本测试通过生成阶段的前置过滤排除引入弱模式的用例，
/// 以验证「不构成弱模式时单调不降」这一 SHALL 行为。</para>
///
/// <para>设计同时声明：<i>若</i>追加内容构成弱模式，强度档<i>可能</i>下降（MAY，非 SHALL），
/// 因此该反向分支不是可普适化的属性，不在本属性测试范围内（仅由 §11 边界单元测试覆盖）。</para>
///
/// **Validates: Requirements 8.9, 8.5**
/// </summary>
public sealed class PasswordStrengthMonotonicityPropertyTests
{
    // 覆盖四个字符类别（小写 / 大写 / 数字 / 符号）的字符生成器，使生成的口令能跨越
    // 多种长度与类别多样性，从而触及 Evaluate 的全部档位边界。
    private static readonly Gen<char> CharGen = Gen.OneOf(
        Gen.Char['a', 'z'],
        Gen.Char['A', 'Z'],
        Gen.Char['0', '9'],
        Gen.OneOfConst('!', '@', '#', '$', '%', '^', '&', '*', '-', '_', '=', '+'));

    // 原始密码 p：长度 0..24，覆盖空串、< 8 的硬下限区间，以及触发长度评分阈值（8/12/16）的区间。
    private static readonly Gen<string> BasePasswordGen =
        CharGen.Array[0, 24].Select(chars => new string(chars));

    // 追加串 s：长度 0..16，可为空（退化为 p+s == p，单调性以等号成立）。
    private static readonly Gen<string> AppendGen =
        CharGen.Array[0, 16].Select(chars => new string(chars));

    private static readonly Gen<(string Base, string Append)> CaseGen =
        from p in BasePasswordGen
        from s in AppendGen
        select (p, s);

    [Fact]
    public void Property1_appending_without_weak_pattern_does_not_lower_strength()
    {
        CaseGen
            // 条件性前提：仅保留「追加后不引入重复 / 可预测弱模式」的用例。p+s 无弱模式时，
            // 其前缀 p 亦无弱模式（弱模式子串在追加下只增不减），故两者惩罚分均为 0，
            // 单调性退化为「长度 + 类别」正向评分的单调性。
            .Where(c => !HasWeakPattern(c.Base + c.Append))
            .Sample(
                c =>
                {
                    string original = c.Base;
                    string extended = original + c.Append;

                    // 前提自检：追加场景下长度不减、字符类别集合不减少恒成立。
                    Assert.True(extended.Length >= original.Length);
                    Assert.True(CategoryMask(original) == (CategoryMask(original) & CategoryMask(extended)));

                    PasswordStrength before = PasswordStrengthEvaluator.Evaluate(original);
                    PasswordStrength after = PasswordStrengthEvaluator.Evaluate(extended);

                    // 核心断言：强度档不下降（PasswordStrength 枚举按强弱升序，可直接比较）。
                    Assert.True(
                        after >= before,
                        $"Evaluate('{extended}')={after} 应不低于 Evaluate('{original}')={before}");
                },
                iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 字符类别位掩码：bit0=小写, bit1=大写, bit2=数字, bit3=符号。用于在测试中确认
    /// 追加后类别集合不减少（原集合是新集合的子集）。
    /// </summary>
    private static int CategoryMask(string value)
    {
        int mask = 0;
        foreach (char ch in value)
        {
            if (char.IsLower(ch)) mask |= 1;
            else if (char.IsUpper(ch)) mask |= 2;
            else if (char.IsDigit(ch)) mask |= 4;
            else mask |= 8;
        }

        return mask;
    }

    /// <summary>
    /// 刻画 §9.2 <c>WeakPatternPenalty</c> 触发条件（penalty &gt; 0）：长重复串（同字符连续 ≥ 3 次）
    /// 或简单顺序模式（连续递增 / 递减 ≥ 4 个字符）。仅用于表达 Property 1 的「条件性」前提，
    /// 排除追加内容引入弱模式的用例，不复现具体惩罚分值。
    /// </summary>
    private static bool HasWeakPattern(string value)
    {
        if (value.Length < 3) return false;

        int run = 1, maxRun = 1;
        int seqUp = 1, seqDown = 1, maxSeq = 1;
        for (int i = 1; i < value.Length; i++)
        {
            run = value[i] == value[i - 1] ? run + 1 : 1;
            if (run > maxRun) maxRun = run;

            int delta = value[i] - value[i - 1];
            seqUp = delta == 1 ? seqUp + 1 : 1;
            seqDown = delta == -1 ? seqDown + 1 : 1;
            maxSeq = Math.Max(maxSeq, Math.Max(seqUp, seqDown));
        }

        return maxRun >= 3 || maxSeq >= 4;
    }
}
