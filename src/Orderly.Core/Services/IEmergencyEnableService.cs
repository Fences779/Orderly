using Orderly.Core.Models;

namespace Orderly.Core.Services;

/// <summary>
/// Owner 紧急启用后端接缝（需求 17.1 / 17.2 / 17.5 / 13.2，design §9.7 / Property 14）。
///
/// 被停用的 <see cref="LocalAccountRole.Owner"/> 可凭正确的 6 位 PIN「紧急启用」，进入
/// <see cref="SessionPermissionMode.Restricted_Permission"/> 受限权限模式：仅可执行数据抢救类
/// 操作（数据备份、数据导出 / 导入恢复，见 <see cref="Orderly.Core.Security.RestrictedModePolicy"/>），
/// 被拒绝查看现金流等机密 / 隐私数据。
///
/// 入口为「独立的紧急入口弹窗」采集的 6 位 PIN（非登录页），本接缝只接收 PIN 与目标 Owner 账号；
/// UI 弹窗接线见任务 14.6 / 18.6。
///
/// 安全约束（P0 / P4 / Property 14）：成功 / 失败均经 <see cref="ISecurityAuditService"/> 记审计且绝不含明文 PIN；
/// 明文 PIN 仅在校验所需期间存在，校验后即清，不延长其内存生命周期、不写日志 / 诊断。
/// </summary>
public interface IEmergencyEnableService
{
    /// <summary>
    /// 尝试为被停用的 Owner 执行紧急启用：
    /// 前置要求目标账号为 <see cref="LocalAccountRole.Owner"/> 且 <c>IsEnabled == false</c>；
    /// PIN 正确 → 进入受限权限模式并返回成功（需求 17.1）；
    /// PIN 不正确或前置不满足 → 拒绝并返回中文错误提示，会话权限模式保持不变（需求 17.2）。
    /// </summary>
    /// <param name="ownerAccountId">目标 Owner 账号标识。</param>
    /// <param name="enteredPin">紧急入口弹窗采集的 6 位 PIN。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>紧急启用结果。</returns>
    Task<EmergencyEnableResult> TryEmergencyEnableAsync(
        string ownerAccountId,
        string enteredPin,
        CancellationToken cancellationToken = default);
}
