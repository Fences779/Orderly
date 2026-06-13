namespace Orderly.Core.Security;

/// <summary>
/// 受限权限模式（<see cref="Orderly.Core.Models.SessionPermissionMode.Restricted_Permission"/>）下可执行的操作种类
/// （需求 17.3 / 17.4 / 13.2，design §9.7）。
///
/// 用于把「受限模式仅放行数据抢救类操作」这一边界表达为可枚举、可测试的输入空间，
/// 供白名单纯函数 <see cref="RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind)"/> 判定。
/// </summary>
public enum RestrictedOperationKind
{
    // —— 数据抢救类（受限模式放行）——

    /// <summary>数据备份。受限模式放行（需求 17.3）。</summary>
    DataBackup,

    /// <summary>数据导出 / 导入恢复。受限模式放行（需求 17.3）。</summary>
    DataExportImportRestore,

    // —— 机密 / 高危类（受限模式一律拒绝，需求 17.4 / 18.4）——

    /// <summary>现金流等和钱相关机密页面。受限模式拒绝。</summary>
    Cashflow,

    /// <summary>经营建议等含财务数据的机密页面。受限模式拒绝。</summary>
    BusinessAdvice,

    /// <summary>成员管理（创建 / 删除 / 停用 / 重置）。受限模式拒绝。</summary>
    MemberManagement,

    /// <summary>设置内安全与数据高危项。受限模式拒绝。</summary>
    SecurityAndDataHighRiskSettings,

    /// <summary>日常业务数据编辑。受限模式拒绝。</summary>
    DailyBusinessDataEdit
}

/// <summary>
/// 受限权限模式操作白名单纯函数判定（需求 17.3 / 17.4 / 13.2，design §9.7，Property 14）。
///
/// 受限模式仅放行「数据抢救类」操作（数据备份、数据导出 / 导入恢复），其余一律拒绝
/// （现金流 / 经营建议等所有和钱相关机密页面、成员管理创建 / 删除 / 停用 / 重置、
/// 设置内安全与数据高危项、日常业务数据编辑）。
///
/// 以纯函数表达，不读取会话 / UI 状态，便于服务层与 ViewModel 层复用与属性测试。
/// </summary>
public static class RestrictedModePolicy
{
    /// <summary>
    /// 判定某操作种类是否在受限权限模式下被放行。仅「数据抢救类」放行，其余一律拒绝。
    /// </summary>
    /// <param name="operationKind">待判定的操作种类。</param>
    /// <returns><see langword="true"/> 表示受限模式下放行；否则拒绝。</returns>
    public static bool IsOperationAllowedInRestrictedMode(RestrictedOperationKind operationKind)
        => operationKind is RestrictedOperationKind.DataBackup
            or RestrictedOperationKind.DataExportImportRestore;
}
