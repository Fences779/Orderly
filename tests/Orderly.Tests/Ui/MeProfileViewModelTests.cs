using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// 任务 14.7：我的页 ViewModel（<see cref="MeProfileViewModel"/>）单元测试。
///
/// <para>覆盖：角色徽章回退（Req 5.5）、成员空状态（Req 7.3）、权限矩阵命令 <c>CanExecute</c>（Req 7.x）、
/// 审计空 / 失败降级（Req 9.3 / 9.4）、凭证修改命令完成后清空输入（Req 8.8）。</para>
///
/// <para><see cref="MeProfileViewModel"/> 构造时经 <c>CollectionViewSource.GetDefaultView</c> 建立成员列表视图，
/// 该 WPF API 必须在 STA 线程上调用；测试项目未引入 xunit.stafact，故沿用
/// <see cref="PasswordBoxBinderTests"/> 的 <c>RunOnSta</c> 包装在 STA 线程构造并操作 VM，
/// 并将断言异常透传回测试线程。依赖以记录调用 / 可配置行为的 fake 实现替身，不使用真实后端。</para>
///
/// **Validates: Requirements 5.5, 7.3, 9.3, 9.4, 8.8**
/// </summary>
public sealed class MeProfileViewModelTests
{
    private static void RunOnSta(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new InvalidOperationException("STA 测试体内发生异常", captured);
        }
    }

    // ── 角色徽章回退（Req 5.5） ──

    [Fact]
    public void RoleBadge_Owner_session_shows_owner_label()
    {
        RunOnSta(() =>
        {
            var session = new FakeSessionContext(new LocalSessionContext
            {
                AccountId = "owner-1",
                Username = "owner",
                DisplayName = "店主",
                Role = LocalAccountRole.Owner,
            });

            var vm = new MeProfileViewModel(sessionContext: session);

            Assert.True(vm.IsCurrentUserOwner);
            Assert.Equal("系统管理员 Owner", vm.RoleBadgeText);
        });
    }

    [Fact]
    public void RoleBadge_Member_session_falls_back_to_member_label()
    {
        RunOnSta(() =>
        {
            var session = new FakeSessionContext(new LocalSessionContext
            {
                AccountId = "m-1",
                Username = "member",
                DisplayName = "店员",
                Role = LocalAccountRole.Member,
            });

            var vm = new MeProfileViewModel(sessionContext: session);

            Assert.False(vm.IsCurrentUserOwner);
            Assert.Equal("系统店员 Member", vm.RoleBadgeText);
        });
    }

    [Fact]
    public void RoleBadge_no_session_or_unknown_role_falls_back_to_member_label()
    {
        RunOnSta(() =>
        {
            // 无会话（角色判定失败）→ 天然落入 Member 分支。
            var vm = new MeProfileViewModel();

            Assert.False(vm.IsCurrentUserOwner);
            Assert.Equal("系统店员 Member", vm.RoleBadgeText);

            // 角色判定失败回退后，置位 Owner 应切换文案（验证文案随角色派生）。
            vm.IsCurrentUserOwner = true;
            Assert.Equal("系统管理员 Owner", vm.RoleBadgeText);
        });
    }

    // ── 成员空状态（Req 7.3） ──

    [Fact]
    public void MemberList_empty_when_no_accounts()
    {
        RunOnSta(() =>
        {
            var vm = new MeProfileViewModel();

            Assert.True(vm.IsMemberListEmpty);
        });
    }

    [Fact]
    public void MemberList_not_empty_when_accounts_present()
    {
        RunOnSta(() =>
        {
            var vm = new MeProfileViewModel();
            vm.ManagedAccounts.Add(NewMember("m-1", "alice", "爱丽丝"));

            Assert.False(vm.IsMemberListEmpty);
        });
    }

    [Fact]
    public void MemberList_empty_when_filter_has_no_match()
    {
        RunOnSta(() =>
        {
            var vm = new MeProfileViewModel();
            vm.ManagedAccounts.Add(NewMember("m-1", "alice", "爱丽丝"));
            vm.ManagedAccounts.Add(NewMember("m-2", "bob", "鲍勃"));

            Assert.False(vm.IsMemberListEmpty);

            // 过滤无命中 → 视图为空 → 空状态置位。
            vm.MemberSearchQuery = "zzz-no-such-member";

            Assert.True(vm.IsMemberListEmpty);
        });
    }

    // ── 权限矩阵命令 CanExecute（Req 7.x，§8.1.1） ──

    [Fact]
    public void DeleteAndDisable_enabled_for_owner_selecting_other_member()
    {
        RunOnSta(() =>
        {
            var session = OwnerSession("owner-1");
            var vm = new MeProfileViewModel(accountService: new FakeAccountService(), sessionContext: session);

            var target = NewMember("m-1", "alice", "爱丽丝");
            vm.ManagedAccounts.Add(target);
            vm.SelectedManagedAccount = target;

            // Owner 选中他人 Member → 删除 / 停用均可执行。
            Assert.True(vm.DeleteMemberCommand.CanExecute(null));
            Assert.True(vm.DisableMemberCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Delete_disabled_for_non_owner()
    {
        RunOnSta(() =>
        {
            var session = MemberSession("m-self");
            var vm = new MeProfileViewModel(accountService: new FakeAccountService(), sessionContext: session);

            var target = NewMember("m-1", "alice", "爱丽丝");
            vm.ManagedAccounts.Add(target);
            vm.SelectedManagedAccount = target;

            // 非 Owner → 删除不可执行。
            Assert.False(vm.DeleteMemberCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Delete_disabled_but_disable_enabled_for_owner_selecting_self()
    {
        RunOnSta(() =>
        {
            var session = OwnerSession("owner-1");
            var vm = new MeProfileViewModel(accountService: new FakeAccountService(), sessionContext: session);

            // 选中自身（Owner 自身账号）。
            var self = new LocalAccountSummary
            {
                AccountId = "owner-1",
                Username = "owner",
                DisplayName = "店主",
                Role = LocalAccountRole.Owner,
                IsEnabled = true,
            };
            vm.ManagedAccounts.Add(self);
            vm.SelectedManagedAccount = self;

            // 任何人不可删自身 → 删除不可执行；Owner 可停用自身 → 停用可执行。
            Assert.False(vm.DeleteMemberCommand.CanExecute(null));
            Assert.True(vm.DisableMemberCommand.CanExecute(null));
        });
    }

    // ── 审计空 / 失败降级（Req 9.3 / 9.4） ──

    [Fact]
    public void Audit_empty_when_service_returns_no_records()
    {
        RunOnSta(() =>
        {
            var audit = new FakeAuditService { Result = Array.Empty<SecurityAuditEntry>() };
            var vm = new MeProfileViewModel(securityAudit: audit);

            vm.LoadSecurityAuditAsync().GetAwaiter().GetResult();

            Assert.True(vm.IsSecurityAuditAvailable);
            Assert.False(vm.IsAuditLoadFailed);
            Assert.True(vm.IsAuditEmpty);
            Assert.Empty(vm.SecurityAuditEntries);
            Assert.Equal("所选范围内暂无安全记录", vm.AuditStatus);
        });
    }

    [Fact]
    public void Audit_load_failed_degrades_and_clears_list()
    {
        RunOnSta(() =>
        {
            var audit = new FakeAuditService { ShouldThrow = true };
            var vm = new MeProfileViewModel(securityAudit: audit);

            // 预置一条残留记录，验证读取失败后列表被清空（不渲染半截列表）。
            vm.SecurityAuditEntries.Add(new SecurityAuditEntry(DateTime.UtcNow, "LoginSucceeded", "owner", "stale"));

            vm.LoadSecurityAuditAsync().GetAwaiter().GetResult();

            Assert.True(vm.IsAuditLoadFailed);
            Assert.False(vm.IsAuditEmpty);
            Assert.Empty(vm.SecurityAuditEntries);
            Assert.Equal("安全记录读取失败", vm.AuditStatus);
        });
    }

    // ── 凭证修改命令完成后清空输入（Req 8.8 / P4） ──

    [Fact]
    public void MasterPasswordChange_success_clears_inputs()
    {
        RunOnSta(() =>
        {
            var account = new FakeAccountService();
            var vm = new MeProfileViewModel(accountService: account);

            vm.CurrentMasterPasswordInput = "old-password";
            vm.NewCurrentMasterPasswordInput = "newpassword1";
            vm.ConfirmNewMasterPasswordInput = "newpassword1";

            Assert.True(vm.MasterPasswordValidation.CanSubmit);

            vm.ChangeCurrentMasterPasswordCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.True(account.MasterPasswordChanged);
            Assert.Equal(string.Empty, vm.CurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.NewCurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.ConfirmNewMasterPasswordInput);
        });
    }

    [Fact]
    public void MasterPasswordChange_failure_still_clears_inputs()
    {
        RunOnSta(() =>
        {
            var account = new FakeAccountService { ShouldThrow = true };
            var vm = new MeProfileViewModel(accountService: account);

            vm.CurrentMasterPasswordInput = "old-password";
            vm.NewCurrentMasterPasswordInput = "newpassword1";
            vm.ConfirmNewMasterPasswordInput = "newpassword1";

            vm.ChangeCurrentMasterPasswordCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 命令失败也应清空凭证输入，确保明文不残留（P4）。
            Assert.Equal(string.Empty, vm.CurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.NewCurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.ConfirmNewMasterPasswordInput);
        });
    }

    [Fact]
    public void PinChange_success_clears_inputs()
    {
        RunOnSta(() =>
        {
            var account = new FakeAccountService();
            var vm = new MeProfileViewModel(accountService: account);

            vm.CurrentPinInput = "111111";
            vm.NewCurrentPinInput = "654321";
            vm.ConfirmNewPinInput = "654321";

            Assert.True(vm.PinValidation.CanSubmit);

            vm.ChangeCurrentPinCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.True(account.PinChanged);
            Assert.Equal(string.Empty, vm.CurrentPinInput);
            Assert.Equal(string.Empty, vm.NewCurrentPinInput);
            Assert.Equal(string.Empty, vm.ConfirmNewPinInput);
        });
    }

    [Fact]
    public void PinChange_failure_still_clears_inputs()
    {
        RunOnSta(() =>
        {
            var account = new FakeAccountService { ShouldThrow = true };
            var vm = new MeProfileViewModel(accountService: account);

            vm.CurrentPinInput = "111111";
            vm.NewCurrentPinInput = "654321";
            vm.ConfirmNewPinInput = "654321";

            vm.ChangeCurrentPinCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.Equal(string.Empty, vm.CurrentPinInput);
            Assert.Equal(string.Empty, vm.NewCurrentPinInput);
            Assert.Equal(string.Empty, vm.ConfirmNewPinInput);
        });
    }

    // ── helpers / fakes ──

    private static LocalAccountSummary NewMember(string id, string username, string displayName) => new()
    {
        AccountId = id,
        Username = username,
        DisplayName = displayName,
        Role = LocalAccountRole.Member,
        IsEnabled = true,
    };

    private static FakeSessionContext OwnerSession(string accountId) => new(new LocalSessionContext
    {
        AccountId = accountId,
        Username = "owner",
        DisplayName = "店主",
        Role = LocalAccountRole.Owner,
    });

    private static FakeSessionContext MemberSession(string accountId) => new(new LocalSessionContext
    {
        AccountId = accountId,
        Username = "member",
        DisplayName = "店员",
        Role = LocalAccountRole.Member,
    });

    /// <summary>提供固定当前会话的会话上下文替身（权限模式恒为 Normal）。</summary>
    private sealed class FakeSessionContext : ISessionContextService
    {
        public FakeSessionContext(LocalSessionContext current) => Current = current;

        public event EventHandler? SessionChanged;

        public LocalSessionContext? Current { get; }
        public bool IsSignedIn => Current is not null;
        public bool IsDataKeyAvailable => false;
        public SessionPermissionMode CurrentPermissionMode => SessionPermissionMode.Normal;

        public void SetCurrent(LocalSessionContext session) => SessionChanged?.Invoke(this, EventArgs.Empty);
        public void SuspendDataKey() { }
        public bool TryRestoreDataKey(string accountId) => false;
        public void Clear() { }
        public void SetPermissionMode(SessionPermissionMode mode) { }
    }

    /// <summary>可配置成功 / 抛异常的安全审计读取替身。</summary>
    private sealed class FakeAuditService : ISecurityAuditService
    {
        public bool ShouldThrow { get; set; }
        public IReadOnlyList<SecurityAuditEntry> Result { get; set; } = Array.Empty<SecurityAuditEntry>();

        public SecurityAuditRecord Record(SecurityEventType eventType, string? subject, SecurityEventOutcome outcome)
            => throw new NotSupportedException();

        public IReadOnlyList<SecurityAuditRecord> GetRecords() => Array.Empty<SecurityAuditRecord>();

        public bool VerifyChainIntegrity() => true;

        public Task RecordAsync(SecurityAuditEventKind kind, string accountLabel, string detail, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(
            string? accountLabel = null,
            DateTime? from = null,
            DateTime? to = null,
            CancellationToken ct = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("simulated audit read failure");
            }

            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// 账号管理服务替身：仅实现凭证修改两条路径（可配置成功 / 抛异常），其余成员未在本测试范围内调用。
    /// </summary>
    private sealed class FakeAccountService : ILocalAccountManagementService
    {
        public bool ShouldThrow { get; set; }
        public bool MasterPasswordChanged { get; private set; }
        public bool PinChanged { get; private set; }

        public Task ChangeCurrentMasterPasswordAsync(string currentMasterPassword, string newMasterPassword, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("simulated master password change failure");
            }

            MasterPasswordChanged = true;
            return Task.CompletedTask;
        }

        public Task ChangeCurrentPinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("simulated pin change failure");
            }

            PinChanged = true;
            return Task.CompletedTask;
        }

        // ── 本测试范围外的成员：不会被调用 ──
        public Task<IReadOnlyList<LocalAccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LocalAccountSummary>> ListAccountDirectoryAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LocalAccountSummary> CreateMemberAsync(CreateMemberAccountRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task VerifyOwnerCredentialsAsync(string ownerUsername, string ownerMasterPassword, string ownerPin, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LocalAccountSummary> CreateMemberWithOwnerVerificationAsync(string ownerUsername, string ownerMasterPassword, string ownerPin, CreateMemberAccountRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DisableMemberAsync(string memberAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteMemberAsync(string memberAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAccountAsync(string ownerUsername, string ownerMasterPassword, string ownerPin, string targetAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResetMemberMasterPasswordAsync(string memberAccountId, string newMasterPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task VerifyMemberPasswordResetAsync(string memberUsername, string memberPin, string ownerUsername, string ownerMasterPassword, string ownerPin, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResetMemberMasterPasswordWithOwnerVerificationAsync(string memberUsername, string memberPin, string ownerUsername, string ownerMasterPassword, string ownerPin, string newMasterPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResetMemberPinAsync(string memberAccountId, string newPin, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ResetOwnerMasterPasswordWithRecoveryKeyAsync(string ownerUsername, string ownerPin, string recoveryKey, string newMasterPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task VerifyOwnerPasswordRecoveryAsync(string ownerUsername, string ownerPin, string recoveryKey, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
