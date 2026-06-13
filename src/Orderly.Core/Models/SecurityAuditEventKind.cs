namespace Orderly.Core.Models;

/// <summary>
/// 安全 / 认证审计事件类型（BC-6.1）。覆盖登录、账户锁定、凭证变更与成员账号管理等安全敏感事件。
///
/// 该枚举独立于业务活动日志 <see cref="ActivityType"/>（<c>ActivityLog.ActivityType</c>），
/// 避免污染业务日志语义；其写入接缝位于认证 / 账户服务层，绝不记录明文凭证（需求 12.1 / design §6.4、§10.2）。
/// </summary>
public enum SecurityAuditEventKind
{
    /// <summary>登录成功。</summary>
    LoginSucceeded,

    /// <summary>登录失败（凭证校验未通过）。</summary>
    LoginFailed,

    /// <summary>账户锁定（失败次数超过阈值进入冷却）。</summary>
    AccountLockedOut,

    /// <summary>凭证变更（主密码 / PIN 修改或重置）。仅记录元数据，绝不含明文。</summary>
    CredentialChanged,

    /// <summary>创建成员账号。</summary>
    MemberCreated,

    /// <summary>重置成员密码。</summary>
    MemberPasswordReset,

    /// <summary>停用成员（仅置 <c>IsEnabled=false</c>，保留账号，可重新启用）。</summary>
    MemberDisabled,

    /// <summary>删除成员（移除登录账号本身），区别于 <see cref="MemberDisabled"/>（仅置禁用）。</summary>
    MemberDeleted
}
