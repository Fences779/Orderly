using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orderly.App.ViewModels;
using Orderly.App.ViewModels.Helpers;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Xunit;

namespace Orderly.Tests.Settings;

/// <summary>
/// 「设置页」离开页保存结果聚合与导航闸门的<b>明确示例单元测试</b>（任务 13.9，设计 §9.5）。
///
/// <para>区别于 <see cref="LeavePageToastNoRepeatPropertyTests"/> 的属性测试（覆盖任意序列的不变式），
/// 本文件用固定示例逐条验证 <see cref="SettingsViewModel.TryLeaveSettingsAsync"/> 的四类离开页行为：</para>
///
/// <list type="number">
/// <item>仅「旧=设置且新≠设置」才触发闸门；旧≠设置或新=设置 → 不触发、放行、不弹、不改 <c>LastSaveOutcome</c>（Req 3.2）。</item>
/// <item>本次未保存（<c>LastSaveOutcome == null</c>）→ 放行、不弹 Toast（Req 3.5）。</item>
/// <item>最近成功 → 放行 + 弹一次「设置已保存」(<see cref="ToastSeverity.Success"/>) + 清空 <c>LastSaveOutcome</c>，再次离开不重复弹（Req 3.3、3.6）。</item>
/// <item>最近失败 → 阻止离开（返回 <c>false</c>）+ 弹人话+错误码 (<see cref="ToastSeverity.Error"/>，经 <see cref="SettingsSaveErrorCode.BuildFailureToastMessage"/>) + 保留 <c>LastSaveOutcome</c>（以便再拦截）（Req 3.4、3.8）。</item>
/// </list>
///
/// **Validates: Requirements 3.2, 3.5, 3.6, 3.8**
/// </summary>
public sealed class LeavePageAggregationTests
{
    // ── Req 3.2：仅「旧=设置且新≠设置」才触发闸门 ───────────────────────────────

    [Fact]
    public async Task Leaving_settings_to_non_settings_triggers_gate()
    {
        var toast = new RecordingToastService();
        var vm = BuildSuccessfullySavedViewModel(toast);

        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);

        // 触发闸门：最近一次成功 → 放行并消费结果（清空 + 弹一次成功 Toast）。
        Assert.True(allowed);
        Assert.Null(vm.LastSaveOutcome);
        Assert.Single(toast.Calls);
    }

    [Fact]
    public async Task Old_section_not_settings_does_not_trigger_gate()
    {
        var toast = new RecordingToastService();
        var vm = BuildSuccessfullySavedViewModel(toast);

        // 旧值非「设置」→ 不构成离开设置页：放行、不弹、不改 LastSaveOutcome。
        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionWorkbench, MainViewModel.SectionMe);

        Assert.True(allowed);
        Assert.Empty(toast.Calls);
        Assert.True(vm.LastSaveOutcome is { Success: true });
    }

    [Fact]
    public async Task New_section_still_settings_does_not_trigger_gate()
    {
        var toast = new RecordingToastService();
        var vm = BuildSuccessfullySavedViewModel(toast);

        // 新值仍为「设置」（如内部分类切换）→ 不构成离开设置页：放行、不弹、不改 LastSaveOutcome。
        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionSettings);

        Assert.True(allowed);
        Assert.Empty(toast.Calls);
        Assert.True(vm.LastSaveOutcome is { Success: true });
    }

    // ── Req 3.5：本次未保存 → 放行、不弹 Toast ─────────────────────────────────

    [Fact]
    public async Task No_save_during_stay_allows_leave_without_toast()
    {
        var toast = new RecordingToastService();
        // 未触发任何保存：LastSaveOutcome 恒为 null。
        var vm = new SettingsViewModel(settingRepository: new SuccessAppSettingRepository(), toast: toast);
        Assert.Null(vm.LastSaveOutcome);

        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);

        Assert.True(allowed);
        Assert.Empty(toast.Calls);
        Assert.Null(vm.LastSaveOutcome);
    }

    // ── Req 3.3 / 3.6：最近成功 → 放行 + 弹一次成功 Toast + 清空，再次离开不重复弹 ──

    [Fact]
    public async Task Recent_success_allows_leave_with_single_success_toast_then_cleared()
    {
        var toast = new RecordingToastService();
        var vm = BuildSuccessfullySavedViewModel(toast);
        Assert.True(vm.LastSaveOutcome is { Success: true });

        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);

        // 放行 + 恰好一次「设置已保存」成功 Toast + 清空已消费结果。
        Assert.True(allowed);
        Assert.Single(toast.Calls);
        Assert.Equal("设置已保存", toast.Calls[0].Message);
        Assert.Equal(ToastSeverity.Success, toast.Calls[0].Severity);
        Assert.Null(vm.LastSaveOutcome);
    }

    [Fact]
    public async Task Recent_success_does_not_repeat_toast_on_subsequent_leave()
    {
        var toast = new RecordingToastService();
        var vm = BuildSuccessfullySavedViewModel(toast);

        bool first = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);
        bool second = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionOrders);

        // 首次消费后清空；再次离开（未再保存）不重复弹出。
        Assert.True(first);
        Assert.True(second);
        Assert.Single(toast.Calls);
        Assert.Null(vm.LastSaveOutcome);
    }

    // ── Req 3.4 / 3.8：最近失败 → 阻止离开 + 弹人话+错误码 + 保留结果以便再拦截 ────

    [Fact]
    public async Task Recent_failure_blocks_leave_with_human_readable_error_toast()
    {
        var toast = new RecordingToastService();
        var vm = await BuildFailedSaveViewModelAsync(toast);
        Assert.True(vm.LastSaveOutcome is { Success: false, ErrorCode: SettingsSaveErrorCode.Persistence });

        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);

        // 阻止离开（返回 false）+ 经 BuildFailureToastMessage 的人话+错误码 Error Toast。
        Assert.False(allowed);
        Assert.Single(toast.Calls);
        Assert.Equal(ToastSeverity.Error, toast.Calls[0].Severity);
        Assert.Equal(
            SettingsSaveErrorCode.BuildFailureToastMessage(SettingsSaveErrorCode.Persistence),
            toast.Calls[0].Message);
        // 人话主体 + 错误码括注均可见（不泄露内部异常细节）。
        Assert.Contains("（错误码：SET-1001）", toast.Calls[0].Message);
    }

    [Fact]
    public async Task Recent_failure_preserves_outcome_to_block_again()
    {
        var toast = new RecordingToastService();
        var vm = await BuildFailedSaveViewModelAsync(toast);

        bool first = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);
        // 失败结果未被清空，便于下一次离开继续拦截。
        Assert.True(vm.LastSaveOutcome is { Success: false });

        bool second = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionCashflow);

        // 两次离开均被阻止，且每次都重新弹出失败警示。
        Assert.False(first);
        Assert.False(second);
        Assert.Equal(2, toast.Calls.Count);
        Assert.All(toast.Calls, call => Assert.Equal(ToastSeverity.Error, call.Severity));
        Assert.True(vm.LastSaveOutcome is { Success: false });
    }

    // ── 构造辅助 ───────────────────────────────────────────────────────────────

    /// <summary>构造一个最近一次自动保存<b>成功</b>的 <see cref="SettingsViewModel"/>。</summary>
    private static SettingsViewModel BuildSuccessfullySavedViewModel(RecordingToastService toast)
    {
        var vm = new SettingsViewModel(settingRepository: new SuccessAppSettingRepository(), toast: toast);
        // 切换一个即时自动保存字段触发入队保存，并 flush 至落盘。
        vm.MaskPhoneByDefaultInput = !vm.MaskPhoneByDefaultInput;
        vm.FlushPendingAutoSaveAsync().GetAwaiter().GetResult();
        return vm;
    }

    /// <summary>构造一个最近一次自动保存<b>失败</b>（持久化抛异常 → SET-1001）的 <see cref="SettingsViewModel"/>。</summary>
    private static async Task<SettingsViewModel> BuildFailedSaveViewModelAsync(RecordingToastService toast)
    {
        var vm = new SettingsViewModel(settingRepository: new FailingAppSettingRepository(), toast: toast);
        vm.MaskPhoneByDefaultInput = !vm.MaskPhoneByDefaultInput;
        await vm.FlushPendingAutoSaveAsync();
        return vm;
    }

    /// <summary>记录每次 <see cref="Show"/> 调用的 fake Toast 服务，用于断言弹出次数 / 内容 / 严重级。</summary>
    private sealed class RecordingToastService : IToastService
    {
        public List<(string Message, ToastSeverity Severity)> Calls { get; } = new();

        public void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null)
            => Calls.Add((message, severity));
    }

    /// <summary>保存恒成功的 fake 偏好仓储，使自动保存产生成功的 <c>LastSaveOutcome</c>。</summary>
    private sealed class SuccessAppSettingRepository : IAppSettingRepository
    {
        public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferences());

        public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>保存恒抛 <see cref="IOException"/> 的 fake 偏好仓储，使失败归类为 SET-1001（持久化）。</summary>
    private sealed class FailingAppSettingRepository : IAppSettingRepository
    {
        public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferences());

        public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
            => throw new System.IO.IOException("simulated persistence failure");

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
