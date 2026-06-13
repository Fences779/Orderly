using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using CsCheck;
using Orderly.App.ViewModels.Helpers;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsSaveErrorCode.MapToStableErrorCode"/> error-code
/// coverage (design §9.5 / §11 Property 6).
///
/// <para><b>Property 6: 错误码全覆盖.</b>
/// 对任意异常（含 <c>null</c>、任意 BCL 异常与任意自定义异常），
/// <see cref="SettingsSaveErrorCode.MapToStableErrorCode"/> 必映射到四个稳定短码之一
/// （<c>SET-1001</c> / <c>SET-1002</c> / <c>SET-1003</c> / <c>SET-1999</c>，Req 3.7），
/// 绝不返回 <c>null</c>、空串、空白串或任何其它值；且返回值仅为该稳定短码，
/// <b>绝不</b>泄露异常的类型名 / 消息 / 堆栈等内部细节（Req 3.4）。</para>
///
/// <para>该属性是「全覆盖 + 不泄露」不变式：无论输入异常落在显式领域异常、常见持久化 / IO 异常，
/// 还是任意未分类异常分支，映射都是全函数（total），其值域恒被四个短码闭合约束；同时由于返回值
/// 永远等于固定短码常量，它不可能携带生成异常中嵌入的敏感哨兵文本，从而保证不向 UI 文案泄露细节。</para>
///
/// **Validates: Requirements 3.7, 3.4**
/// </summary>
public sealed class SaveErrorCodeCoveragePropertyTests
{
    // 四个稳定短码的闭合值域（Req 3.7）：任何映射结果都必须落在其中。
    private static readonly HashSet<string> AllowedCodes = new(StringComparer.Ordinal)
    {
        SettingsSaveErrorCode.Persistence,  // SET-1001
        SettingsSaveErrorCode.Validation,   // SET-1002
        SettingsSaveErrorCode.Hotkey,       // SET-1003
        SettingsSaveErrorCode.Unknown,      // SET-1999
    };

    // 嵌入异常消息的敏感哨兵：用于验证返回的短码绝不回带异常消息文本（不泄露内部细节，Req 3.4）。
    private const string Sentinel = "LEAK_SENTINEL_a1b2c3d4_请勿泄露";

    // 任意异常消息生成器：覆盖空串、纯哨兵、含哨兵的随机串，确保消息内容多样且可检测泄露。
    private static readonly Gen<string> MessageGen = Gen.OneOf(
        Gen.Const(string.Empty),
        Gen.Const(Sentinel),
        Gen.String[0, 40].Select(s => Sentinel + s),
        Gen.String[0, 40].Select(s => s + Sentinel));

    /// <summary>
    /// 生成 (异常, 期望短码) 对：穷举映射的每个分支——四个显式 / 隐式归类到 SET-1001/1002/1003，
    /// 以及多种未分类 BCL / 自定义异常与 <c>null</c> 归类到 SET-1999。携带期望短码使属性同时验证
    /// 「映射全覆盖」与「映射正确」。
    /// </summary>
    private static readonly Gen<(Exception? Exception, string Expected)> CaseGen =
        MessageGen.SelectMany(msg => Gen.Int[0, 13].Select(kind => kind switch
        {
            // 显式领域异常（归类优先）。
            0 => ((Exception?)new SettingsHotkeyException(msg), SettingsSaveErrorCode.Hotkey),
            1 => (new SettingsValidationException(msg), SettingsSaveErrorCode.Validation),
            2 => (new SettingsPersistenceException(msg), SettingsSaveErrorCode.Persistence),

            // 常见持久化 / IO 失败 → SET-1001。
            3 => (new IOException(msg), SettingsSaveErrorCode.Persistence),
            4 => (new FileNotFoundException(msg), SettingsSaveErrorCode.Persistence),
            5 => (new UnauthorizedAccessException(msg), SettingsSaveErrorCode.Persistence),
            6 => (new FakeDbException(msg), SettingsSaveErrorCode.Persistence),

            // null 与任意未分类异常 → SET-1999。
            7 => (null, SettingsSaveErrorCode.Unknown),
            8 => (new Exception(msg), SettingsSaveErrorCode.Unknown),
            9 => (new InvalidOperationException(msg), SettingsSaveErrorCode.Unknown),
            10 => (new ArgumentException(msg), SettingsSaveErrorCode.Unknown),
            11 => (new TimeoutException(msg), SettingsSaveErrorCode.Unknown),
            12 => (new NotSupportedException(msg), SettingsSaveErrorCode.Unknown),

            // 带内部异常的包装异常仍属未分类 → SET-1999（不因 inner 改变归类）。
            _ => (new AggregateException(msg, new IOException(msg)), SettingsSaveErrorCode.Unknown),
        }));

    [Fact]
    public void Property6_maps_any_exception_to_one_of_four_stable_codes_without_leaking_details()
    {
        CaseGen.Sample(
            c =>
            {
                string code = SettingsSaveErrorCode.MapToStableErrorCode(c.Exception);

                // 全覆盖 / 非空（Req 3.7）：结果绝不为 null / 空 / 空白。
                Assert.False(
                    string.IsNullOrWhiteSpace(code),
                    "MapToStableErrorCode 返回了 null / 空 / 空白短码");

                // 值域闭合（Req 3.7）：结果必为四个稳定短码之一，绝无其它值。
                Assert.True(
                    AllowedCodes.Contains(code),
                    $"映射结果 '{code}' 不属于四个稳定短码 {{SET-1001, SET-1002, SET-1003, SET-1999}}");

                // 映射正确：与逐分支期望一致，证明每条失败路径都被确定地归类。
                Assert.Equal(c.Expected, code);

                // 不泄露细节（Req 3.4）：返回的短码绝不回带异常消息中的敏感哨兵文本。
                Assert.DoesNotContain(Sentinel, code, StringComparison.Ordinal);

                // 形态约束：稳定短码恒为 "SET-1001/1002/1003/1999" 之一，长度固定 8，进一步排除细节外泄。
                Assert.Equal(8, code.Length);
                Assert.StartsWith("SET-", code, StringComparison.Ordinal);
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 测试用具体 <see cref="DbException"/> 子类（DbException 为抽象类型），用于覆盖
    /// 「DbException → SET-1001」映射分支。
    /// </summary>
    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message) { }
    }
}
