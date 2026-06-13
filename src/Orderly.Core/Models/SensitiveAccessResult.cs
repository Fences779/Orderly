namespace Orderly.Core.Models;

/// <summary>
/// 敏感页面 PIN 门禁（<c>ISensitivePageGuard.TryEnterAsync</c>）的判定结果（需求 18 / 17.4，design §9.8 / Property 14）。
///
/// 门禁仅作为访问控制层叠加于现金流等「和钱相关的机密页面」之上：只有 <see cref="Granted"/> 才放行并渲染机密内容；
/// 其余结果下机密内容恒不渲染（需求 18.3）。
/// </summary>
public enum SensitiveAccessResult
{
    /// <summary>PIN 正确且当前会话非受限模式 → 放行进入并渲染机密内容（需求 18.1）。</summary>
    Granted,

    /// <summary>PIN 不正确 → 拒绝进入，配套中文错误提示，机密内容不渲染（需求 18.2 / 18.3）。</summary>
    PinRejected,

    /// <summary>
    /// 当前处于 <see cref="SessionPermissionMode.Restricted_Permission"/> 受限权限模式 → 恒拒绝机密数据访问，
    /// 先于 PIN 校验短路（需求 18.4 / 17.4）。
    /// </summary>
    BlockedByRestricted,
}
