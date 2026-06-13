using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="IEmergencyEnableService"/> 的实现（需求 17.1 / 17.2 / 17.5 / 13.2，design §9.7 / Property 14）。
///
/// 紧急启用流程（与 design §9.7 伪代码一致）：
/// <list type="number">
///   <item>前置校验：目标账号存在、角色为 <see cref="LocalAccountRole.Owner"/> 且 <c>IsEnabled == false</c>；不满足则拒绝。</item>
///   <item>复用既有 <see cref="ILocalAuthService.VerifyPinAsync"/> 校验 6 位 PIN。</item>
///   <item>PIN 正确 → 经 <see cref="ISessionContextService.SetPermissionMode"/> 进入受限权限模式，记 <see cref="SecurityAuditEventKind.LoginSucceeded"/> 审计；返回成功。</item>
///   <item>PIN 不正确 → 记 <see cref="SecurityAuditEventKind.LoginFailed"/> 审计，返回中文错误提示「PIN 不正确，无法紧急启用」，不进入受限模式。</item>
/// </list>
///
/// 安全约束（P0 / P4）：审计 detail 仅含事件元数据，绝不含明文 PIN；明文 PIN 仅透传给既有校验通道，
/// 本接缝不缓存、不写日志、不延长其生命周期。审计写入为「尽力而为」，绝不改变本方法控制流与返回语义。
/// </summary>
public sealed class EmergencyEnableService : IEmergencyEnableService
{
    /// <summary>PIN 不正确时的中文错误提示（需求 17.2）。</summary>
    internal const string PinRejectedMessage = "PIN 不正确，无法紧急启用";

    /// <summary>前置条件不满足（非 Owner / 账号未停用 / 账号不存在）时的中文错误提示（需求 17.2）。</summary>
    internal const string PreconditionFailedMessage = "当前账号不满足紧急启用条件，无法紧急启用";

    private readonly ILocalAccountRepository _accountRepository;
    private readonly ILocalAuthService _authService;
    private readonly ISessionContextService _sessionContextService;
    private readonly ISecurityAuditService _securityAudit;

    public EmergencyEnableService(
        ILocalAccountRepository accountRepository,
        ILocalAuthService authService,
        ISessionContextService sessionContextService,
        ISecurityAuditService securityAudit)
    {
        _accountRepository = accountRepository
            ?? throw new ArgumentNullException(nameof(accountRepository));
        _authService = authService
            ?? throw new ArgumentNullException(nameof(authService));
        _sessionContextService = sessionContextService
            ?? throw new ArgumentNullException(nameof(sessionContextService));
        _securityAudit = securityAudit
            ?? throw new ArgumentNullException(nameof(securityAudit));
    }

    public async Task<EmergencyEnableResult> TryEmergencyEnableAsync(
        string ownerAccountId,
        string enteredPin,
        CancellationToken cancellationToken = default)
    {
        // 前置校验：账号存在、为 Owner 且处于被停用状态（IsEnabled == false）。
        var account = string.IsNullOrWhiteSpace(ownerAccountId)
            ? null
            : await _accountRepository.GetByAccountIdAsync(ownerAccountId, cancellationToken);

        if (account is null
            || account.Role != LocalAccountRole.Owner
            || account.IsEnabled)
        {
            // 前置不满足：拒绝，不进入受限模式；记审计（不含明文 PIN）。
            await TryAuditAsync(
                SecurityAuditEventKind.LoginFailed,
                ownerAccountId,
                "emergency-enable-precondition-failed",
                cancellationToken);
            return EmergencyEnableResult.Failure(PreconditionFailedMessage);
        }

        // 复用既有 PIN 校验通道；明文 PIN 仅透传，不在本接缝缓存 / 记录。
        var pinValid = await _authService.VerifyPinAsync(account.AccountId, enteredPin, cancellationToken);

        if (!pinValid)
        {
            // 需求 17.2 / 17.5：PIN 错误 → 拒绝，记 LoginFailed 审计（不含明文 PIN），返回中文提示。
            await TryAuditAsync(
                SecurityAuditEventKind.LoginFailed,
                account.AccountId,
                "emergency-enable-pin-failed",
                cancellationToken);
            return EmergencyEnableResult.Failure(PinRejectedMessage);
        }

        // 需求 17.1：PIN 正确 → 进入受限权限模式。
        _sessionContextService.SetPermissionMode(SessionPermissionMode.Restricted_Permission);

        // 需求 17.5：成功亦记审计（仅元数据，绝不含明文 PIN）。
        await TryAuditAsync(
            SecurityAuditEventKind.LoginSucceeded,
            account.AccountId,
            "emergency-enable-restricted",
            cancellationToken);

        return EmergencyEnableResult.Success();
    }

    // 审计写入为「尽力而为」：绝不改变紧急启用控制流与返回语义，任何异常都被吞掉。
    private async Task TryAuditAsync(
        SecurityAuditEventKind kind,
        string accountLabel,
        string detail,
        CancellationToken cancellationToken)
    {
        try
        {
            await _securityAudit.RecordAsync(kind, accountLabel ?? string.Empty, detail, cancellationToken);
        }
        catch
        {
            // 审计失败不得影响紧急启用结果。
        }
    }
}
