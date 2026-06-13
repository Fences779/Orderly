using Orderly.Core.Models;

namespace Orderly.Core.Services;

/// <summary>
/// 统一安全审计服务抽象（需求 2.4 / design Property 4）。
///
/// 为安全敏感事件（认证失败、账户锁定、越权拒绝、凭证/密钥变更与重封装）提供统一、
/// 结构化、防篡改且不含明文敏感信息的审计写入能力。实现采用追加写 + 链式哈希，
/// 任意历史记录被篡改都可通过 <see cref="VerifyChainIntegrity"/> 检测。
///
/// 写入语义为"尽力而为"：审计写入不得改变调用方原有控制流与返回语义，
/// 故实现层与接入点均不应因审计失败而抛出异常或影响安全分支结果。
/// </summary>
public interface ISecurityAuditService
{
    /// <summary>
    /// 追加写入一条安全审计记录。
    /// </summary>
    /// <param name="eventType">安全敏感事件类型。</param>
    /// <param name="subject">主体标识（账户 ID / 用户名）。实现将对其做单向哈希后存储，绝不存储明文。可为 null。</param>
    /// <param name="outcome">事件处置结果。</param>
    /// <returns>新追加的、已计算链式哈希的审计记录。</returns>
    SecurityAuditRecord Record(SecurityEventType eventType, string? subject, SecurityEventOutcome outcome);

    /// <summary>返回当前审计链的只读快照（按追加顺序）。</summary>
    IReadOnlyList<SecurityAuditRecord> GetRecords();

    /// <summary>重新计算并校验整条审计链的完整性，链未被篡改时返回 true。</summary>
    bool VerifyChainIntegrity();

    /// <summary>
    /// 异步写入接缝（BC-6 / BC-14，需求 12.5）。供认证 / 账户服务层在登录、账户锁定、凭证变更、
    /// 成员创建/重置/停用/删除等安全敏感事件发生时调用，恰好记录一条对应类型的审计记录。
    ///
    /// 安全约束（P0 / P4）：绝不接收 / 记录明文凭证（密码 / PIN 原文）；仅接收事件类型、账号标签
    /// 与已脱敏的 <paramref name="detail"/>。记录写入加密本地存储并保持防篡改（持久化实现见任务 11.3）。
    /// 写入语义为「尽力而为」，不得改变调用方原有控制流。
    /// </summary>
    /// <param name="kind">安全审计事件类型。</param>
    /// <param name="accountLabel">账号标签 / 标识，用于归属展示；不含明文凭证。</param>
    /// <param name="detail">已脱敏的事件详情说明；绝不含明文凭证。</param>
    /// <param name="ct">取消令牌。</param>
    Task RecordAsync(SecurityAuditEventKind kind, string accountLabel, string detail, CancellationToken ct = default);

    /// <summary>
    /// 异步读取 API（BC-6 / BC-14，需求 12.5 / 9.7）。供我的页「账户安全 / 登录记录」卡片拉取审计列表。
    ///
    /// 按账号标签与时间范围（<paramref name="from"/> / <paramref name="to"/>，含边界，null 表示该侧不限）
    /// 筛选，返回顺序稳定的 <see cref="SecurityAuditEntry"/> 列表；仅返回落在范围内的子集，
    /// 空结果返回空列表而非抛出异常。全量保留：不对历史记录截断或自动清除（日期范围读取实现见任务 11.4）。
    /// </summary>
    /// <param name="accountLabel">账号标签筛选；null 表示不限账号。</param>
    /// <param name="from">时间范围下界（含），null 表示不限下界。</param>
    /// <param name="to">时间范围上界（含），null 表示不限上界。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>顺序稳定的安全审计条目列表；无匹配时为空列表。</returns>
    Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(
        string? accountLabel = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
