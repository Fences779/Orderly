namespace Orderly.Core.Models;

/// <summary>
/// 安全敏感事件的种类（对应需求 2.4 / design Property 4 所列举的安全敏感事件）。
/// </summary>
public enum SecurityEventType
{
    /// <summary>认证失败（主密码 / PIN / 恢复密钥校验失败等）。</summary>
    AuthenticationFailure = 0,

    /// <summary>账户锁定（失败次数超过阈值进入冷却）。</summary>
    AccountLockout = 1,

    /// <summary>越权拒绝（非 Owner 尝试执行受限操作、跨账户操作等）。</summary>
    AuthorizationDenied = 2,

    /// <summary>凭证变更（主密码 / PIN 修改或重置）。</summary>
    CredentialChange = 3,

    /// <summary>密钥变更与重封装（数据密钥使用新凭证重新封装）。</summary>
    KeyRewrap = 4,
}

/// <summary>
/// 安全敏感事件的处置结果。
/// </summary>
public enum SecurityEventOutcome
{
    /// <summary>操作失败（如凭证校验未通过）。</summary>
    Failure = 0,

    /// <summary>操作被拒绝（如越权访问被门控阻断）。</summary>
    Denied = 1,

    /// <summary>主体已被锁定（频率限制 / 失败锁定触发）。</summary>
    Locked = 2,

    /// <summary>操作成功（如凭证变更、密钥重封装完成）。</summary>
    Success = 3,
}

/// <summary>
/// 统一、结构化、防篡改的安全审计记录（需求 2.4 / design Property 4）。
///
/// 设计要点：
/// - 不含任何明文敏感信息：主体仅以单向哈希（<see cref="SubjectIdentifier"/>）表示，
///   绝不存储主密码 / PIN / 恢复密钥等明文凭证。
/// - 防篡改：采用追加写 + 链式哈希。每条记录通过 <see cref="PreviousHash"/> 链接上一条记录的
///   <see cref="RecordHash"/>，任意历史记录被篡改都会破坏其后所有记录的哈希链，便于检测。
/// </summary>
public sealed class SecurityAuditRecord
{
    /// <summary>链中的单调递增序号（从 0 起），用于固定记录顺序。</summary>
    public long Sequence { get; init; }

    /// <summary>事件类型（认证失败、锁定、越权拒绝、凭证变更、密钥重封装）。</summary>
    public SecurityEventType EventType { get; init; }

    /// <summary>事件发生时间（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// 主体标识的单向哈希（账户 ID / 用户名经哈希后得到），不含明文。
    /// 用于在不泄露身份明文的前提下关联同一主体的多次事件。
    /// </summary>
    public string SubjectIdentifier { get; init; } = string.Empty;

    /// <summary>事件处置结果。</summary>
    public SecurityEventOutcome Outcome { get; init; }

    /// <summary>链式哈希：上一条记录的 <see cref="RecordHash"/>（首条为创世哈希）。</summary>
    public string PreviousHash { get; init; } = string.Empty;

    /// <summary>本条记录的完整性哈希，覆盖本条全部字段与 <see cref="PreviousHash"/>。</summary>
    public string RecordHash { get; init; } = string.Empty;
}
