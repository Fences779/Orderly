using System;
using System.Threading.Tasks;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「设置页」离开页保存结果聚合与导航闸门（任务 13.6，设计 §9.5 / Req 3.2、3.3、3.4、3.5、3.6、3.8）。
///
/// <para>导航事件源在壳层 <see cref="MainViewModel"/>（拥有 <c>SelectedSection</c> 与 section 变更检测）；
/// 本分部承载「确保挂起自动保存落盘」与「依最近一次保存结果决定放行/阻止离开」的执行逻辑。
/// <see cref="MainViewModel"/> 在检测到「离开『设置』」时调用 <see cref="TryLeaveSettingsAsync"/>，
/// 据其返回值决定是否取消本次导航（拉回设置页）。</para>
/// </summary>
public partial class SettingsViewModel
{
    /// <summary>
    /// 确保挂起 / 进行中的自动保存已落盘（设计 §9.5）。离开设置页前调用，使最后一次改动在读取
    /// <see cref="LastSaveOutcome"/> 之前完成写入。
    ///
    /// <para>若仍有排队请求但消费循环尚未启动，先驱动一次；随后 await 当前循环句柄。采用有界迭代吸收
    /// 「循环退出与再入队」的竞态窗口（正常情况下一次迭代即清空）。自动保存的失败已由
    /// <c>SaveP0SettingsAsync</c> 捕获并写入 <see cref="LastSaveOutcome"/>，故此处吞掉循环异常不会丢失结果。</para>
    /// </summary>
    public async Task FlushPendingAutoSaveAsync()
    {
        // 有界迭代：正常一次即清空；上界仅为吸收极少见的「退出—再入队」竞态，避免理论上的无限等待。
        for (var i = 0; i < 16; i++)
        {
            if (!_hasQueuedSettingsAutoSave && !_isRunningSettingsAutoSave)
            {
                return;
            }

            if (_hasQueuedSettingsAutoSave && !_isRunningSettingsAutoSave)
            {
                _settingsAutoSaveLoop = ProcessQueuedSettingsAutoSaveAsync();
            }

            var loop = _settingsAutoSaveLoop;
            if (loop is not null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch
                {
                    // 单次保存的失败已归类并写入 LastSaveOutcome；此处不再向上抛出。
                }
            }
            else
            {
                // 标志已置位但句柄尚未捕获的瞬态窗口，让出线程后重试。
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// 离开设置页导航闸门（设计 §9.5 / Req 3.2、3.3、3.4、3.5、3.6、3.8）。
    ///
    /// <list type="bullet">
    /// <item>非「离开设置页」场景（旧值非「设置」或新值仍为「设置」）→ 返回 <c>true</c>（放行，Req 3.2）。</item>
    /// <item>本次停留未发生任何保存（<see cref="LastSaveOutcome"/> 为 <c>null</c>）→ 返回 <c>true</c>，不打扰（Req 3.5）。</item>
    /// <item>最近一次保存成功 → 经 <see cref="IToastService"/> 弹「设置已保存」(<see cref="ToastSeverity.Success"/>)
    /// 并清空 <see cref="LastSaveOutcome"/>，返回 <c>true</c>（Req 3.3、3.6）。</item>
    /// <item>最近一次保存失败 → 经 <see cref="SettingsSaveErrorCode.BuildFailureToastMessage"/> 弹「人话 + 错误码」
    /// (<see cref="ToastSeverity.Error"/>)，<b>不</b>清空 <see cref="LastSaveOutcome"/> 以便再次拦截，返回 <c>false</c>
    /// （阻止离开，Req 3.4、3.8）。</item>
    /// </list>
    ///
    /// <para><see cref="IToastService"/> 未注入（<c>null</c>，集成任务 21.1 才完整注入）时仅返回放行/阻止布尔，不弹提示。</para>
    /// </summary>
    /// <param name="oldSection">导航旧值（离开前所在分区）。</param>
    /// <param name="newSection">导航新值（拟进入的分区）。</param>
    /// <returns><c>true</c> 放行离开；<c>false</c> 阻止离开（壳层据此拉回设置页）。</returns>
    public async Task<bool> TryLeaveSettingsAsync(string? oldSection, string? newSection)
    {
        // Req 3.2：非「离开设置页」场景正常放行。
        if (!IsLeavingSettings(oldSection, newSection))
        {
            return true;
        }

        // 设计 §9.5：先确保最后一次改动已落盘，再读取最近一次保存结果。
        await FlushPendingAutoSaveAsync();

        var outcome = LastSaveOutcome;

        // Req 3.5：本次停留未发生保存 → 放行且不弹提示。
        if (outcome is null)
        {
            return true;
        }

        if (outcome.Success)
        {
            // Req 3.3 / 3.6：成功提示后清空已消费结果，避免重复提示。
            _toast?.Show("设置已保存", ToastSeverity.Success);
            LastSaveOutcome = null;
            return true;
        }

        // Req 3.4 / 3.8：最近一次保存失败 → 人话 + 错误码警示，阻止离开，且不清空以便再次拦截。
        _toast?.Show(
            SettingsSaveErrorCode.BuildFailureToastMessage(outcome.ErrorCode),
            ToastSeverity.Error);
        return false;
    }

    /// <summary>
    /// 判定本次导航是否构成「离开设置页」：旧值为「设置」且新值不为「设置」（设计 §9.5 / Req 3.2、3.8）。
    /// </summary>
    private static bool IsLeavingSettings(string? oldSection, string? newSection)
    {
        return string.Equals(oldSection, MainViewModel.SectionSettings, StringComparison.Ordinal)
            && !string.Equals(newSection, MainViewModel.SectionSettings, StringComparison.Ordinal);
    }
}
