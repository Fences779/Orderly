using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="ICredentialChangeSessionCoordinator"/> 的实现（design §9.6 / Req 16 / Property 13）。
///
/// 会话后置状态由（凭证种类, 是否成功）唯一决定，复用既有 <see cref="ISessionLockService"/>
/// 手动锁定与 <see cref="ISessionContextService"/> 会话上下文机制：
/// <list type="bullet">
///   <item>失败 / 取消 → 直接返回，不触碰会话锁定与会话上下文（Req 16.3）。</item>
///   <item>成功 → 先记审计（仅元数据，不含明文凭证），再做会话转移。</item>
///   <item>主密码成功 → 强制登出（清空会话上下文 + 锁定服务登出），要求重新登录（Req 16.1）。</item>
///   <item>PIN 成功 → <see cref="ISessionLockService.LockManually"/> 进入 PendingPinUnlock（Req 16.2）。</item>
/// </list>
/// </summary>
public sealed class CredentialChangeSessionCoordinator : ICredentialChangeSessionCoordinator
{
    private readonly ISessionLockService _sessionLockService;
    private readonly ISessionContextService _sessionContextService;
    private readonly ISecurityAuditService _securityAudit;

    public CredentialChangeSessionCoordinator(
        ISessionLockService sessionLockService,
        ISessionContextService sessionContextService,
        ISecurityAuditService securityAudit)
    {
        _sessionLockService = sessionLockService
            ?? throw new ArgumentNullException(nameof(sessionLockService));
        _sessionContextService = sessionContextService
            ?? throw new ArgumentNullException(nameof(sessionContextService));
        _securityAudit = securityAudit
            ?? throw new ArgumentNullException(nameof(securityAudit));
    }

    public void OnCredentialChangeCompleted(CredentialChangeKind kind, CredentialChangeResult result)
    {
        // Req 16.3：失败或取消 → 会话状态保持不变，既不登出也不锁定。
        if (result != CredentialChangeResult.Success)
        {
            return;
        }

        // 成功路径：先记审计（仅事件元数据，绝不含明文密码 / PIN —— Req 16.4 / P4）。
        // 主体在会话转移前捕获，便于关联当前账号；审计写入失败不得影响会话转移。
        var subject = _sessionContextService.Current?.AccountId;
        TryAudit(subject);

        switch (kind)
        {
            case CredentialChangeKind.MasterPassword:
                // Req 16.1：主密码改后强制登出，要求用新主密码重新登录。
                // 复用既有登出语义：锁定服务登出 + 清空会话上下文（与 App 登出流程一致）。
                _sessionLockService.Logout();
                _sessionContextService.Clear();
                break;

            case CredentialChangeKind.Pin:
                // Req 16.2：PIN 改后锁定进入 PendingPinUnlock（复用 LockManually），不强制登出。
                _sessionLockService.LockManually();
                break;
        }
    }

    // 安全审计写入为"尽力而为"：绝不改变既有控制流与返回语义，任何异常都被吞掉。
    private void TryAudit(string? subject)
    {
        try
        {
            _securityAudit.Record(SecurityEventType.CredentialChange, subject, SecurityEventOutcome.Success);
        }
        catch
        {
            // 审计失败不得影响会话转移行为。
        }
    }
}
