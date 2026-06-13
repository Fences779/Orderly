using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using Orderly.App.Services;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Integration;

/// <summary>
/// 任务 21.2：设置页与我的页精装升级的<b>端到端集成测试</b>。
///
/// <para>覆盖六类业务流程闭环（每类至少 1~2 个 <c>[Fact]</c>）：</para>
/// <list type="number">
///   <item>改设置 → 离开页成功放行（+「设置已保存」）/ 失败阻止（+ 人话 + 错误码 Toast）（Req 3.2、3.8）。</item>
///   <item>搜索 → 激活命中跳转分类 + 定位锚点高亮信号 + 超限提示（Req 2.3）。</item>
///   <item>头像上传 → 持久化引用键 → 模拟重启后从偏好恢复头像（Req 6.4）。</item>
///   <item>凭证修改 → 审计写入且无明文 + 主密码改强制登出 / PIN 改锁定（Req 12.2、14.3、16.1）。</item>
///   <item>删除成员 → 登录账号被移除并记 <c>MemberDeleted</c> 安全审计（Req 12.2）。</item>
///   <item>敏感页面 → PIN 门禁拦截 / 放行 / 受限模式短路（Req 18.1）。</item>
/// </list>
///
/// <para><b>遗留绑定限制说明</b>：设置页内容字段当前仍绑 <c>MainViewModel</c> 旧 <c>Settings*</c> 状态，
/// 尚未整体改绑到 <see cref="SettingsViewModel"/> 自动保存，因此<b>真实 UI 端到端不可行</b>。本测试在
/// <b>ViewModel / 服务集成层</b>组合多个真实 VM 与真实服务（真实 <see cref="SettingsViewModel"/> 自动保存引擎 +
/// 真实 <see cref="AppSettingRepository"/>、真实 <see cref="AvatarStorageService"/>、真实
/// <see cref="SecurityAuditService"/>、真实 <see cref="LocalAccountManagementService"/>、真实
/// <see cref="CredentialChangeSessionCoordinator"/>、真实 <see cref="SensitivePageGuard"/>），仅在 UI/会话
/// 边界处使用记录型替身，覆盖上述流程闭环。这与既有测试范式一致。</para>
///
/// <para>涉及 <c>CollectionViewSource</c> / WPF 成像的 <see cref="MeProfileViewModel"/> 构造须在 STA 线程，
/// 沿用 <see cref="RunOnSta"/> 包装并将断言异常透传回测试线程。</para>
///
/// **Validates: Requirements 3.2, 3.8, 2.3, 6.4, 12.2, 14.3, 16.1, 18.1**
/// </summary>
public sealed class EndToEndIntegrationTests
{
    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 1：改设置 → 离开页成功放行 / 失败阻止并人话 Toast（Req 3.2、3.8）
    // 组合真实 SettingsViewModel 自动保存引擎 + 真实 AppSettingRepository（成功）/ 失败仓储 + 记录型 Toast。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Scenario1_ChangeSetting_then_leave_with_successful_save_allows_navigation_and_persists()
    {
        using var db = new SettingsDatabase();
        var toast = new RecordingToastService();
        var repository = new AppSettingRepository(new SqliteConnectionFactory(db.Path));
        var vm = new SettingsViewModel(settingRepository: repository, toast: toast);

        // 改一个即改即存字段 → 触发真实防抖自动保存 → flush 至真实落盘。
        var toggled = !vm.MaskPhoneByDefaultInput;
        vm.MaskPhoneByDefaultInput = toggled;
        await vm.FlushPendingAutoSaveAsync();

        // Req 3.2：旧值非「设置」不构成离开设置页 → 放行且不弹、不消费保存结果。
        bool notLeaving = await vm.TryLeaveSettingsAsync(MainViewModel.SectionWorkbench, MainViewModel.SectionMe);
        Assert.True(notLeaving);
        Assert.Empty(toast.Calls);

        // Req 3.2 / 3.3：旧=设置且新≠设置 + 最近一次成功 → 放行 + 弹一次「设置已保存」+ 清空结果。
        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);
        Assert.True(allowed);
        Assert.Single(toast.Calls);
        Assert.Equal("设置已保存", toast.Calls[0].Message);
        Assert.Equal(ToastSeverity.Success, toast.Calls[0].Severity);
        Assert.Null(vm.LastSaveOutcome);

        // 端到端持久化校验：用全新仓储实例读回，确认改动真实落盘（自动保存语义不变）。
        var reloaded = await new AppSettingRepository(new SqliteConnectionFactory(db.Path)).GetPreferencesAsync();
        Assert.Equal(toggled, reloaded.MaskPhoneByDefault);
    }

    [Fact]
    public async Task Scenario1_ChangeSetting_then_leave_with_failed_save_blocks_navigation_with_humanized_toast()
    {
        var toast = new RecordingToastService();
        // 持久化恒抛 IOException → 失败归类为 SET-1001（持久化）。
        var vm = new SettingsViewModel(settingRepository: new FailingAppSettingRepository(), toast: toast);

        vm.MaskPhoneByDefaultInput = !vm.MaskPhoneByDefaultInput;
        await vm.FlushPendingAutoSaveAsync();
        Assert.True(vm.LastSaveOutcome is { Success: false });

        // Req 3.8：最近一次失败 → 阻止离开（返回 false）+ 弹人话 + 错误码 Error Toast + 保留结果以便再拦截。
        bool allowed = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionMe);

        Assert.False(allowed);
        Assert.Single(toast.Calls);
        Assert.Equal(ToastSeverity.Error, toast.Calls[0].Severity);
        // 人话主体 + 错误码括注均可见（不泄露内部异常类型名 / 堆栈）。
        Assert.Contains("（错误码：SET-1001）", toast.Calls[0].Message);
        Assert.DoesNotContain("IOException", toast.Calls[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.LastSaveOutcome is { Success: false });

        // 再次离开仍被阻止并重新弹出失败警示（结果未被消费）。
        bool allowedAgain = await vm.TryLeaveSettingsAsync(MainViewModel.SectionSettings, MainViewModel.SectionCashflow);
        Assert.False(allowedAgain);
        Assert.Equal(2, toast.Calls.Count);
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 2：搜索 → 跳转分类 + 定位高亮 + 超限提示（Req 2.3）
    // 组合真实 SettingsViewModel 搜索接线 + 真实 SettingsSearchIndex。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario2_Search_then_activate_result_switches_category_and_emits_scroll_anchor()
    {
        var vm = new SettingsViewModel(searchIndex: new SettingsSearchIndex());

        // 输入查询 → 真实索引过滤 → 命中结果填充。
        vm.SettingsSearchQuery = "备份";
        Assert.NotEmpty(vm.SearchResults);

        var hit = vm.SearchResults[0];
        // 命中条目分类闭合 + 锚点非空（Req 2.3 的前置不变式）。
        Assert.False(string.IsNullOrWhiteSpace(hit.CategoryKey));
        Assert.False(string.IsNullOrWhiteSpace(hit.AnchorId));

        // 激活命中 → 切换到所属分类 + 暴露待滚动锚点（命中跳转 + 定位高亮信号，Req 2.3）。
        vm.ActivateSearchResultCommand.Execute(hit);

        Assert.Equal(hit.CategoryKey, vm.SelectedCategoryKey);
        Assert.Equal(hit.AnchorId, vm.PendingScrollAnchorId);
    }

    [Fact]
    public void Scenario2_Search_over_limit_sets_truncation_hint_and_empty_query_does_not_navigate()
    {
        var vm = new SettingsViewModel(searchIndex: new SettingsSearchIndex());

        // 高频命中查询 → 原始命中超过 12 条上限 → 置位「结果超限」提示信号（Req 2.8，配合 2.3）。
        vm.SettingsSearchQuery = "a";
        Assert.Equal(SettingsSearchIndex.MaxResults, vm.SearchResults.Count);
        Assert.True(vm.IsSearchResultsTruncated);

        // 空查询 → 空结果、不触发任何跳转 / 滚动 / 高亮（Req 2.7）。
        vm.SettingsSearchQuery = "   ";
        Assert.Empty(vm.SearchResults);
        Assert.False(vm.IsSearchResultsTruncated);

        vm.ActivateSearchResultCommand.Execute(null);
        Assert.Null(vm.PendingScrollAnchorId);
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 3：头像上传 → 重启保留（Req 6.4）
    // 组合真实 AvatarStorageService + 真实 AppSettingRepository + 真实 MeProfileViewModel（STA）。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario3_UploadAvatar_persists_reference_and_survives_app_restart()
    {
        RunOnSta(() =>
        {
            using var workspace = new AvatarWorkspace();
            const string accountId = "owner1";

            var avatarService = new AvatarStorageService(() => workspace.AppRoot);
            var session = new StubSessionContext(NewSession(accountId, LocalAccountRole.Owner));

            // ── 第一次会话：上传头像 ──
            var settingRepository = new AppSettingRepository(new SqliteConnectionFactory(workspace.DbPath));
            var vm1 = new MeProfileViewModel(
                avatarService: avatarService,
                sessionContext: session,
                settingRepository: settingRepository)
            {
                // 注入文件选择委托（替代 OpenFileDialog），返回真实生成的 PNG。
                PickAvatarFile = () => workspace.WritePng(64, 64),
            };

            vm1.ChangeAvatarCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 即时刷新：头像图源已加载（Req 6.3）。
            Assert.NotNull(vm1.AvatarImageSource);

            // 引用键已持久化到偏好（Req 6.3，经现有 KV upsert 通道）。
            var persisted = settingRepository.GetPreferencesAsync().GetAwaiter().GetResult();
            Assert.Equal($"avatars/{accountId}.png", persisted.AvatarReference);

            // ── 模拟应用重启：全新 VM + 全新仓储实例，从偏好恢复头像（Req 6.4）──
            var restartRepository = new AppSettingRepository(new SqliteConnectionFactory(workspace.DbPath));
            var vm2 = new MeProfileViewModel(
                avatarService: avatarService,
                sessionContext: session,
                settingRepository: restartRepository);

            Assert.Null(vm2.AvatarImageSource); // 加载前为占位。
            vm2.LoadAvatarFromPreferencesAsync().GetAwaiter().GetResult();

            // 重启后已保存的 AvatarReference 被加载并显示头像。
            Assert.NotNull(vm2.AvatarImageSource);
        });
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 4：凭证修改 → 审计写入且无明文 + 主密码改强制登出 / PIN 改锁定（Req 12.2、14.3、16.1）
    // 组合真实 MeProfileViewModel 凭证命令 + 真实 CredentialChangeSessionCoordinator + 真实审计链 + 记录型锁定。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Scenario4_MasterPasswordChange_forces_logout_and_audits_without_plaintext()
    {
        RunOnSta(() =>
        {
            var lockService = new RecordingSessionLockService();
            var session = new StubSessionContext(NewSession("owner1", LocalAccountRole.Owner));
            var audit = new SecurityAuditService(); // 真实内存防篡改审计链。
            var coordinator = new CredentialChangeSessionCoordinator(lockService, session, audit);
            var account = new CredentialFakeAccountService();

            var vm = new MeProfileViewModel(
                accountService: account,
                sessionContext: session,
                credentialSession: coordinator);

            const string secret = "PLAINTEXT_NEWPASS_x9";
            vm.CurrentMasterPasswordInput = "old-password";
            vm.NewCurrentMasterPasswordInput = secret;
            vm.ConfirmNewMasterPasswordInput = secret;
            Assert.True(vm.MasterPasswordValidation.CanSubmit);

            vm.ChangeCurrentMasterPasswordCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            // 后端凭证修改被调用。
            Assert.True(account.MasterPasswordChanged);

            // Req 16.1：主密码改成功 → 强制登出（Logout + 会话清空），不锁定。
            Assert.Equal(1, lockService.LogoutCount);
            Assert.Equal(1, session.ClearCount);
            Assert.Equal(0, lockService.LockManuallyCount);
            Assert.Equal(SessionLockState.LoggedOut, lockService.State);

            // Req 12.2 / 16.4：成功路径恰好记一条凭证变更审计。
            Assert.Single(audit.GetRecords());
            Assert.Equal(SecurityEventType.CredentialChange, audit.GetRecords()[0].EventType);
            Assert.True(audit.VerifyChainIntegrity());

            // Req 14.3 / 8.8 / P4：命令完成后清空凭证输入，明文不残留；审计主体经哈希、绝不含明文。
            Assert.Equal(string.Empty, vm.CurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.NewCurrentMasterPasswordInput);
            Assert.Equal(string.Empty, vm.ConfirmNewMasterPasswordInput);
            foreach (var record in audit.GetRecords())
            {
                Assert.DoesNotContain(secret, record.SubjectIdentifier, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public void Scenario4_PinChange_locks_session_pending_unlock_and_audits()
    {
        RunOnSta(() =>
        {
            var lockService = new RecordingSessionLockService();
            var session = new StubSessionContext(NewSession("owner1", LocalAccountRole.Owner));
            var audit = new SecurityAuditService();
            var coordinator = new CredentialChangeSessionCoordinator(lockService, session, audit);
            var account = new CredentialFakeAccountService();

            var vm = new MeProfileViewModel(
                accountService: account,
                sessionContext: session,
                credentialSession: coordinator);

            vm.CurrentPinInput = "111111";
            vm.NewCurrentPinInput = "654321";
            vm.ConfirmNewPinInput = "654321";
            Assert.True(vm.PinValidation.CanSubmit);

            vm.ChangeCurrentPinCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.True(account.PinChanged);

            // Req 16.2：PIN 改成功 → 锁定进入 PendingPinUnlock，不登出、不清空会话。
            Assert.Equal(1, lockService.LockManuallyCount);
            Assert.Equal(0, lockService.LogoutCount);
            Assert.Equal(0, session.ClearCount);
            Assert.Equal(SessionLockState.PendingPinUnlock, lockService.State);

            // Req 12.2 / 16.4：恰好记一条凭证变更审计。
            Assert.Single(audit.GetRecords());
            Assert.Equal(SecurityEventType.CredentialChange, audit.GetRecords()[0].EventType);

            // Req 14.3：命令完成后清空 PIN 输入。
            Assert.Equal(string.Empty, vm.CurrentPinInput);
            Assert.Equal(string.Empty, vm.NewCurrentPinInput);
            Assert.Equal(string.Empty, vm.ConfirmNewPinInput);
        });
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 5：删除成员 → 账号移除并记 MemberDeleted（Req 12.2）
    // 组合真实 LocalAccountManagementService + 内存账户仓储 + 真实持久化 SecurityAuditService（加密库）。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Scenario5_DeleteMember_removes_login_account_and_records_MemberDeleted_audit()
    {
        using var auditDb = new EncryptedDatabase();
        var owner = MakeOwner();
        var member = MakeMember(owner.AccountId);
        var repository = new InMemoryLocalAccountRepository(new[] { owner, member });

        var session = new FakeDataKeySessionContextService(new byte[32]);
        session.SetCurrent(new LocalSessionContext
        {
            AccountId = owner.AccountId,
            Username = owner.Username,
            DisplayName = owner.DisplayName,
            Role = LocalAccountRole.Owner,
            DatabasePath = owner.DatabasePath,
            DataKey = new byte[32],
        });

        // 真实持久化审计服务，写入会话数据密钥加密的本地库（追加式 + 链式完整性，防篡改）。
        var audit = new SecurityAuditService(() => auditDb.Factory());
        var service = new LocalAccountManagementService(
            repository,
            session,
            securityAuditService: audit);

        await service.DeleteMemberAsync(member.AccountId);

        // 删除=移除登录账号本身：仓储与目录均查询不到该账号。
        Assert.Null(await repository.GetByAccountIdAsync(member.AccountId));
        Assert.DoesNotContain(await repository.ListAsync(), a => a.AccountId == member.AccountId);

        // Req 12.2：恰好记录一条 MemberDeleted 安全审计（用全新实例读取，确保真实持久化往返）。
        var entries = await new SecurityAuditService(() => auditDb.Factory()).QueryAsync();
        var deleted = Assert.Single(entries, e => e.Kind == nameof(SecurityAuditEventKind.MemberDeleted));

        // 审计仅含账号标签 + 脱敏 detail，绝不含明文凭证；持久化链完整防篡改。
        Assert.DoesNotContain("password", deleted.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pin", deleted.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(await new SecurityAuditService(() => auditDb.Factory()).VerifyPersistedChainIntegrityAsync());
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // 场景 6：敏感页面 → PIN 门禁拦截（Req 18.1）
    // 组合真实 SensitivePageGuard + 记录型会话上下文 + PIN 校验认证替身。
    // ════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Scenario6_SensitivePageGuard_blocks_wrong_pin_and_grants_correct_pin()
    {
        const string correctPin = "246813";
        var session = new StubSessionContext(NewSession("owner1", LocalAccountRole.Owner));
        var auth = new PinStubAuthService("owner1", correctPin);
        var guard = new SensitivePageGuard(session, auth);

        // Req 18.1：进入现金流前 PIN 错误 → 拒绝（机密内容由调用方据此恒不渲染）。
        var rejected = await guard.TryEnterAsync(MainViewModel.SectionCashflow, correctPin + "9");
        Assert.Equal(SensitiveAccessResult.PinRejected, rejected);

        // Req 18.1：PIN 正确 → 放行进入。
        var granted = await guard.TryEnterAsync(MainViewModel.SectionCashflow, correctPin);
        Assert.Equal(SensitiveAccessResult.Granted, granted);
    }

    [Fact]
    public async Task Scenario6_SensitivePageGuard_short_circuits_in_restricted_mode()
    {
        const string correctPin = "246813";
        var session = new StubSessionContext(NewSession("owner1", LocalAccountRole.Owner));
        session.SetPermissionMode(SessionPermissionMode.Restricted_Permission);
        var auth = new PinStubAuthService("owner1", correctPin);
        var guard = new SensitivePageGuard(session, auth);

        // Req 18.4 / 17.4：受限模式恒拒绝机密页面，先于 PIN 校验短路（即便提交正确 PIN）。
        var blocked = await guard.TryEnterAsync(MainViewModel.SectionBusinessAdvice, correctPin);
        Assert.Equal(SensitiveAccessResult.BlockedByRestricted, blocked);
    }

    // ════════════════════════════════════════════════════════════════════════════════════
    // STA 包装与测试夹具
    // ════════════════════════════════════════════════════════════════════════════════════

    /// <summary>在 STA 线程上执行测试体（构造 WPF 依赖的 VM / 成像所需），并将断言异常透传回测试线程。</summary>
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

    private static LocalSessionContext NewSession(string accountId, LocalAccountRole role) => new()
    {
        AccountId = accountId,
        Username = accountId,
        DisplayName = accountId,
        Role = role,
    };

    private static LocalAccount MakeOwner() => new()
    {
        AccountId = Guid.NewGuid().ToString("N"),
        Username = "owner",
        DisplayName = "Owner",
        Role = LocalAccountRole.Owner,
        IsEnabled = true,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now,
    };

    private static LocalAccount MakeMember(string ownerAccountId) => new()
    {
        AccountId = Guid.NewGuid().ToString("N"),
        Username = "member",
        DisplayName = "Member",
        Role = LocalAccountRole.Member,
        IsEnabled = true,
        AdminOwnerAccountId = ownerAccountId,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now,
    };

    // ── 一次性临时数据库 / 工作区 ─────────────────────────────────────────────────────────

    /// <summary>未加密临时设置库（含 <c>AppSettings</c> KV 表），用于真实 <see cref="AppSettingRepository"/> 往返。</summary>
    private sealed class SettingsDatabase : IDisposable
    {
        public SettingsDatabase()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"orderly-e2e-settings-{Guid.NewGuid():N}.db");

            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = Path }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { Path, Path + "-wal", Path + "-shm", Path + "-journal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>加密临时库（SQLCipher，32 字节密钥），用于真实持久化 <see cref="SecurityAuditService"/>。</summary>
    private sealed class EncryptedDatabase : IDisposable
    {
        private readonly string _dir;
        private readonly string _dbPath;
        private readonly byte[] _key;

        public EncryptedDatabase()
        {
            _dir = Path.Combine(Path.GetTempPath(), "orderly-e2e-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dbPath = Path.Combine(_dir, "audit.db");
            _key = Enumerable.Range(0, 32).Select(i => (byte)(i + 7)).ToArray();
        }

        public SqliteConnectionFactory Factory() => new(_dbPath, () => (byte[])_key.Clone());

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>头像测试工作区：隔离的 app 数据根目录（服务输出）+ 设置库 + 源图片目录，结束时清理。</summary>
    private sealed class AvatarWorkspace : IDisposable
    {
        private readonly string _root;

        public AvatarWorkspace()
        {
            _root = Path.Combine(Path.GetTempPath(), "orderly-e2e-avatar", Guid.NewGuid().ToString("N"));
            AppRoot = Path.Combine(_root, "app");
            SourceDir = Path.Combine(_root, "src");
            Directory.CreateDirectory(AppRoot);
            Directory.CreateDirectory(SourceDir);

            DbPath = Path.Combine(_root, "settings.db");
            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        public string AppRoot { get; }

        public string SourceDir { get; }

        public string DbPath { get; }

        /// <summary>生成一张真实可解码的 PNG 源图片，返回其路径。</summary>
        public string WritePng(int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int stride = w * 4;
            var pixels = new byte[h * stride];
            new Random(11).NextBytes(pixels);
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF; // 不透明 alpha
            }

            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var path = Path.Combine(SourceDir, Guid.NewGuid().ToString("N") + ".png");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fs);
            return path;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // ── 记录型 / 可配置替身（UI 与会话边界）──────────────────────────────────────────────

    /// <summary>记录每次 <see cref="Show"/> 调用的 Toast 替身，用于断言弹出次数 / 内容 / 严重级。</summary>
    private sealed class RecordingToastService : IToastService
    {
        public List<(string Message, ToastSeverity Severity)> Calls { get; } = new();

        public void Show(string message, ToastSeverity severity = ToastSeverity.Info, TimeSpan? duration = null)
            => Calls.Add((message, severity));
    }

    /// <summary>保存恒抛 <see cref="IOException"/> 的偏好仓储，使失败归类为 SET-1001（持久化）。</summary>
    private sealed class FailingAppSettingRepository : IAppSettingRepository
    {
        public Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AppPreferences());

        public Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
            => throw new IOException("simulated persistence failure");

        public Task UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>提供固定当前会话、记录 <see cref="Clear"/> 调用、可配置权限模式的会话上下文替身。</summary>
    private sealed class StubSessionContext : ISessionContextService
    {
        private LocalSessionContext? _current;
        private SessionPermissionMode _mode = SessionPermissionMode.Normal;

        public StubSessionContext(LocalSessionContext current) => _current = current;

        public event EventHandler? SessionChanged;

        public int ClearCount { get; private set; }
        public LocalSessionContext? Current => _current;
        public bool IsSignedIn => _current is not null;
        public bool IsDataKeyAvailable => false;
        public SessionPermissionMode CurrentPermissionMode => _mode;
        public bool IsRestrictedPermissionMode => _mode == SessionPermissionMode.Restricted_Permission;

        public void SetCurrent(LocalSessionContext session)
        {
            _current = session;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SuspendDataKey() { }
        public bool TryRestoreDataKey(string accountId) => false;

        public void Clear()
        {
            ClearCount++;
            _current = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPermissionMode(SessionPermissionMode mode)
        {
            _mode = mode;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>记录 <see cref="LockManually"/> / <see cref="Logout"/> 调用并维护派生锁定状态的替身。</summary>
    private sealed class RecordingSessionLockService : ISessionLockService
    {
        public event EventHandler<SessionLockState>? LockStateChanged;

        public int LockManuallyCount { get; private set; }
        public int LogoutCount { get; private set; }
        public SessionLockState State { get; private set; } = SessionLockState.Unlocked;
        public bool IsPinRequired => State == SessionLockState.PendingPinUnlock;

        public void MarkSignedIn() => SetState(SessionLockState.Unlocked);
        public void LockBySystemResume() => SetState(SessionLockState.PendingPinUnlock);

        public void LockManually()
        {
            LockManuallyCount++;
            SetState(SessionLockState.PendingPinUnlock);
        }

        public void UnlockWithPin(bool verified)
        {
            if (verified)
            {
                SetState(SessionLockState.Unlocked);
            }
        }

        public void Logout()
        {
            LogoutCount++;
            SetState(SessionLockState.LoggedOut);
        }

        private void SetState(SessionLockState next)
        {
            State = next;
            LockStateChanged?.Invoke(this, next);
        }
    }

    /// <summary>仅实现凭证修改两条路径（恒成功）的账号管理替身；其余成员不在本场景调用范围内。</summary>
    private sealed class CredentialFakeAccountService : ILocalAccountManagementService
    {
        public bool MasterPasswordChanged { get; private set; }
        public bool PinChanged { get; private set; }

        public Task ChangeCurrentMasterPasswordAsync(string currentMasterPassword, string newMasterPassword, CancellationToken cancellationToken = default)
        {
            MasterPasswordChanged = true;
            return Task.CompletedTask;
        }

        public Task ChangeCurrentPinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
        {
            PinChanged = true;
            return Task.CompletedTask;
        }

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

    /// <summary>以相等比较模拟 PIN 校验的认证替身；明文仅透传比较、不留存。</summary>
    private sealed class PinStubAuthService : ILocalAuthService
    {
        private readonly string _accountId;
        private readonly string _correctPin;

        public PinStubAuthService(string accountId, string correctPin)
        {
            _accountId = accountId;
            _correctPin = correctPin;
        }

        public Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
            => Task.FromResult(
                string.Equals(accountId, _accountId, StringComparison.Ordinal)
                && string.Equals(pin, _correctPin, StringComparison.Ordinal));

        public Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<LegacyDatabaseMigrationPlan> BuildLegacyMigrationPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CreateFirstOwnerResult> CreateFirstOwnerAsync(CreateFirstOwnerRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LocalSignInResult> SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
