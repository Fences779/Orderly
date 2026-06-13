using System;
using System.Threading.Tasks;

namespace Orderly.App.ViewModels;

/// <summary>
/// 壳层离开设置页导航闸门接线（任务 13.6 / BC-9，设计 §9.5）。
///
/// <para>导航事件源在 <see cref="MainViewModel"/>：用 <c>CommunityToolkit.Mvvm</c> 生成的
/// <see cref="OnSelectedSectionChanging(string, string)"/> 部分方法捕获旧值；在已有的
/// <c>OnSelectedSectionChanged</c>（见 <c>MainViewModel.SettingsP0.cs</c>）中检测到「旧=设置且新≠设置」时，
/// 经 <see cref="HandleLeaveSettingsGateAsync"/> 调用 <see cref="SettingsViewModel.TryLeaveSettingsAsync"/>。</para>
///
/// <para><b>同步 Changing vs 异步 flush 的取舍</b>：<c>OnSelectedSectionChanging</c> 是同步部分方法，无法在其中
/// await 自动保存 flush；因此采用设计 §9.5 推荐的「拉回」稳妥方案——在 <c>Changed</c> 阶段异步执行闸门，
/// 若闸门返回 <c>false</c>（最近一次保存失败）则将 <see cref="SelectedSection"/> 拉回「设置」，并用
/// <see cref="_isRevertingSettingsSection"/> 防重入，避免拉回动作再次触发闸门。</para>
/// </summary>
public partial class MainViewModel
{
    /// <summary>导航变更前捕获的旧分区值，供 <c>OnSelectedSectionChanged</c> 判定「是否离开设置页」。</summary>
    private string? _sectionBeforeChange;

    /// <summary>正在因闸门阻止而把 <see cref="SelectedSection"/> 拉回「设置」；置位期间抑制闸门重入。</summary>
    private bool _isRevertingSettingsSection;

    /// <summary>
    /// 捕获导航旧值（BC-9 / 设计 §9.5）。<c>CommunityToolkit.Mvvm</c> 为 <see cref="SelectedSection"/> 生成的
    /// 两参 <c>Changing</c> 部分方法，在属性落到新值之前调用。
    /// </summary>
    partial void OnSelectedSectionChanging(string? oldValue, string newValue)
    {
        _sectionBeforeChange = oldValue;
    }

    /// <summary>
    /// 离开设置页导航闸门钩子（设计 §9.5 / Req 3.2、3.3、3.8）。由 <c>OnSelectedSectionChanged</c> 在归一化通过后调用：
    /// 仅当非拉回过程、且「旧=设置且新≠设置」时，异步执行闸门。
    /// </summary>
    private void TriggerLeaveSettingsGateIfNeeded(string newSection)
    {
        if (_isRevertingSettingsSection)
        {
            return;
        }

        if (!string.Equals(_sectionBeforeChange, SectionSettings, StringComparison.Ordinal)
            || string.Equals(newSection, SectionSettings, StringComparison.Ordinal))
        {
            return;
        }

        _ = HandleLeaveSettingsGateAsync(SectionSettings, newSection);
    }

    /// <summary>
    /// 异步执行离开设置页闸门：await <see cref="SettingsViewModel.TryLeaveSettingsAsync"/>；
    /// 返回 <c>false</c>（阻止离开）时把 <see cref="SelectedSection"/> 拉回「设置」（设计 §9.5 / Req 3.8）。
    /// </summary>
    private async Task HandleLeaveSettingsGateAsync(string oldSection, string newSection)
    {
        var canLeave = await Settings.TryLeaveSettingsAsync(oldSection, newSection);
        if (canLeave)
        {
            return;
        }

        // 阻止离开：拉回设置页。置位防重入标志，使拉回触发的 Changed 不再进入闸门。
        _isRevertingSettingsSection = true;
        try
        {
            SelectedSection = SectionSettings;
        }
        finally
        {
            _isRevertingSettingsSection = false;
        }
    }
}
