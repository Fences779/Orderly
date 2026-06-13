using Orderly.Core.Models;

namespace Orderly.Core.Services;

/// <summary>
/// 敏感页面 PIN 门禁后端接缝（需求 18.1~18.5 / 17.4 / 13.3，design §9.8 / Property 14）。
///
/// 门禁范围限定为「和钱相关的机密页面」：现金流，以及经营建议等含财务数据的页面；
/// 库存、商品等非财务敏感页面不纳入门禁，避免过度打扰。
///
/// 门禁作为<b>访问控制层叠加</b>在目标页面之上：只决定「能否进入并渲染机密内容」，
/// <b>不改动该页面进入后自身的既有 UI 结构与布局</b>（需求 13.3）。未通过验证时机密内容恒不渲染（需求 18.3）。
///
/// 判定顺序（与 design §9.8 伪代码 / Property 14 一致）：
/// <list type="number">
///   <item>受限模式短路：<see cref="ISessionContextService.IsRestrictedPermissionMode"/> 为真 → <see cref="SensitiveAccessResult.BlockedByRestricted"/>（先于 PIN 校验，需求 18.4 / 17.4）。</item>
///   <item>复用既有 <see cref="ILocalAuthService.VerifyPinAsync"/> 校验当前账号的 6 位 PIN：正确 → <see cref="SensitiveAccessResult.Granted"/>；错误 → <see cref="SensitiveAccessResult.PinRejected"/>（需求 18.1 / 18.2）。</item>
/// </list>
///
/// 会话锁定（<c>PendingPinUnlock</c>）的「先解锁」协同由调用方（UI 任务 19.x）处理；
/// 本接缝聚焦受限模式短路 + PIN 校验。
///
/// 安全约束（P0 / P4 / Property 14）：明文 PIN 仅在校验所需期间存在，仅透传给既有校验通道，
/// 校验后即清，不缓存、不写日志 / 诊断、不延长其内存生命周期（需求 18.5）。
/// </summary>
public interface ISensitivePageGuard
{
    /// <summary>
    /// 进入敏感页面前调用：先判定受限模式，再校验 6 位 PIN。
    /// 返回 <see cref="SensitiveAccessResult.Granted"/> 时调用方才挂载 / 渲染该页面的机密内容；
    /// 其余结果下机密内容恒不渲染。
    /// </summary>
    /// <param name="pageKey">机密页面标识（如现金流页面）。</param>
    /// <param name="enteredPin">进入前采集的 6 位 PIN。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>门禁判定结果。</returns>
    Task<SensitiveAccessResult> TryEnterAsync(
        string pageKey,
        string enteredPin,
        CancellationToken cancellationToken = default);
}
