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
}
