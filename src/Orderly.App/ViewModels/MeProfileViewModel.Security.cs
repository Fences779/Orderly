using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」账户安全 / 登录记录卡接线（任务 14.5，design §6.4 / §10.2，Req 9）。
///
/// <para>经 <see cref="ISecurityAuditService.QueryAsync"/> 按日期范围拉取真实安全审计记录
/// （登录成功 / 失败、账户锁定、凭证变更、成员创建 / 重置 / 停用 / 删除）注入
/// <see cref="MeProfileViewModel.SecurityAuditEntries"/>；<see cref="IsSecurityAuditAvailable"/> 基于
/// 服务是否注入判定（替换骨架阶段硬编码 <c>false</c>）。</para>
///
/// <para>默认窗口（Req 9.6）：初始 <see cref="AuditRangeStart"/> = 今天-30 天、<see cref="AuditRangeEnd"/> = 今天，
/// 即「最近 30 天」；用户可经日期范围筛选（<see cref="ApplyAuditDateRangeCommand"/>）查看更早记录——日期筛选仅作用于
/// <b>读取展示</b>，底层 <see cref="ISecurityAuditService"/> 全量保留全部历史，不因筛选 / 查询而减少（Req 9.7 / Property 15）。</para>
///
/// <para>降级口径（Req 9.3 / 9.4）：查询无记录 → <see cref="IsAuditEmpty"/> 置位并显示空状态文案，绝不臆造数据；
/// 读取失败 → 清空列表（不渲染半截列表）、<see cref="IsAuditLoadFailed"/> 置位、<see cref="AuditStatus"/> 置
/// 「安全记录读取失败」，且不泄露异常细节。</para>
///
/// <para>最近登录（Req 9.5）：<see cref="MeProfileViewModel.CurrentAccountLastLoginAt"/> 取当前账号的
/// <c>LocalAccount.LastLoginAt</c> 直接展示（经账号目录查得；不可得时回退会话登录时间）。</para>
/// </summary>
public partial class MeProfileViewModel
{
    /// <summary>默认安全记录展示窗口天数（Req 9.6：最近 30 天）。</summary>
    private const int DefaultAuditWindowDays = 30;

    // ── 最近登录与审计列表 ──

    /// <summary>当前账号最近登录时间（Req 9.5）：直接展示 <c>LocalAccount.LastLoginAt</c>。</summary>
    [ObservableProperty]
    private DateTime? currentAccountLastLoginAt;

    /// <summary>安全审计记录列表（落在所选日期范围内的子集，顺序稳定）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<SecurityAuditEntry> SecurityAuditEntries { get; } = new();

    // ── 日期范围筛选状态（design §6.4） ──

    /// <summary>起始日期（含），<c>null</c> 表示不限下界。默认今天-30 天（Req 9.6 最近 30 天窗口）。</summary>
    [ObservableProperty]
    private DateTime? auditRangeStart = DateTime.Today.AddDays(-DefaultAuditWindowDays);

    /// <summary>结束日期（含），<c>null</c> 表示不限上界。默认今天（Req 9.6 最近 30 天窗口）。</summary>
    [ObservableProperty]
    private DateTime? auditRangeEnd = DateTime.Today;

    // ── 降级 / 状态信号 ──

    /// <summary>账户安全卡状态文案：空状态 / 读取失败提示（Req 9.3 / 9.4）。正常有数据时为空串。</summary>
    [ObservableProperty]
    private string auditStatus = string.Empty;

    /// <summary>审计读取是否失败（Req 9.4）；为 <c>true</c> 时视图显示「安全记录读取失败」且不渲染半截列表。</summary>
    [ObservableProperty]
    private bool isAuditLoadFailed;

    /// <summary>
    /// 安全审计后端是否可用（design §10.2）：基于 <see cref="ISecurityAuditService"/> 是否注入判定，
    /// 替换骨架阶段硬编码 <c>false</c>。为 <c>false</c> 时视图显示占位文案、不渲染列表与筛选控件。
    /// </summary>
    public bool IsSecurityAuditAvailable => _securityAudit is not null;

    /// <summary>
    /// 是否为「无记录」空状态（Req 9.3）：服务可用、未发生读取失败、且列表为空时为 <c>true</c>。
    /// 读取失败（<see cref="IsAuditLoadFailed"/>）时不视为空状态，以区分两类降级文案。
    /// </summary>
    public bool IsAuditEmpty =>
        IsSecurityAuditAvailable && !IsAuditLoadFailed && SecurityAuditEntries.Count == 0;

    // ── 命令 ──

    /// <summary>
    /// 应用日期范围筛选（design §6.4，Req 9.7）：以当前 <see cref="AuditRangeStart"/> / <see cref="AuditRangeEnd"/>
    /// 重新经 <see cref="ISecurityAuditService.QueryAsync"/> 拉取并刷新 <see cref="SecurityAuditEntries"/>。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyAuditDateRange))]
    private Task ApplyAuditDateRangeAsync() => LoadSecurityAuditAsync(CancellationToken.None);

    private bool CanApplyAuditDateRange() => IsSecurityAuditAvailable && !IsBusy;

    /// <summary>
    /// 初始加载 / 刷新安全审计列表（Req 9.1~9.7）。默认「最近 30 天」窗口；先刷新最近登录时间（Req 9.5），
    /// 再经 <see cref="ISecurityAuditService.QueryAsync"/> 按当前日期范围拉取记录。
    ///
    /// <para>降级：服务未注入 → 显示占位且不查询；空结果 → 空状态文案（Req 9.3）；读取抛异常 → 清空列表、
    /// 置失败文案「安全记录读取失败」、不泄露异常细节（Req 9.4）。</para>
    /// </summary>
    public async Task LoadSecurityAuditAsync(CancellationToken cancellationToken = default)
    {
        // Req 9.5：最近登录时间独立于审计列表展示，先行刷新（不可得时静默回退）。
        await RefreshLastLoginAsync(cancellationToken).ConfigureAwait(true);

        // 服务未注入（后端未启用 / 完整 DI 见 21.1）：显示占位文案，不渲染列表与筛选结果。
        if (_securityAudit is null)
        {
            SecurityAuditEntries.Clear();
            IsAuditLoadFailed = false;
            AuditStatus = "安全记录暂不可用";
            RaiseAuditStateChanged();
            return;
        }

        try
        {
            // 日期范围以「含边界」的整日窗口下推：结束日界取当日 23:59:59.999，确保当天记录被纳入。
            var from = AuditRangeStart?.Date;
            var to = AuditRangeEnd?.Date.AddDays(1).AddTicks(-1);

            var entries = await _securityAudit
                .QueryAsync(accountLabel: ResolveAuditAccountLabel(), from: from, to: to, ct: cancellationToken)
                .ConfigureAwait(true);

            SecurityAuditEntries.Clear();
            foreach (var entry in entries)
            {
                SecurityAuditEntries.Add(entry);
            }

            IsAuditLoadFailed = false;
            // Req 9.3：无记录 → 空状态文案，绝不臆造数据。
            AuditStatus = SecurityAuditEntries.Count == 0 ? "所选范围内暂无安全记录" : string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Req 9.4：读取失败 → 不渲染半截列表、统一降级文案、不泄露异常细节（不附 ex.Message）。
            SecurityAuditEntries.Clear();
            IsAuditLoadFailed = true;
            AuditStatus = "安全记录读取失败";
        }
        finally
        {
            RaiseAuditStateChanged();
        }
    }

    /// <summary>
    /// 刷新当前账号最近登录时间（Req 9.5）。优先经账号目录查得当前账号的 <c>LastLoginAt</c>；
    /// 账号服务不可用 / 查询失败 / 未命中时回退至会话登录时间（若可得），任何异常均静默吞掉。
    /// </summary>
    private async Task RefreshLastLoginAsync(CancellationToken cancellationToken)
    {
        var sessionSignedInAt = _sessionContext?.Current?.SignedInAt;

        if (_accountService is null || string.IsNullOrWhiteSpace(CurrentAccountId))
        {
            CurrentAccountLastLoginAt ??= sessionSignedInAt;
            return;
        }

        try
        {
            var directory = await _accountService
                .ListAccountDirectoryAsync(cancellationToken)
                .ConfigureAwait(true);

            var self = directory.FirstOrDefault(a =>
                string.Equals(a.AccountId, CurrentAccountId, StringComparison.Ordinal));

            CurrentAccountLastLoginAt = self?.LastLoginAt ?? sessionSignedInAt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 最近登录获取失败不影响审计卡其余展示；回退会话登录时间（若可得），不泄露异常细节。
            CurrentAccountLastLoginAt ??= sessionSignedInAt;
        }
    }

    /// <summary>
    /// 解析用于审计查询的账号标签。审计写入接缝以归一化用户名 / 账号标识记录归属，
    /// 此处取当前会话用户名（登录类事件以用户名为标签）；用户名缺失时回退账号标识。
    /// </summary>
    private string? ResolveAuditAccountLabel()
    {
        var username = _sessionContext?.Current?.Username;
        if (!string.IsNullOrWhiteSpace(username))
        {
            return username;
        }

        return string.IsNullOrWhiteSpace(CurrentAccountId) ? null : CurrentAccountId;
    }

    /// <summary>统一广播审计派生状态变更（空状态信号）。</summary>
    private void RaiseAuditStateChanged() => OnPropertyChanged(nameof(IsAuditEmpty));
}
