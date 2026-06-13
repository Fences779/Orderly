using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CsCheck;
using Orderly.App.ViewModels.Helpers;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsSaveErrorCode.BuildFailureToastMessage"/>
/// failure-toast composition (design §9.5 / §11 Property 11).
///
/// <para><b>Property 11: 失败提示含人话说明与错误码.</b>
/// 对任意错误码（四个稳定短码 <c>SET-1001/1002/1003/1999</c>、<c>null</c>、空 / 空白串，
/// 以及任意非法 / 未识别串），<see cref="SettingsSaveErrorCode.BuildFailureToastMessage"/>
/// 返回的文案必<b>同时</b>含：(a) 一段非空、面向普通用户的中文「人话」说明主体（位于错误码括注之前），
/// 以及 (b) 一个 <c>「（错误码：…）」</c> 括注（Req 3.4）；其中四码与 <c>null</c> / 空白输入的括注
/// 必含一个稳定短码 <c>SET-xxxx</c>（Req 3.7）。文案<b>绝不</b>把异常类型名 / 消息 / 堆栈等内部
/// 细节带入人话说明主体——即使调用方把含「内部细节」哨兵的串当作错误码传入，人话说明主体仍保持
/// 干净、与输入无关（Req 3.4）。</para>
///
/// <para>该属性是「人话 + 短码 + 不泄露」不变式：无论错误码落在哪个分支，文案恒由
/// 「{固定的人话说明}（错误码：{短码}）」两段组成；人话说明取自四条固定中文文案之一，
/// 与传入错误码内容解耦，因此不可能回带调用方注入的敏感哨兵文本；而括注始终存在并对四码 /
/// 空白输入坍缩到稳定 <c>SET-xxxx</c> 短码，保证用户既看得懂又能报排查码。</para>
///
/// **Validates: Requirements 3.4, 3.7**
/// </summary>
public sealed class SaveFailureToastMessagePropertyTests
{
    // 四个稳定短码的闭合值域（Req 3.7）：四码 / null / 空白输入的括注必落在其中。
    private static readonly HashSet<string> StableCodes = new(StringComparer.Ordinal)
    {
        SettingsSaveErrorCode.Persistence,  // SET-1001
        SettingsSaveErrorCode.Validation,   // SET-1002
        SettingsSaveErrorCode.Hotkey,       // SET-1003
        SettingsSaveErrorCode.Unknown,      // SET-1999
    };

    // 错误码括注的固定前缀；人话说明主体即此前缀之前的全部文本。
    private const string ParenPrefix = "（错误码：";

    // 匹配任一稳定短码 SET-1001/1002/1003/1999。
    private static readonly Regex StableCodePattern =
        new(@"SET-(1001|1002|1003|1999)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 「内部细节」哨兵：模拟异常类型名 / 堆栈等绝不应出现在人话说明主体中的敏感文本。
    private const string Sentinel = "System.IO.IOException: LEAK_at_StackTrace_请勿泄露";

    /// <summary>
    /// 输入错误码及其期望：<see cref="ExpectsStableCode"/> 表示括注是否必含稳定 <c>SET-xxxx</c>，
    /// <see cref="HasSentinel"/> 表示该输入是否含哨兵（用于验证人话主体不回带内部细节）。
    /// </summary>
    private readonly record struct Case(string? Input, bool ExpectsStableCode, bool HasSentinel);

    private static readonly Gen<Case> CaseGen =
        Gen.OneOf(
            // 四个稳定短码：括注必含对应 SET-xxxx。
            Gen.Const(new Case(SettingsSaveErrorCode.Persistence, true, false)),
            Gen.Const(new Case(SettingsSaveErrorCode.Validation, true, false)),
            Gen.Const(new Case(SettingsSaveErrorCode.Hotkey, true, false)),
            Gen.Const(new Case(SettingsSaveErrorCode.Unknown, true, false)),

            // null / 空 / 空白：回退到 SET-1999，括注仍含稳定短码。
            Gen.Const(new Case(null, true, false)),
            Gen.Const(new Case(string.Empty, true, false)),
            Gen.Const(new Case("   ", true, false)),
            Gen.Const(new Case("\t\r\n", true, false)),

            // 任意未识别串：人话主体仍非空且不泄露，括注存在（但未必为稳定短码）。
            Gen.String[0, 40].Select(s => new Case(s, false, false)),

            // 含「内部细节」哨兵的串当作错误码传入：人话说明主体必须保持干净、不回带哨兵。
            Gen.OneOf(
                Gen.Const(Sentinel),
                Gen.String[0, 20].Select(s => s + Sentinel),
                Gen.String[0, 20].Select(s => Sentinel + s))
                .Select(s => new Case(s, false, true)));

    [Fact]
    public void Property11_failure_toast_always_has_human_body_and_stable_error_code_without_leaking_details()
    {
        CaseGen.Sample(
            c =>
            {
                string message = SettingsSaveErrorCode.BuildFailureToastMessage(c.Input);

                // 文案非空（Req 3.4）。
                Assert.False(
                    string.IsNullOrWhiteSpace(message),
                    "BuildFailureToastMessage 返回了 null / 空 / 空白文案");

                // 必含错误码括注：形如「…（错误码：{code}）」（Req 3.4）。
                Assert.Contains(ParenPrefix, message, StringComparison.Ordinal);
                Assert.EndsWith("）", message, StringComparison.Ordinal);

                // (a) 人话说明主体 = 括注之前的文本，必非空、非空白，且自身不含「错误码」标记，
                // 证明说明主体与括注是相互独立的两段（Req 3.4）。
                int parenIndex = message.IndexOf(ParenPrefix, StringComparison.Ordinal);
                Assert.True(parenIndex > 0, "人话说明主体缺失：错误码括注之前没有任何说明文本");

                string body = message[..parenIndex];
                Assert.False(
                    string.IsNullOrWhiteSpace(body),
                    "人话说明主体为空 / 空白");
                Assert.DoesNotContain("错误码", body, StringComparison.Ordinal);

                // 不泄露内部细节（Req 3.4）：即使调用方把含异常类型名 / 堆栈哨兵的串当作错误码传入，
                // 人话说明主体仍与输入解耦、绝不回带哨兵。
                Assert.DoesNotContain(Sentinel, body, StringComparison.Ordinal);
                if (c.HasSentinel)
                {
                    Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
                    Assert.DoesNotContain("StackTrace", body, StringComparison.Ordinal);
                }

                // 括注内容 = ParenPrefix 与结尾「）」之间的文本。
                string parenBody = message[(parenIndex + ParenPrefix.Length)..^1];

                // (b) 稳定错误码（Req 3.7）：四码与 null / 空白输入的括注必含一个 SET-xxxx 稳定短码。
                if (c.ExpectsStableCode)
                {
                    Assert.Matches(StableCodePattern, message);
                    Assert.Contains(parenBody, StableCodes);
                }

                // 四码输入：括注必原样回带该稳定短码。
                if (c.Input is not null && StableCodes.Contains(c.Input))
                {
                    Assert.Equal(c.Input, parenBody);
                }

                // null / 空白输入：括注坍缩到默认稳定短码 SET-1999。
                if (string.IsNullOrWhiteSpace(c.Input))
                {
                    Assert.Equal(SettingsSaveErrorCode.Unknown, parenBody);
                }
            },
            iter: PbtConfig.MinIterations);
    }
}
