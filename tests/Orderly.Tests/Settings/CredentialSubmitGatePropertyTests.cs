using System;
using System.Linq;
using CsCheck;
using Orderly.App.ViewModels;
using Orderly.App.ViewModels.Helpers;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for the credential submit gate (design §8.5 / §9.3 / §11 Property 2).
///
/// <para><b>Property 2: 提交门槛一致（强度非硬门槛）.</b>
/// 对任意输入：
/// <list type="bullet">
/// <item>主密码 <c>CanSubmit</c> ⟺（当前密码非空 ∧ 新密码长度 &gt;= 8 ∧ 两次输入一致），
/// 且新密码强度偏弱（<see cref="PasswordValidationState.IsStrengthWeak"/>）<b>绝不</b>影响
/// <see cref="PasswordValidationState.CanSubmit"/>；</item>
/// <item>PIN <c>CanSubmit</c> ⟺（当前 PIN 非空 ∧ 新 PIN 恰好 6 位纯数字 ∧ 两次输入一致）。</item>
/// </list></para>
///
/// <para>这两条等价关系是「门槛恰为三项硬条件之合取」的可普适化属性：通过在测试中<b>独立</b>
/// 重算期望门槛（不读取被测对象的 <c>CanSubmit</c>，而是直接从原始输入推导），再断言其与
/// <see cref="CredentialValidator"/> 产出状态的 <c>CanSubmit</c> 完全一致，即可证明强度从不参与
/// 主密码门槛、且 PIN 门槛严格等于「非空 + 6 位纯数字 + 一致」。</para>
///
/// **Validates: Requirements 8.2, 8.4, 8.1, 8.3, 8.10**
/// </summary>
public sealed class CredentialSubmitGatePropertyTests
{
    // 覆盖四个字符类别（小写 / 大写 / 数字 / 符号）的字符生成器，使新密码能跨越多种长度与
    // 类别多样性，从而触及 Evaluate 的全部强度档位（含长度达标但强度偏弱的用例）。
    private static readonly Gen<char> CharGen = Gen.OneOf(
        Gen.Char['a', 'z'],
        Gen.Char['A', 'Z'],
        Gen.Char['0', '9'],
        Gen.OneOfConst('!', '@', '#', '$', '%', '^', '&', '*', '-', '_', '=', '+'));

    // 当前密码：长度 0..12，含空串（覆盖「未提供当前密码」分支）。
    private static readonly Gen<string> CurrentPasswordGen =
        CharGen.Array[0, 12].Select(chars => new string(chars));

    // 新密码：长度 0..24，覆盖空串、< 8 的硬下限区间，以及 >= 8 的可提交区间。
    private static readonly Gen<string> NewPasswordGen =
        CharGen.Array[0, 24].Select(chars => new string(chars));

    // 任意确认串（用于制造不一致用例）。
    private static readonly Gen<string> ArbitraryGen =
        CharGen.Array[0, 24].Select(chars => new string(chars));

    private static readonly Gen<(string Current, string New, string Confirm)> MasterCaseGen =
        from current in CurrentPasswordGen
        from newPwd in NewPasswordGen
        from forceMatch in Gen.Bool
        from other in ArbitraryGen
        // forceMatch 时确认串等于新密码（覆盖一致分支）；否则取任意串（多为不一致，偶尔巧合一致）。
        select (current, newPwd, forceMatch ? newPwd : other);

    // PIN 字符生成器：数字占多数并混入非数字 / 空格，使新 PIN 能落在「恰好 6 位纯数字」内外两侧。
    private static readonly Gen<char> PinCharGen = Gen.OneOf(
        Gen.Char['0', '9'],
        Gen.Char['0', '9'],
        Gen.OneOfConst('a', 'X', ' ', '!', '９'));

    private static readonly Gen<string> CurrentPinGen =
        Gen.Char['0', '9'].Array[0, 6].Select(chars => new string(chars));

    // 新 PIN：长度 0..10，混合字符，覆盖位数不足 / 超长 / 含非数字 / 恰好 6 位纯数字。
    private static readonly Gen<string> NewPinGen =
        PinCharGen.Array[0, 10].Select(chars => new string(chars));

    private static readonly Gen<string> ArbitraryPinGen =
        PinCharGen.Array[0, 10].Select(chars => new string(chars));

    private static readonly Gen<(string Current, string New, string Confirm)> PinCaseGen =
        from current in CurrentPinGen
        from newPin in NewPinGen
        from forceMatch in Gen.Bool
        from other in ArbitraryPinGen
        select (current, newPin, forceMatch ? newPin : other);

    [Fact]
    public void Property2_master_password_can_submit_iff_current_length_and_match_and_strength_never_gates()
    {
        MasterCaseGen.Sample(
            c =>
            {
                PasswordValidationState state =
                    CredentialValidator.RecomputeMasterPasswordValidation(c.Current, c.New, c.Confirm);

                // 独立推导的三项硬门槛（不读取 state.CanSubmit，避免与被测实现循环论证）。
                bool expectedCurrentProvided = !string.IsNullOrEmpty(c.Current);
                bool expectedLengthValid = c.New.Length >= CredentialValidator.MinPasswordLength;
                bool expectedConfirmMatch =
                    !string.IsNullOrEmpty(c.New) && string.Equals(c.New, c.Confirm, StringComparison.Ordinal);
                bool expectedCanSubmit = expectedCurrentProvided && expectedLengthValid && expectedConfirmMatch;

                // 核心等价：CanSubmit ⟺ 当前非空 ∧ 新密码长度 >= 8 ∧ 两次一致。
                Assert.Equal(expectedCanSubmit, state.CanSubmit);

                // 强度非门槛：CanSubmit 必须恰等于「三项硬条件」，完全不含 IsStrengthWeak 项；
                // 因此即便强度偏弱（IsStrengthWeak=true），只要三项满足，CanSubmit 仍为真。
                Assert.Equal(
                    state.IsCurrentProvided && state.IsNewLengthValid && state.IsConfirmMatch,
                    state.CanSubmit);

                if (state.IsStrengthWeak)
                {
                    // 强度偏弱仅在长度达标时成立，且绝不下压 CanSubmit。
                    Assert.True(state.IsNewLengthValid);
                    Assert.Equal(
                        state.IsCurrentProvided && state.IsConfirmMatch,
                        state.CanSubmit);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property2_pin_can_submit_iff_current_and_six_digits_and_match()
    {
        PinCaseGen.Sample(
            c =>
            {
                PinValidationState state =
                    CredentialValidator.RecomputePinValidation(c.Current, c.New, c.Confirm);

                bool expectedCurrentProvided = !string.IsNullOrEmpty(c.Current);
                bool expectedDigitsValid =
                    c.New.Length == 6 && c.New.All(ch => ch >= '0' && ch <= '9');
                bool expectedConfirmMatch =
                    !string.IsNullOrEmpty(c.New) && string.Equals(c.New, c.Confirm, StringComparison.Ordinal);
                bool expectedCanSubmit = expectedCurrentProvided && expectedDigitsValid && expectedConfirmMatch;

                // 核心等价：CanSubmit ⟺ 当前 PIN 非空 ∧ 新 PIN 恰好 6 位纯数字 ∧ 两次一致。
                Assert.Equal(expectedCanSubmit, state.CanSubmit);

                // CanSubmit 恰为三项硬条件之合取，无任何额外门槛。
                Assert.Equal(
                    state.IsCurrentProvided && state.IsNewDigitsValid && state.IsConfirmMatch,
                    state.CanSubmit);
            },
            iter: PbtConfig.MinIterations);
    }
}
