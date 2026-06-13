using System;
using System.Data.Common;
using System.IO;
using System.Security;
using Orderly.App.ViewModels.Helpers;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 明确示例单元测试：验证 <see cref="SettingsSaveErrorCode.MapToStableErrorCode"/> 的逐类归类
/// （设计 §9.5 / §11；Req 3.7）。
///
/// <para>归类口径：</para>
/// <list type="bullet">
/// <item>持久化 / IO / DB 失败（<see cref="IOException"/>、<see cref="UnauthorizedAccessException"/>、
/// <see cref="DbException"/>、<see cref="SettingsPersistenceException"/>）→ <c>SET-1001</c>。</item>
/// <item>输入校验未过（<see cref="SettingsValidationException"/>）→ <c>SET-1002</c>。</item>
/// <item>热键应用失败（<see cref="SettingsHotkeyException"/>）→ <c>SET-1003</c>。</item>
/// <item>其它未分类异常与 <c>null</c>（<see cref="InvalidOperationException"/> 等）→ <c>SET-1999</c>。</item>
/// </list>
///
/// **Validates: Requirements 3.7**
/// </summary>
public sealed class SaveErrorCodeMappingTests
{
    /// <summary>
    /// 持久化 / IO / DB 类异常与显式持久化领域异常一律归 <c>SET-1001</c>。
    /// 包含 <see cref="IOException"/> 子类（<see cref="FileNotFoundException"/>）与
    /// <see cref="DbException"/> 子类，验证归类按异常基类型而非精确类型匹配。
    /// </summary>
    [Theory]
    [MemberData(nameof(PersistenceCases))]
    public void MapToStableErrorCode_persistence_and_io_and_db_map_to_SET_1001(Exception exception)
    {
        Assert.Equal(SettingsSaveErrorCode.Persistence, SettingsSaveErrorCode.MapToStableErrorCode(exception));
        Assert.Equal("SET-1001", SettingsSaveErrorCode.MapToStableErrorCode(exception));
    }

    public static TheoryData<Exception> PersistenceCases() => new()
    {
        new IOException("disk full"),
        new FileNotFoundException("missing file"),
        new UnauthorizedAccessException("denied"),
        new FakeDbException("db write failed"),
        new SettingsPersistenceException("persist failed"),
    };

    /// <summary>校验未过的显式领域异常归 <c>SET-1002</c>。</summary>
    [Fact]
    public void MapToStableErrorCode_validation_maps_to_SET_1002()
    {
        var exception = new SettingsValidationException("invalid input");

        Assert.Equal(SettingsSaveErrorCode.Validation, SettingsSaveErrorCode.MapToStableErrorCode(exception));
        Assert.Equal("SET-1002", SettingsSaveErrorCode.MapToStableErrorCode(exception));
    }

    /// <summary>热键应用失败的显式领域异常归 <c>SET-1003</c>。</summary>
    [Fact]
    public void MapToStableErrorCode_hotkey_maps_to_SET_1003()
    {
        var exception = new SettingsHotkeyException("hotkey conflict");

        Assert.Equal(SettingsSaveErrorCode.Hotkey, SettingsSaveErrorCode.MapToStableErrorCode(exception));
        Assert.Equal("SET-1003", SettingsSaveErrorCode.MapToStableErrorCode(exception));
    }

    /// <summary>
    /// 其它未分类异常一律归 <c>SET-1999</c>，覆盖任务要求的 <see cref="InvalidOperationException"/>
    /// 以及多种常见 BCL 异常，确保归类为「全函数」且不漏归到前三类短码。
    /// </summary>
    [Theory]
    [MemberData(nameof(UnknownCases))]
    public void MapToStableErrorCode_other_exceptions_map_to_SET_1999(Exception exception)
    {
        Assert.Equal(SettingsSaveErrorCode.Unknown, SettingsSaveErrorCode.MapToStableErrorCode(exception));
        Assert.Equal("SET-1999", SettingsSaveErrorCode.MapToStableErrorCode(exception));
    }

    public static TheoryData<Exception> UnknownCases() => new()
    {
        new InvalidOperationException("bad state"),
        new Exception("generic"),
        new ArgumentException("bad arg"),
        new ArgumentNullException("param"),
        new TimeoutException("timed out"),
        new NotSupportedException("nope"),
        new SecurityException("blocked"),
        new AggregateException("wrapped", new IOException("inner io")),
    };

    /// <summary>
    /// <c>null</c> 异常（无具体异常信息的失败路径）归 <c>SET-1999</c>，
    /// 证明映射对 <c>null</c> 输入仍是全函数、不抛异常。
    /// </summary>
    [Fact]
    public void MapToStableErrorCode_null_maps_to_SET_1999()
    {
        Assert.Equal(SettingsSaveErrorCode.Unknown, SettingsSaveErrorCode.MapToStableErrorCode(null));
        Assert.Equal("SET-1999", SettingsSaveErrorCode.MapToStableErrorCode(null));
    }

    /// <summary>
    /// 测试用具体 <see cref="DbException"/> 子类（<see cref="DbException"/> 为抽象类型），
    /// 用于覆盖「DbException → SET-1001」归类分支。
    /// </summary>
    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string message) : base(message) { }
    }
}
