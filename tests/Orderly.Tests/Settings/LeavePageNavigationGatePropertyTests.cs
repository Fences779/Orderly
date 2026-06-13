using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Orderly.App.ViewModels;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// Property-based test for <see cref="SettingsViewModel.TryLeaveSettingsAsync"/> leave-page
/// navigation gate (design §9.5 / §11 Property 10).
///
/// <para><b>Property 10: 离开页导航闸门.</b>
/// 对任意（最近一次保存结果状态 × 导航 <c>old</c>/<c>new</c>）组合：</para>
/// <list type="bullet">
/// <item>导航被放行 ⟺（最近一次保存成功 ∨ 本次停留未发生保存）（Req 3.8、3.3、3.5）：
/// <see cref="SettingsViewModel.TryLeaveSettingsAsync"/> 返回 <c>true</c>。</item>
/// <item>最近一次保存失败 → 阻止离开（返回 <c>false</c>），且 <see cref="SettingsViewModel.LastSaveOutcome"/>
/// 不被清空（以便再次拦截）。</item>
/// <item>非「离开设置页」场景（旧值非「设置」或新值仍为「设置」）→ 恒放行 <c>true</c> 且不改变保存结果状态
/// （Req 3.2）。</item>
/// </list>
///
/// <para>本测试用可控的 fake <see cref="IAppSettingRepository"/> 驱动三种保存结果状态：
/// 「未保存」（本次停留不触发任何保存，<c>LastSaveOutcome == null</c>）、「成功」（保存成功）、
/// 「失败」（<see cref="SavePreferencesAsync"/> 抛 <see cref="IOException"/> 使 <c>LastSaveOutcome</c>
/// 归类为失败 <c>SET-1001</c>）。配合「离开 / 非离开」两类导航，穷举验证导航闸门的放行 / 阻止双条件
/// 与失败状态保留不变式。</para>
///
/// **Validates: Requirements 3.8, 3.2, 3.3, 3.5**
/// </summary>
public sealed class LeavePageNavigationGatePropertyTests
{
    /// <summary>本次停留期间的保存结果状态（驱动 <see cref="SettingsViewModel.LastSaveOutcome"/>）。</summary>
    private enum SaveState
    {
        /// <summary>本次停留未发生任何保存（<c>LastSaveOutcome == null</c>）。</summary>
        None,

        /// <summary>最近一次保存成功。</summary>
        Success,

        /// <summary>最近一次保存失败（归类为 <c>SET-1001</c>）。</summary>
        Failure,
    }

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

    // 任意分区（含「设置」）。
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

    // 偏向离开动作（覆盖闸门主路径），并保留非离开动作覆盖「不触发」边界。
    private static readonly Gen<NavAction> ActionGen =
        Gen.Frequency((3, LeaveActionGen), (1, NonLeaveActionGen));

    private static readonly Gen<SaveState> SaveStateGen =
        Gen.OneOfConst(SaveState.None, SaveState.Success, SaveState.Failure);

    private static readonly Gen<Scenario> ScenarioGen =
        Gen.Select(SaveStateGen, ActionGen, (state, action) => new Scenario(state, action));

    [Fact]
    public void Property10_navigation_allowed_iff_last_save_succeeded_or_no_save_and_failure_blocks_without_clearing()
    {
        ScenarioGen.Sample(
            scenario =>
            {
                var toast = new RecordingToastService();
                SettingsViewModel vm = BuildSettingsViewModel(scenario.State, toast);

                // 前置不变式：保存结果状态与 LastSaveOutcome 一致。
                AssertPreconditionState(scenario.State, vm);

                SettingsSaveOutcome? before = vm.LastSaveOutcome;
                NavAction action = scenario.Action;
                bool leaving = IsLeavingSettings(action.Old, action.New);

                bool allowed = vm.TryLeaveSettingsAsync(action.Old, action.New)
                    .GetAwaiter().GetResult();

                if (!leaving)
                {
                    // Req 3.2：非「离开设置页」场景恒放行，且不改变保存结果状态、不弹任何提示。
                    Assert.True(allowed, $"非离开导航 {action.Old}→{action.New} 应被放行");
                    Assert.Same(before, vm.LastSaveOutcome);
                    Assert.Empty(toast.Calls);
                    return;
                }

                // 离开设置页：放行 ⟺（最近一次成功 ∨ 本次停留未保存）。
                bool expectedAllowed = scenario.State != SaveState.Failure;
                Assert.Equal(expectedAllowed, allowed);

                switch (scenario.State)
                {
                    case SaveState.None:
                        // Req 3.5：未发生保存 → 放行、不弹提示、状态仍为空。
                        Assert.True(allowed);
                        Assert.Null(vm.LastSaveOutcome);
                        Assert.Empty(toast.Calls);
                        break;

                    case SaveState.Success:
                        // Req 3.3：成功 → 放行，弹成功提示并清空已消费结果。
                        Assert.True(allowed);
                        Assert.Null(vm.LastSaveOutcome);
                        (string Message, ToastSeverity Severity) call = Assert.Single(toast.Calls);
                        Assert.Equal(ToastSeverity.Success, call.Severity);
                        Assert.Equal("设置已保存", call.Message);
                        break;

                    case SaveState.Failure:
                        // Req 3.8：失败 → 阻止离开（返回 false），且 LastSaveOutcome 不清空以便再次拦截。
                        Assert.False(allowed);
                        Assert.Same(before, vm.LastSaveOutcome);
                        Assert.NotNull(vm.LastSaveOutcome);
                        Assert.False(vm.LastSaveOutcome!.Success);
                        Assert.Equal(SettingsSaveErrorCode.Persistence, vm.LastSaveOutcome.ErrorCode);
                        break;

                    default:
                        Assert.Fail($"未覆盖的保存结果状态：{scenario.State}");
                        break;
                }
            },
            iter: PbtConfig.MinIterations);
    }

    /// <summary>断言构造后的 <see cref="SettingsViewModel.LastSaveOutcome"/> 与目标保存结果状态一致。</summary>
    private static void AssertPreconditionState(SaveState state, SettingsViewModel vm)
    {
        switch (state)
        {
            case SaveState.None:
                Assert.Null(vm.LastSaveOutcome);
                break;
            case SaveState.Success:
                Assert.True(
                    vm.LastSaveOutcome is { Success: true },
                    "Success 状态应已记录一条成功的 LastSaveOutcome");
                break;
            case SaveState.Failure:
                Assert.True(
                    vm.LastSaveOutcome is { Success: false, ErrorCode: SettingsSaveErrorCode.Persistence },
                    "Failure 状态应已记录一条 SET-1001 失败的 LastSaveOutcome");
                break;
        }
    }

    /// <summary>
    /// 构造一个 <see cref="SettingsViewModel"/> 并将其 <see cref="SettingsViewModel.LastSaveOutcome"/>
    /// 驱动到目标 <paramref name="state"/>：
    /// <list type="bullet">
    /// <item><see cref="SaveState.None"/>：不触发任何保存（<c>LastSaveOutcome</c> 保持 <c>null</c>）。</item>
    /// <item><see cref="SaveState.Success"/>：注入恒成功仓储，变更一个即时 P0 字段触发保存并 flush 落盘。</item>
    /// <item><see cref="SaveState.Failure"/>：注入抛 <see cref="IOException"/> 的仓储，触发保存并 flush，
    /// 使 <c>LastSaveOutcome</c> 归类为失败 <c>SET-1001</c>。</item>
    /// </list>
    /// </summary>
    private static SettingsViewModel BuildSettingsViewModel(SaveState state, RecordingToastService toast)
    {
        IAppSettingRepository repository = state == SaveState.Failure
            ? new ThrowingAppSettingRepository()
            : new SuccessAppSettingRepository();

        var vm = new SettingsViewModel(settingRepository: repository, toast: toast);

        if (state != SaveState.None)
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

    private sealed record Scenario(SaveState State, NavAction Action);

    /// <summary>记录每次 <see cref="Show"/> 调用的 fake Toast 服务，用于断言提示次数与内容。</summary>
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

    /// <summary>
    /// 保存恒抛 <see cref="IOException"/> 的 fake 偏好仓储，使自动保存路径产生失败 <c>LastSaveOutcome</c>
    /// （持久化失败归类为 <c>SET-1001</c>）。
    /// </summary>
    private sealed class ThrowingAppSettingRepository : IAppSettingRepository
    {
        public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferences());

        public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
            => throw new IOException("模拟落盘失败");

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
