using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Security;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property-based test for sensitive-page PIN gating + restricted-mode confidential protection
/// (design §9.7 / §9.8 / Req 17 / Req 18 / §11 Property 14).
///
/// <para><b>Property 14: 敏感页面 PIN 门禁与受限模式机密保护.</b>
/// 对任意进入现金流等敏感页面的尝试：
/// <list type="bullet">
///   <item>进入成功（<see cref="SensitiveAccessResult.Granted"/>） ⟺
///     （PIN 正确 ∧ 当前会话非受限模式），且仅 <c>Granted</c> 才渲染机密内容（Req 18.1 / 18.3）。</item>
///   <item>处于 <see cref="SessionPermissionMode.Restricted_Permission"/> →
///     恒 <see cref="SensitiveAccessResult.BlockedByRestricted"/>，先于 PIN 校验短路、无论 PIN 对错
///     （Req 18.4 / 17.4），且机密内容恒不渲染。</item>
///   <item>非受限 + PIN 错误 → <see cref="SensitiveAccessResult.PinRejected"/>，机密内容不渲染（Req 18.2 / 18.3）。</item>
///   <item>受限模式白名单：<see cref="RestrictedModePolicy.IsOperationAllowedInRestrictedMode"/>
///     仅对数据备份 / 导出导入恢复返回 <see langword="true"/>，机密 / 高危操作恒返回 <see langword="false"/>
///     （Req 17.3 / 17.4）。</item>
///   <item>被停用 Owner 紧急启用：正确 6 位 PIN → 进入受限模式（Req 17.1）；错误 PIN → 拒绝且权限模式不变
///     （Req 17.2）；成功 / 失败均记审计且绝不含明文 PIN（Req 17.5 / 18.5）。</item>
/// </list></para>
///
/// <para>协作依赖（<see cref="ISessionContextService"/> / <see cref="ILocalAuthService"/> /
/// <see cref="ISecurityAuditService"/> / <see cref="ILocalAccountRepository"/>）均以记录调用的 fake 替身实现，
/// 断言基于独立重算的期望，不读取被测产物，避免循环论证。</para>
///
/// **Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5, 17.1, 17.2, 17.3, 17.4, 17.5**
/// </summary>
public sealed class SensitivePageGuardRestrictedModePropertyTests
{
    private const string FixedAccountId = "acct-fixed-id";

    // 明文 PIN 以独特哨兵前缀生成，确保不会与账号 ID 偶然子串碰撞——
    // 任何包含该前缀的字符串出现在审计沉淀中都意味着明文真的泄露了（Req 18.5 / 17.5）。
    private const string PinSentinelPrefix = "PINSECRET_";

    private static readonly Gen<string> CorrectPinGen =
        from body in Gen.Char['0', '9'].Array[6, 6].Select(chars => new string(chars))
        select PinSentinelPrefix + body;

    /// <summary>页面键无关门禁结果，随机抽样若干典型机密页面键以增强覆盖。</summary>
    private static readonly Gen<string> PageKeyGen =
        Gen.OneOfConst("Cashflow", "BusinessAdvice", "FinanceReport");

    // ----------------------------------------------------------------------------------
    // Facet A: 门禁结果 ⟺ (PIN 正确 ∧ 非受限)，仅 Granted 才渲染机密内容（Req 18.1/18.2/18.3）
    // ----------------------------------------------------------------------------------
    private static readonly Gen<(bool Restricted, string CorrectPin, bool PinMatches, string PageKey)> GuardCaseGen =
        from restricted in Gen.Bool
        from correctPin in CorrectPinGen
        from pinMatches in Gen.Bool
        from pageKey in PageKeyGen
        select (restricted, correctPin, pinMatches, pageKey);

    [Fact]
    public void Property14_guard_result_determined_by_pin_correctness_and_restricted_mode()
    {
        GuardCaseGen.Sample(
            c =>
            {
                var session = new ConfigurableSessionContextService(
                    FixedAccountId,
                    c.Restricted ? SessionPermissionMode.Restricted_Permission : SessionPermissionMode.Normal);
                var auth = new RecordingAuthService(FixedAccountId, c.CorrectPin);
                var guard = new SensitivePageGuard(session, auth);

                string enteredPin = c.PinMatches ? c.CorrectPin : (c.CorrectPin + "_WRONG");

                SensitiveAccessResult result = guard
                    .TryEnterAsync(c.PageKey, enteredPin)
                    .GetAwaiter().GetResult();

                // 独立重算期望结果。
                SensitiveAccessResult expected = c.Restricted
                    ? SensitiveAccessResult.BlockedByRestricted
                    : (c.PinMatches ? SensitiveAccessResult.Granted : SensitiveAccessResult.PinRejected);

                Assert.Equal(expected, result);

                // Req 18.3：仅 Granted 才渲染机密内容；其余结果机密内容恒不渲染。
                bool confidentialRendered = result == SensitiveAccessResult.Granted;
                bool shouldRender = !c.Restricted && c.PinMatches;
                Assert.Equal(shouldRender, confidentialRendered);

                // Req 18.4 / 17.4：受限模式短路必须先于 PIN 校验——受限时绝不调用 PIN 校验通道。
                if (c.Restricted)
                {
                    Assert.Equal(0, auth.VerifyPinCallCount);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    // ----------------------------------------------------------------------------------
    // Facet B: 受限模式恒 BlockedByRestricted、先于 PIN 校验、无论 PIN 对错（Req 18.4 / 17.4）
    // ----------------------------------------------------------------------------------
    [Fact]
    public void Property14_restricted_mode_blocks_before_pin_regardless_of_pin()
    {
        var caseGen =
            from correctPin in CorrectPinGen
            from pinMatches in Gen.Bool
            from pageKey in PageKeyGen
            select (correctPin, pinMatches, pageKey);

        caseGen.Sample(
            c =>
            {
                var session = new ConfigurableSessionContextService(
                    FixedAccountId, SessionPermissionMode.Restricted_Permission);
                var auth = new RecordingAuthService(FixedAccountId, c.correctPin);
                var guard = new SensitivePageGuard(session, auth);

                string enteredPin = c.pinMatches ? c.correctPin : (c.correctPin + "_WRONG");

                SensitiveAccessResult result = guard
                    .TryEnterAsync(c.pageKey, enteredPin)
                    .GetAwaiter().GetResult();

                // 无论 PIN 对错，受限模式恒拒绝且先于 PIN 校验短路（PIN 通道未被触达）。
                Assert.Equal(SensitiveAccessResult.BlockedByRestricted, result);
                Assert.Equal(0, auth.VerifyPinCallCount);
            },
            iter: PbtConfig.MinIterations);
    }

    // ----------------------------------------------------------------------------------
    // Facet C: 受限模式白名单——仅数据抢救类放行（Req 17.3 / 17.4）
    // ----------------------------------------------------------------------------------
    [Fact]
    public void Property14_restricted_mode_whitelist_allows_only_data_rescue_operations()
    {
        var opGen = Gen.OneOfConst(Enum.GetValues<RestrictedOperationKind>());

        opGen.Sample(
            op =>
            {
                bool allowed = RestrictedModePolicy.IsOperationAllowedInRestrictedMode(op);

                // 独立重算：仅「数据备份」与「数据导出 / 导入恢复」放行，其余一律拒绝。
                bool expected = op is RestrictedOperationKind.DataBackup
                    or RestrictedOperationKind.DataExportImportRestore;

                Assert.Equal(expected, allowed);
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property14_restricted_mode_whitelist_exhaustive_enumeration()
    {
        foreach (RestrictedOperationKind op in Enum.GetValues<RestrictedOperationKind>())
        {
            bool allowed = RestrictedModePolicy.IsOperationAllowedInRestrictedMode(op);
            bool expected = op is RestrictedOperationKind.DataBackup
                or RestrictedOperationKind.DataExportImportRestore;
            Assert.Equal(expected, allowed);
        }

        // 数据抢救类恒放行；机密 / 高危类恒拒绝。
        Assert.True(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.DataBackup));
        Assert.True(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.DataExportImportRestore));
        Assert.False(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.Cashflow));
        Assert.False(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.BusinessAdvice));
        Assert.False(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.MemberManagement));
        Assert.False(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.SecurityAndDataHighRiskSettings));
        Assert.False(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.DailyBusinessDataEdit));
    }

    // ----------------------------------------------------------------------------------
    // Facet D: 被停用 Owner 紧急启用——正确 PIN 进入受限模式、错误 PIN 拒绝且模式不变；
    //          成功 / 失败均记审计且不含明文 PIN（Req 17.1 / 17.2 / 17.3 / 17.5 / 18.5）
    // ----------------------------------------------------------------------------------
    [Fact]
    public void Property14_emergency_enable_enters_restricted_iff_correct_pin_and_never_leaks_pin()
    {
        var caseGen =
            from correctPin in CorrectPinGen
            from pinMatches in Gen.Bool
            select (correctPin, pinMatches);

        caseGen.Sample(
            c =>
            {
                var owner = new LocalAccount
                {
                    AccountId = FixedAccountId,
                    Username = "owner",
                    Role = LocalAccountRole.Owner,
                    IsEnabled = false, // 被停用的 Owner 才可紧急启用
                };
                var repo = new InMemoryLocalAccountRepository(new[] { owner });
                var session = new ConfigurableSessionContextService(FixedAccountId, SessionPermissionMode.Normal);
                var auth = new RecordingAuthService(FixedAccountId, c.correctPin);
                var audit = new RecordingSecurityAuditService();
                var service = new EmergencyEnableService(repo, auth, session, audit);

                string enteredPin = c.pinMatches ? c.correctPin : (c.correctPin + "_WRONG");

                EmergencyEnableResult result = service
                    .TryEmergencyEnableAsync(FixedAccountId, enteredPin)
                    .GetAwaiter().GetResult();

                if (c.pinMatches)
                {
                    // Req 17.1：正确 PIN → 成功并进入受限权限模式；受限模式下数据抢救类操作恒放行（Req 17.3）。
                    Assert.True(result.Succeeded);
                    Assert.Equal(SessionPermissionMode.Restricted_Permission, session.CurrentPermissionMode);
                    Assert.True(session.IsRestrictedPermissionMode);
                    Assert.True(RestrictedModePolicy.IsOperationAllowedInRestrictedMode(RestrictedOperationKind.DataBackup));
                }
                else
                {
                    // Req 17.2：错误 PIN → 拒绝、给中文提示、权限模式保持 Normal 不变。
                    Assert.False(result.Succeeded);
                    Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
                    Assert.Equal(SessionPermissionMode.Normal, session.CurrentPermissionMode);
                    Assert.False(session.IsRestrictedPermissionMode);
                }

                // Req 17.5：成功 / 失败均至少记录一条审计。
                Assert.NotEmpty(audit.AllSubjectsAndDetails);

                // Req 17.5 / 18.5 / P4：任一审计沉淀绝不含明文 PIN 原文。
                foreach (string sink in audit.AllSubjectsAndDetails)
                {
                    Assert.DoesNotContain(c.correctPin, sink, StringComparison.Ordinal);
                    Assert.DoesNotContain(enteredPin, sink, StringComparison.Ordinal);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    // ---- 记录调用的依赖替身（fake/stub） -------------------------------------------------

    /// <summary>可配置权限模式与当前账号的会话上下文替身。</summary>
    private sealed class ConfigurableSessionContextService : ISessionContextService
    {
        private readonly LocalSessionContext _current;

        public ConfigurableSessionContextService(string accountId, SessionPermissionMode mode)
        {
            _current = new LocalSessionContext { AccountId = accountId };
            CurrentPermissionMode = mode;
        }

        public event EventHandler? SessionChanged;

        public LocalSessionContext? Current => _current;
        public bool IsSignedIn => true;
        public bool IsDataKeyAvailable => false;
        public SessionPermissionMode CurrentPermissionMode { get; private set; }

        public bool IsRestrictedPermissionMode =>
            CurrentPermissionMode == SessionPermissionMode.Restricted_Permission;

        public void SetCurrent(LocalSessionContext session) => SessionChanged?.Invoke(this, EventArgs.Empty);
        public void SuspendDataKey() { }
        public bool TryRestoreDataKey(string accountId) => false;
        public void Clear() { }
        public void SetPermissionMode(SessionPermissionMode mode) => CurrentPermissionMode = mode;
    }

    /// <summary>记录 PIN 校验调用次数的认证替身；以相等比较模拟 PIN 正确与否，明文仅透传不留存。</summary>
    private sealed class RecordingAuthService : ILocalAuthService
    {
        private readonly string _accountId;
        private readonly string _correctPin;

        public RecordingAuthService(string accountId, string correctPin)
        {
            _accountId = accountId;
            _correctPin = correctPin;
        }

        public int VerifyPinCallCount { get; private set; }

        public Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
        {
            VerifyPinCallCount++;
            bool ok = string.Equals(accountId, _accountId, StringComparison.Ordinal)
                && string.Equals(pin, _correctPin, StringComparison.Ordinal);
            return Task.FromResult(ok);
        }

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

    /// <summary>记录所有审计写入的替身；累计主体 / 详情以断言绝不含明文 PIN。</summary>
    private sealed class RecordingSecurityAuditService : ISecurityAuditService
    {
        private readonly List<SecurityAuditRecord> _records = new();
        private readonly List<string> _subjectsAndDetails = new();

        public IReadOnlyList<string> AllSubjectsAndDetails => _subjectsAndDetails;

        public SecurityAuditRecord Record(SecurityEventType eventType, string? subject, SecurityEventOutcome outcome)
        {
            var record = new SecurityAuditRecord
            {
                Sequence = _records.Count,
                EventType = eventType,
                OccurredAt = DateTimeOffset.UtcNow,
                SubjectIdentifier = subject ?? string.Empty,
                Outcome = outcome,
            };
            _records.Add(record);
            _subjectsAndDetails.Add(subject ?? string.Empty);
            return record;
        }

        public IReadOnlyList<SecurityAuditRecord> GetRecords() => _records;

        public bool VerifyChainIntegrity() => true;

        public Task RecordAsync(
            SecurityAuditEventKind kind,
            string accountLabel,
            string detail,
            CancellationToken ct = default)
        {
            _subjectsAndDetails.Add(accountLabel ?? string.Empty);
            _subjectsAndDetails.Add(detail ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SecurityAuditEntry>> QueryAsync(
            string? accountLabel = null,
            DateTime? from = null,
            DateTime? to = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityAuditEntry>>(Array.Empty<SecurityAuditEntry>());
    }
}
