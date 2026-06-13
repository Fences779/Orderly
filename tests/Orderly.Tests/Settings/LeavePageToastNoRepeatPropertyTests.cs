using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsViewModel.TryLeaveSettingsAsync"/> leave-page
/// Toast de-duplication (design §9.5 / §11 Property 5).
///
/// <para><b>Property 5: Toast 不重复.</b>
/// 离开设置页触发一次结果 Toast 后 <see cref="SettingsViewModel.LastSaveOutcome"/> 被清空（Req 3.6）；
/// 本次停留未发生保存（<c>LastSaveOutcome == null</c>）时不弹出任何 Toast 且放行离开（Req 3.5）。</para>
///
/// <para>该属性的核心不变式：对任意「是否在本次停留发生过保存」与「任意长度的导航尝试序列」组合，
/// 经离开设置页触发的成功结果 Toast 至多出现一次——首次离开消费并清空 <c>LastSaveOutcome</c> 后，
/// 后续离开（未再保存）恒不再弹出（不重复）；未发生保存时则一次都不弹。非「离开设置页」的导航
/// （旧值非「设置」或新值仍为「设置」）既不弹 Toast 也不改变 <c>LastSaveOutcome</c>。</para>
///
/// <para>本测试用记录调用的 fake <see cref="IToastService"/> 统计 <see cref="IToastService.Show"/>
/// 次数以断言「不重复」；用可控的 fake <see cref="IAppSettingRepository"/>（保存成功）经 <c>*Input</c>
/// 变更驱动一次自动保存使 <c>LastSaveOutcome</c> 置为成功，覆盖 Req 3.5、3.6 两条停留场景。</para>
///
/// **Validates: Requirements 3.5, 3.6**
/// </summary>
public sealed class LeavePageToastNoRepeatPropertyTests
{
    // 非「设置」的目标分区（离开设置页的合法去向）。
    private static readonly string[] NonSettingsSections =
    {
        MainViewModel.SectionWorkbench,
        MainViewModel.SectionOrders,
        MainViewModel.SectionProducts,
        MainViewModel.SectionInventory,
        MainViewModel.SectionCustomers,
        MainViewModel.SectionCashflow,
        MainViewModel.SectionBusinessAdvice,
        MainViewModel.SectionMe,
    };

    // 任意分区（含「设置」），用于构造「非离开」导航的新值。
    private static readonly string[] AllSections =
        NonSettingsSections.Append(MainViewModel.SectionSettings).ToArray();

    private static readonly Gen<string> NonSettingsSectionGen = Gen.OneOfConst(NonSettingsSections);
    private static readonly Gen<string> AnySectionGen = Gen.OneOfConst(AllSections);

    // 「离开设置页」导航：旧值恒为「设置」，新值为任意非「设置」分区（满足 IsLeavingSettings）。
    private static readonly Gen<NavAction> LeaveActionGen =
        NonSettingsSectionGen.Select(target => new NavAction(MainViewModel.SectionSettings, target));

    // 「非离开」导航：旧值为非「设置」分区（无论新值是什么都不构成离开设置页）。
    private static readonly Gen<NavAction> NonLeaveActionGen =
        Gen.Select(NonSettingsSectionGen, AnySectionGen, (oldS, newS) => new NavAction(oldS, newS));

    // 偏向离开动作（覆盖消费/清空主路径），并保留非离开动作覆盖「不触发」边界。
    private static readonly Gen<NavAction> ActionGen =
        Gen.Frequency((3, LeaveActionGen), (1, NonLeaveActionGen));

    // 场景：是否在停留期间发生一次成功保存 + 任意长度（0..5）的导航尝试序列。
    private static readonly Gen<Scenario> ScenarioGen =
        Gen.Select(Gen.Bool, ActionGen.Array[0, 5], (performSave, actions) => new Scenario(performSave, actions));

    [Fact]
    public void Property5_success_toast_fires_at_most_once_then_cleared_and_no_save_never_toasts()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                var toast = new RecordingToastService();
                SettingsViewModel vm = BuildSettingsViewModel(scenario.PerformSave, toast);

                // 前置不变式：performSave ⟺ 停留后存在一条「成功」LastSaveOutcome。
                if (scenario.PerformSave)
                {
                    Assert.True(
                        vm.LastSaveOutcome is { Success: true },
                        "performSave=true 时应已记录一条成功的 LastSaveOutcome");
                }
                else
                {
                    Assert.Null(vm.LastSaveOutcome);
                }

                var hasSeenLeave = false;
                foreach (NavAction action in scenario.Actions)
                {
                    bool leaving = IsLeavingSettings(action.Old, action.New);
                    bool allowed = vm.TryLeaveSettingsAsync(action.Old, action.New)
                        .GetAwaiter().GetResult();

                    // Req 3.3 / 3.5：成功或未保存均放行离开；本场景永不阻止。
                    Assert.True(allowed, $"导航 {action.Old}→{action.New} 应被放行");

                    if (leaving)
                    {
                        hasSeenLeave = true;
                        // Req 3.6：任一「离开」消费后，LastSaveOutcome 恒被清空（无论原先是成功还是 null）。
                        Assert.Null(vm.LastSaveOutcome);
                    }
                }

                // 「不重复」：整条序列中成功 Toast 至多一次——
                // 仅当「发生过保存」且「至少有一次离开」时恰好弹一次，否则一次都不弹。
                int expectedToastCount = (scenario.PerformSave && hasSeenLeave) ? 1 : 0;
                Assert.Equal(expectedToastCount, toast.Calls.Count);

                // 本场景仅可能出现「成功」Toast（无失败路径）。
                Assert.All(toast.Calls, call => Assert.Equal(ToastSeverity.Success, call.Severity));
                Assert.All(toast.Calls, call => Assert.Equal("设置已保存", call.Message));

                // Req 3.5：未发生保存时，无论多少次离开尝试都不弹任何 Toast。
                if (!scenario.PerformSave)
                {
                    Assert.Empty(toast.Calls);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>
    /// 构造一个 <see cref="SettingsViewModel"/>，注入记录型 Toast 与成功保存仓储；
    /// 当 <paramref name="performSave"/> 为 <c>true</c> 时经一次即时 <c>*Input</c> 变更驱动自动保存，
    /// 并 flush 至落盘，使 <see cref="SettingsViewModel.LastSaveOutcome"/> 置为成功结果。
    /// </summary>
    private static SettingsViewModel BuildSettingsViewModel(bool performSave, RecordingToastService toast)
    {
        var repository = new SuccessAppSettingRepository();
        var vm = new SettingsViewModel(settingRepository: repository, toast: toast);

        if (performSave)
        {
            // 切换一个属于即时自动保存集合的 P0 字段，触发入队保存（即改即存语义）。
            vm.MaskPhoneByDefaultInput = !vm.MaskPhoneByDefaultInput;
            vm.FlushPendingAutoSaveAsync().GetAwaiter().GetResult();
        }

        return vm;
    }

    /// <summary>镜像 <c>SettingsViewModel.IsLeavingSettings</c>：旧值为「设置」且新值不为「设置」。</summary>
    private static bool IsLeavingSettings(string? oldSection, string? newSection)
        => string.Equals(oldSection, MainViewModel.SectionSettings, StringComparison.Ordinal)
            && !string.Equals(newSection, MainViewModel.SectionSettings, StringComparison.Ordinal);

    private sealed record NavAction(string? Old, string? New);

    private sealed record Scenario(bool PerformSave, IReadOnlyList<NavAction> Actions);

    /// <summary>记录每次 <see cref="Show"/> 调用的 fake Toast 服务，用于统计弹出次数与内容。</summary>
    private sealed class RecordingToastService : IToastService
    {
        public List<(string Message, ToastSeverity Severity)> Calls { get; } = new();

        public void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null)
            => Calls.Add((message, severity));
    }

    /// <summary>保存恒成功的 fake 偏好仓储，使自动保存路径产生成功的 <c>LastSaveOutcome</c>。</summary>
    private sealed class SuccessAppSettingRepository : IAppSettingRepository
    {
        public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferences());

        public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
