using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="ISensitivePageGuard"/> 的实现（需求 18.1~18.5 / 17.4 / 13.3，design §9.8 / Property 14）。
///
/// 判定顺序（与 design §9.8 伪代码一致）：
/// <list type="number">
///   <item>受限模式短路：<see cref="ISessionContextService.IsRestrictedPermissionMode"/> 为真 → <see cref="SensitiveAccessResult.BlockedByRestricted"/>，先于 PIN 校验（需求 18.4 / 17.4）。</item>
///   <item>复用既有 <see cref="ILocalAuthService.VerifyPinAsync"/> 校验当前会话账号的 6 位 PIN：正确 → <see cref="SensitiveAccessResult.Granted"/>；错误或无登录账号 → <see cref="SensitiveAccessResult.PinRejected"/>（需求 18.1 / 18.2）。</item>
/// </list>
///
/// 未通过验证时（<see cref="SensitiveAccessResult.PinRejected"/> / <see cref="SensitiveAccessResult.BlockedByRestricted"/>）
/// 由调用方保证机密内容恒不渲染（需求 18.3）。会话锁定（<c>PendingPinUnlock</c>）的先解锁协同由调用方处理（UI 19.x）。
///
/// 安全约束（P0 / P4 / Property 14）：明文 PIN 仅透传给既有校验通道，本接缝不缓存、不写日志、
/// 校验后不延长其内存生命周期（需求 18.5）。
/// </summary>
public sealed class SensitivePageGuard : ISensitivePageGuard
{
    private readonly ISessionContextService _sessionContextService;
    private readonly ILocalAuthService _authService;

    public SensitivePageGuard(
        ISessionContextService sessionContextService,
        ILocalAuthService authService)
    {
        _sessionContextService = sessionContextService
            ?? throw new ArgumentNullException(nameof(sessionContextService));
        _authService = authService
            ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<SensitiveAccessResult> TryEnterAsync(
        string pageKey,
        string enteredPin,
        CancellationToken cancellationToken = default)
    {
        // 需求 18.4 / 17.4：受限模式恒拒绝机密页面，先于 PIN 校验短路（机密内容不渲染）。
        if (_sessionContextService.IsRestrictedPermissionMode)
        {
            return SensitiveAccessResult.BlockedByRestricted;
        }

        // 无登录账号则无从校验 PIN → 拒绝（机密内容不渲染）。
        var accountId = _sessionContextService.Current?.AccountId;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return SensitiveAccessResult.PinRejected;
        }

        // 复用既有 PIN 校验通道；明文 PIN 仅透传，不在本接缝缓存 / 记录 / 延长生命周期（需求 18.5）。
        var pinValid = await _authService.VerifyPinAsync(accountId, enteredPin, cancellationToken);

        // 需求 18.1 / 18.2：PIN 正确且非受限 → 放行；PIN 错误 → 拒绝（中文提示由调用方呈现），机密内容不渲染。
        return pinValid
            ? SensitiveAccessResult.Granted
            : SensitiveAccessResult.PinRejected;
    }
}
