using System;
using System.Data.Common;
using System.IO;

namespace Orderly.App.ViewModels.Helpers;

/// <summary>
/// 离开设置页时聚合的「最近一次保存结果」轻量记录（设计 §9.5 / Req 3.7）。
/// 由自动保存引擎在成功 / 异常路径写入：成功时 <see cref="ErrorCode"/> 为 <c>null</c>，
/// 失败时填充稳定短码（<see cref="SettingsSaveErrorCode"/> 之一）。
/// 仅承载结果元数据，不携带任何异常类型名 / 堆栈，避免向 UI 文案泄露内部细节（P4）。
/// </summary>
/// <param name="Success">本次保存是否成功。</param>
/// <param name="ErrorCode">失败时的稳定错误短码；成功时为 <c>null</c>。</param>
/// <param name="At">结果产生时间。</param>
public sealed record SettingsSaveOutcome(bool Success, string? ErrorCode, DateTime At)
{
    /// <summary>构造一次成功结果（无错误码）。</summary>
    public static SettingsSaveOutcome FromSuccess(DateTime at) => new(true, null, at);

    /// <summary>
    /// 由异常构造一次失败结果，错误码经 <see cref="SettingsSaveErrorCode.MapToStableErrorCode"/>
    /// 归类，绝不把异常类型名 / 堆栈写入结果。
    /// </summary>
    public static SettingsSaveOutcome FromFailure(Exception? exception, DateTime at) =>
        new(false, SettingsSaveErrorCode.MapToStableErrorCode(exception), at);
}

/// <summary>
/// 设置保存稳定错误码与异常 → 错误码的归类映射（设计 §9.5 / Req 3.7、3.4）。
///
/// <para>映射只产出对外稳定的短码，<b>绝不</b>把异常类型名或堆栈等内部细节泄露到 UI 文案；
/// 失败 Toast 的人话文案拼装由本类型的 <see cref="BuildFailureToastMessage"/> 负责，
/// 同样只暴露「人话说明 + 短码」。</para>
/// </summary>
public static class SettingsSaveErrorCode
{
    /// <summary>持久化写入失败（IO / DB 异常）。</summary>
    public const string Persistence = "SET-1001";

    /// <summary>输入校验未通过导致未保存。</summary>
    public const string Validation = "SET-1002";

    /// <summary>热键应用失败回滚。</summary>
    public const string Hotkey = "SET-1003";

    /// <summary>其它未分类异常。</summary>
    public const string Unknown = "SET-1999";

    /// <summary>持久化失败（SET-1001）面向普通用户的中文「人话」说明（设计 §9.5）。</summary>
    private const string PersistenceText = "设置没能保存，可能是磁盘空间或读写出了问题，请稍后重试";

    /// <summary>校验未过（SET-1002）面向普通用户的中文「人话」说明（设计 §9.5）。</summary>
    private const string ValidationText = "有设置项填写不正确，暂时没能保存，请检查后重试";

    /// <summary>热键冲突（SET-1003）面向普通用户的中文「人话」说明（设计 §9.5）。</summary>
    private const string HotkeyText = "快捷键和其它程序冲突了，这项设置没能保存，请换一组按键";

    /// <summary>其它未分类（SET-1999）面向普通用户的中文「人话」说明（设计 §9.5）。</summary>
    private const string UnknownText = "设置保存失败了，请稍后重试";

    /// <summary>
    /// 按稳定错误码拼装失败 Toast 文案：以面向普通用户的中文「人话」说明为主体，
    /// 稳定错误码以括注形式辅助附带，最终形如「{人话说明}（错误码：{errorCode}）」
    /// （设计 §9.5 / Req 3.4 / Property 11）。
    ///
    /// <para>文案<b>只</b>包含人话说明与短码，绝不携带任何内部异常类型名或堆栈细节（P4）。
    /// 未识别或 <c>null</c> 的错误码回退到 <see cref="Unknown"/>（SET-1999）的通俗说明，
    /// 括注中仍原样附带传入的错误码以便排查。</para>
    /// </summary>
    /// <param name="errorCode">稳定错误短码（<see cref="Persistence"/> / <see cref="Validation"/> /
    /// <see cref="Hotkey"/> / <see cref="Unknown"/> 之一）。</param>
    /// <returns>拼装后的失败提示文案。</returns>
    public static string BuildFailureToastMessage(string? errorCode)
    {
        var plain = errorCode switch
        {
            Persistence => PersistenceText,
            Validation => ValidationText,
            Hotkey => HotkeyText,
            // 其它未分类异常，以及未识别 / null 错误码，统一回退到通用说明。
            _ => UnknownText
        };

        // 未识别 / null 错误码时，括注回落到 SET-1999，保证文案始终含一个稳定短码。
        var code = string.IsNullOrWhiteSpace(errorCode) ? Unknown : errorCode;

        return $"{plain}（错误码：{code}）";
    }

    /// <summary>
    /// 将保存失败异常归类为稳定短码：持久化失败 → <see cref="Persistence"/>、校验未过 →
    /// <see cref="Validation"/>、热键失败 → <see cref="Hotkey"/>、其它（含 <c>null</c>）→
    /// <see cref="Unknown"/>。
    ///
    /// <para>任何输入都能映射到四个短码之一（无未分类裸异常泄露到 UI 文案，Property 6）；
    /// 归类只依据异常类别，不读取也不返回异常消息 / 类型名 / 堆栈。</para>
    /// </summary>
    public static string MapToStableErrorCode(Exception? exception)
    {
        return exception switch
        {
            // 显式领域异常优先归类，使映射确定且可测试。
            SettingsHotkeyException => Hotkey,
            SettingsValidationException => Validation,
            SettingsPersistenceException => Persistence,

            // 常见持久化 / IO 失败归入 SET-1001。
            IOException => Persistence,
            UnauthorizedAccessException => Persistence,
            DbException => Persistence,

            // 其它未分类异常（含 null）一律 SET-1999，绝不泄露细节。
            _ => Unknown
        };
    }
}

/// <summary>
/// 设置持久化写入失败（IO / DB），归类为 <see cref="SettingsSaveErrorCode.Persistence"/>（SET-1001）。
/// </summary>
public sealed class SettingsPersistenceException : Exception
{
    public SettingsPersistenceException(string message) : base(message) { }

    public SettingsPersistenceException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// 设置输入校验未通过，归类为 <see cref="SettingsSaveErrorCode.Validation"/>（SET-1002）。
/// </summary>
public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(string message) : base(message) { }

    public SettingsValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// 热键应用失败回滚，归类为 <see cref="SettingsSaveErrorCode.Hotkey"/>（SET-1003）。
/// </summary>
public sealed class SettingsHotkeyException : Exception
{
    public SettingsHotkeyException(string message) : base(message) { }

    public SettingsHotkeyException(string message, Exception innerException)
        : base(message, innerException) { }
}
