using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property-based test for credential-change session transfer
/// (design §9.6 / Req 16 / §11 Property 13).
///
/// <para><b>Property 13: 凭证修改后会话转移.</b>
/// 对任意 (kind ∈ {MasterPassword, Pin}, result ∈ {Success, Failed, Cancelled})，
/// <see cref="CredentialChangeSessionCoordinator.OnCredentialChangeCompleted"/> 的会话后置
/// 行为恒由 (凭证种类, 是否成功) 唯一决定：
/// <list type="bullet">
///   <item><c>result ≠ Success</c> → 不登出、不锁定、不清空会话、不记审计（Req 16.3）。</item>
///   <item><c>result == Success ∧ MasterPassword</c> → 触发登出（Logout + Clear）且恰好记一条
///     <see cref="SecurityEventType.CredentialChange"/> 审计；不锁定（Req 16.1 / 16.4）。</item>
///   <item><c>result == Success ∧ Pin</c> → 触发锁定（<see cref="ISessionLockService.LockManually"/>）
///     进入 <see cref="SessionLockState.PendingPinUnlock"/>、不登出，且恰好记一条审计（Req 16.2 / 16.4）。</item>
///   <item>任一审计记录的主体/详情绝不含明文凭证（Req 16.4 / P4 / Property 7）。</item>
/// </list></para>
///
/// <para>三个协作依赖（<see cref="ISessionLockService"/> / <see cref="ISessionContextService"/> /
/// <see cref="ISecurityAuditService"/>）以记录调用的 fake/stub 实现替身，断言基于独立重算的期望，
/// 不读取被测产物，避免循环论证。输入空间仅 2 种类 × 3 结果 = 6 种组合，既以 CsCheck 在该离散
/// 空间随机抽样，又显式全覆盖枚举全部 6 种组合逐一断言。</para>
///
/// **Validates: Requirements 16.1, 16.2, 16.3, 16.4**
/// </summary>
public sealed class CredentialChangeSessionTransferPropertyTests
{
    private static readonly Gen<CredentialChangeKind> KindGen =
        Gen.OneOfConst(CredentialChangeKind.MasterPassword, CredentialChangeKind.Pin);

    private static readonly Gen<CredentialChangeResult> ResultGen =
        Gen.OneOfConst(
            CredentialChangeResult.Success,
            CredentialChangeResult.Failed,
            CredentialChangeResult.Cancelled);

    // 明文凭证（如新密码 / 新 PIN）原文：协调器签名不接收明文，此处用于断言审计绝不含明文。
    // 以独特哨兵前缀生成，确保与固定账号 ID（"acct-fixed-id"）不会偶然子串碰撞——
    // 任何包含该前缀的字符串出现在审计中都意味着明文真的泄露了。
    private const string SecretPrefix = "PLAINTEXT_CREDENTIAL_";

    private static readonly Gen<string> SecretGen =
        from body in Gen.String[Gen.Char.AlphaNumeric, 1, 16]
        select SecretPrefix + body;

    private static readonly Gen<(CredentialChangeKind Kind, CredentialChangeResult Result, string Secret)> CaseGen =
        from kind in KindGen
        from result in ResultGen
        from secret in SecretGen
        select (kind, result, secret);

    [Fact]
    public void Property13_session_transfer_is_determined_by_kind_and_result()
    {
        CaseGen.Sample(
            c =>
            {
                var lockService = new RecordingSessionLockService();
                var contextService = new RecordingSessionContextService("acct-fixed-id");
                var audit = new RecordingSecurityAuditService();
                var coordinator = new CredentialChangeSessionCoordinator(lockService, contextService, audit);

                coordinator.OnCredentialChangeCompleted(c.Kind, c.Result);

                bool success = c.Result == CredentialChangeResult.Success;

                if (!success)
                {
                    // Req 16.3：失败 / 取消 → 会话状态保持不变，既不登出、不锁定、不清空、不记审计。
                    Assert.Equal(0, lockService.LogoutCount);
                    Assert.Equal(0, lockService.LockManuallyCount);
                    Assert.Equal(0, contextService.ClearCount);
                    Assert.Equal(SessionLockState.Unlocked, lockService.State);
                    Assert.Empty(audit.Records);
                }
                else if (c.Kind == CredentialChangeKind.MasterPassword)
                {
                    // Req 16.1：主密码改成功 → 强制登出（Logout + Clear），不锁定。
                    Assert.Equal(1, lockService.LogoutCount);
                    Assert.Equal(1, contextService.ClearCount);
                    Assert.Equal(0, lockService.LockManuallyCount);
                    Assert.Equal(SessionLockState.LoggedOut, lockService.State);
                    // Req 16.4：成功路径恰好记一条 CredentialChange 审计。
                    Assert.Single(audit.Records);
                    Assert.Equal(SecurityEventType.CredentialChange, audit.Records[0].EventType);
                    Assert.Equal(SecurityEventOutcome.Success, audit.Records[0].Outcome);
                }
                else // Pin
                {
                    // Req 16.2：PIN 改成功 → 锁定进入 PendingPinUnlock，不登出、不清空会话。
                    Assert.Equal(1, lockService.LockManuallyCount);
                    Assert.Equal(0, lockService.LogoutCount);
                    Assert.Equal(0, contextService.ClearCount);
                    Assert.Equal(SessionLockState.PendingPinUnlock, lockService.State);
                    // Req 16.4：成功路径恰好记一条 CredentialChange 审计。
                    Assert.Single(audit.Records);
                    Assert.Equal(SecurityEventType.CredentialChange, audit.Records[0].EventType);
                    Assert.Equal(SecurityEventOutcome.Success, audit.Records[0].Outcome);
                }

                // Req 16.4 / P4：任一审计的主体 / 详情绝不含明文凭证原文。
                foreach (var rec in audit.AllSubjectsAndDetails)
                {
                    Assert.DoesNotContain(c.Secret, rec, StringComparison.Ordinal);
                }
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property13_exhaustive_enumeration_covers_all_six_combinations()
    {
        var kinds = new[] { CredentialChangeKind.MasterPassword, CredentialChangeKind.Pin };
        var results = new[]
        {
            CredentialChangeResult.Success,
            CredentialChangeResult.Failed,
            CredentialChangeResult.Cancelled,
        };

        var seen = new List<(CredentialChangeKind, CredentialChangeResult)>();

        foreach (CredentialChangeKind kind in kinds)
        {
            foreach (CredentialChangeResult result in results)
            {
                seen.Add((kind, result));

                var lockService = new RecordingSessionLockService();
                var contextService = new RecordingSessionContextService("acct-fixed-id");
                var audit = new RecordingSecurityAuditService();
                var coordinator = new CredentialChangeSessionCoordinator(lockService, contextService, audit);

                coordinator.OnCredentialChangeCompleted(kind, result);

                bool success = result == CredentialChangeResult.Success;
                bool isMaster = kind == CredentialChangeKind.MasterPassword;

                Assert.Equal(success && isMaster ? 1 : 0, lockService.LogoutCount);
                Assert.Equal(success && isMaster ? 1 : 0, contextService.ClearCount);
                Assert.Equal(success && !isMaster ? 1 : 0, lockService.LockManuallyCount);
                Assert.Equal(success ? 1 : 0, audit.Records.Count);

                if (success)
                {
                    Assert.Equal(SecurityEventType.CredentialChange, audit.Records[0].EventType);
                }
            }
        }

        // 确认 2 种类 × 3 结果 = 6 种组合全部被覆盖且互不重复。
        Assert.Equal(6, seen.Distinct().Count());
    }

    // ---- 记录调用的依赖替身（fake/stub） -------------------------------------------------

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

    /// <summary>记录 <see cref="Clear"/> 调用并提供当前账号主体的替身。</summary>
    private sealed class RecordingSessionContextService : ISessionContextService
    {
        private LocalSessionContext? _current;

        public RecordingSessionContextService(string accountId)
        {
            _current = new LocalSessionContext { AccountId = accountId };
        }

        public event EventHandler? SessionChanged;

        public int ClearCount { get; private set; }
        public LocalSessionContext? Current => _current;
        public bool IsSignedIn => _current is not null;
        public bool IsDataKeyAvailable => false;
        public SessionPermissionMode CurrentPermissionMode { get; private set; } = SessionPermissionMode.Normal;

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

        public void SetPermissionMode(SessionPermissionMode mode) => CurrentPermissionMode = mode;
    }

    /// <summary>记录所有审计写入的替身；不接触任何明文凭证。</summary>
    private sealed class RecordingSecurityAuditService : ISecurityAuditService
    {
        private readonly List<SecurityAuditRecord> _records = new();
        private readonly List<string> _subjectsAndDetails = new();

        public IReadOnlyList<SecurityAuditRecord> Records => _records;
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
