using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Security;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」Owner 紧急启用与受限权限模式状态接线（任务 14.6，design §9.7，BC-13，Req 17 / 13.2）。
///
/// <para>紧急启用入口为「独立的紧急入口弹窗」采集的 6 位 PIN（<b>不</b>放在登录页，登录页保持不变）。
/// 本任务以可注入委托 <see cref="PickEmergencyPin"/> 抽象 PIN 采集，实际弹窗 UI 在任务 18.6 接入；
/// 委托返回采集到的明文 PIN（取消或未采集时返回 <c>null</c>）。</para>
///
/// <para>采集到的 6 位 PIN 经 <see cref="IEmergencyEnableService.TryEmergencyEnableAsync"/> 提交：
/// 成功 → 进入受限权限模式并就地 / Toast 提示；失败 → 给后端返回的中文错误提示（Req 17.2）。</para>
///
/// <para>受限模式只读状态供 UI 提示与能力门控：受限模式下仅放行「数据抢救类」操作
/// （数据备份、数据导出 / 导入恢复，见 <see cref="RestrictedModePolicy"/>），其余入口（现金流 / 经营建议等
/// 机密页面、成员管理、设置内安全与数据高危项、日常业务编辑）一律禁用（Req 17.3 / 17.4 / 13.2）。
/// 会话受限模式变更（<see cref="ISessionContextService.SessionChanged"/>）时刷新这些只读属性。</para>
///
/// <para>安全约束（P0 / P4）：明文 PIN 仅在本命令调用后端校验所需期间存在，提交后即清空局部引用、
/// 不缓存、不写日志 / 诊断；审计写入由后端服务负责且绝不含明文 PIN。</para>
/// </summary>
public partial class MeProfileViewModel
{
    /// <summary>受限模式提示文案（Req 17.3 / 17.4）：仅数据抢救类操作可用。</summary>
    public const string RestrictedModeNotice = "仅数据备份/导入导出恢复等数据抢救操作可用";

    /// <summary>
    /// 紧急入口 PIN 采集委托（可注入）。默认弹「独立的紧急入口弹窗」采集 6 位 PIN——本任务先以委托抽象，
    /// 实际弹窗 UI 在任务 18.6 接入；返回采集到的明文 PIN，取消或未采集时返回 <c>null</c>。
    /// 未接线（<c>null</c>）时紧急启用命令不采集、不调用后端（安全默认）。
    /// </summary>
    public Func<string?>? PickEmergencyPin { get; set; }

    /// <summary>
    /// 紧急启用就地状态文案（成功 / 失败）。无 Toast 服务时作为兜底反馈；不含明文 PIN。
    /// </summary>
    [ObservableProperty]
    private string emergencyStatus = string.Empty;

    // ── 受限权限模式只读状态（供 UI 提示与能力门控，Req 17.1 / 17.3 / 17.4） ──

    /// <summary>
    /// 当前会话是否处于受限权限模式（只读，派生自 <see cref="ISessionContextService.IsRestrictedPermissionMode"/>）。
    /// 会话未注入时为 <see langword="false"/>。
    /// </summary>
    public bool IsRestrictedPermissionMode => _sessionContext?.IsRestrictedPermissionMode ?? false;

    /// <summary>是否可访问机密数据（现金流 / 经营建议等）。受限模式下恒禁用（Req 17.4）。</summary>
    public bool CanAccessConfidentialData => IsOperationAllowedNow(RestrictedOperationKind.Cashflow);

    /// <summary>是否可使用成员管理（创建 / 删除 / 停用 / 重置）。受限模式下恒禁用（Req 17.4）。</summary>
    public bool CanUseMemberManagement => IsOperationAllowedNow(RestrictedOperationKind.MemberManagement);

    /// <summary>是否可使用设置内安全与数据高危项。受限模式下恒禁用（Req 17.4）。</summary>
    public bool CanUseSecurityAndDataHighRiskSettings
        => IsOperationAllowedNow(RestrictedOperationKind.SecurityAndDataHighRiskSettings);

    /// <summary>是否可进行日常业务数据编辑。受限模式下恒禁用（Req 17.4）。</summary>
    public bool CanEditDailyBusinessData => IsOperationAllowedNow(RestrictedOperationKind.DailyBusinessDataEdit);

    /// <summary>
    /// 是否可执行数据抢救类操作（数据备份、数据导出 / 导入恢复）。正常与受限模式下均放行（Req 17.3）。
    /// </summary>
    public bool CanUseDataRescueOperations => IsOperationAllowedNow(RestrictedOperationKind.DataBackup);

    // ── 命令 ──

    /// <summary>
    /// 尝试 Owner 紧急启用（Req 17.1 / 17.2 / 17.5 / 13.2）：经 <see cref="PickEmergencyPin"/> 采集 6 位 PIN →
    /// 调 <see cref="IEmergencyEnableService.TryEmergencyEnableAsync"/>。成功提示进入受限模式；失败给中文提示。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTryEmergencyEnable))]
    private async Task TryEmergencyEnableAsync()
    {
        if (_emergencyEnable is null || IsBusy)
        {
            return;
        }

        // 经可注入委托采集 6 位 PIN（实际弹「独立的紧急入口弹窗」UI 在任务 18.6；不放登录页）。
        var pin = PickEmergencyPin?.Invoke();
        if (string.IsNullOrEmpty(pin))
        {
            // 取消采集 / 未接线：不调用后端、不改变会话状态（Req 17.2 同口径，前置不满足即放弃）。
            return;
        }

        try
        {
            IsBusy = true;

            var result = await _emergencyEnable
                .TryEmergencyEnableAsync(CurrentAccountId, pin, CancellationToken.None)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                EmergencyStatus = $"已进入受限权限模式：{RestrictedModeNotice}";
                _toast?.Show(EmergencyStatus, ToastSeverity.Success);
            }
            else
            {
                // Req 17.2：失败给后端返回的中文错误提示（如「PIN 不正确，无法紧急启用」）。
                EmergencyStatus = result.ErrorMessage ?? "紧急启用失败";
                _toast?.Show(EmergencyStatus, ToastSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            EmergencyStatus = $"紧急启用失败：{ex.Message}";
            _toast?.Show(EmergencyStatus, ToastSeverity.Error);
        }
        finally
        {
            // P4：明文 PIN 即用即清——清空局部引用，不缓存、不写日志；会话权限模式由后端服务驱动，
            // 此处经 SessionChanged 事件统一刷新受限模式只读属性。
            pin = null;
            IsBusy = false;
        }
    }

    private bool CanTryEmergencyEnable() => _emergencyEnable is not null && !IsBusy;

    // ── 内部帮助 ──

    /// <summary>
    /// 当前会话下某操作是否被放行：非受限模式一律放行；受限模式经
    /// <see cref="RestrictedModePolicy.IsOperationAllowedInRestrictedMode"/> 判定（仅数据抢救类放行）。
    /// </summary>
    private bool IsOperationAllowedNow(RestrictedOperationKind kind)
        => !IsRestrictedPermissionMode || RestrictedModePolicy.IsOperationAllowedInRestrictedMode(kind);

    /// <summary>
    /// 会话变更（含受限权限模式进入 / 退出）时刷新受限模式只读状态与能力门控属性（Req 17.1）。
    /// </summary>
    private void OnSessionContextChangedForRestrictedMode(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsRestrictedPermissionMode));
        OnPropertyChanged(nameof(CanAccessConfidentialData));
        OnPropertyChanged(nameof(CanUseMemberManagement));
        OnPropertyChanged(nameof(CanUseSecurityAndDataHighRiskSettings));
        OnPropertyChanged(nameof(CanEditDailyBusinessData));
        OnPropertyChanged(nameof(CanUseDataRescueOperations));
    }
}
