using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// 明文 PIN「即用即清」单元测试（任务 9.7，需求 18.5 / 17.5 / 14.5，design §9.7 / §9.8）。
///
/// <para>验证 <see cref="EmergencyEnableService.TryEmergencyEnableAsync"/>（紧急启用）与
/// <see cref="SensitivePageGuard.TryEnterAsync"/>（敏感页面门禁）在完成 PIN 校验后：</para>
/// <list type="bullet">
///   <item><b>即用</b>：明文 PIN 确实被透传给既有校验通道（<see cref="ILocalAuthService.VerifyPinAsync"/>）并参与校验。</item>
///   <item><b>即清</b>：服务实例不持有 / 不缓存明文 PIN——反射检查服务对象的所有字段，
///     不存在任何保存了 PIN 原文的 <see cref="string"/> 字段（也无 <c>ILogger</c> 等可写日志/诊断的字段）。</item>
///   <item><b>不写日志 / 诊断 / 审计明文</b>：任一审计沉淀（账号标签 / detail）绝不含明文 PIN 原文。</item>
/// </list>
///
/// <para>明文 PIN 以独特哨兵前缀生成，确保不会与账号 ID / 固定文案偶然子串碰撞——
/// 任何包含该前缀的字符串出现在服务字段或审计沉淀中都意味着明文真的泄露了。</para>
/// </summary>
public sealed class PlaintextPinEphemeralTests
{
    private const string FixedAccountId = "acct-fixed-id";

    // 哨兵：6 位数字 PIN 体 + 不可能被代码常量/账号 ID 偶然包含的前缀。
    // 反射/审计扫描以整串与前缀双重比对，命中即视为泄露。
    private const string PinSentinelPrefix = "PINSECRET_";
    private const string CorrectPin = PinSentinelPrefix + "123456";

    // ------------------------------------------------------------------------------------
    // 紧急启用：校验后明文 PIN 不残留、不写日志/诊断/审计明文（需求 17.5 / 14.5）
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task EmergencyEnable_correct_pin_does_not_retain_or_leak_plaintext_pin()
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
        var auth = new RecordingAuthService(FixedAccountId, CorrectPin);
        var audit = new RecordingSecurityAuditService();
        var service = new EmergencyEnableService(repo, auth, session, audit);

        var result = await service.TryEmergencyEnableAsync(FixedAccountId, CorrectPin);

        // 即用：成功且明文 PIN 确实被透传到校验通道并参与校验。
        Assert.True(result.Succeeded);
        Assert.Equal(1, auth.VerifyPinCallCount);
        Assert.Equal(CorrectPin, auth.LastPinSeen);

        // 即清：服务实例不持有/不缓存明文 PIN，且无可写日志/诊断的字段。
        AssertServiceRetainsNoPlaintextPin(service, CorrectPin);

        // 不写日志/诊断/审计明文：任一审计沉淀绝不含明文 PIN。
        AssertSinksContainNoPlaintextPin(audit.AllSubjectsAndDetails, CorrectPin);
    }

    [Fact]
    public async Task EmergencyEnable_wrong_pin_does_not_retain_or_leak_plaintext_pin()
    {
        var owner = new LocalAccount
        {
            AccountId = FixedAccountId,
            Username = "owner",
            Role = LocalAccountRole.Owner,
            IsEnabled = false,
        };
        var repo = new InMemoryLocalAccountRepository(new[] { owner });
        var session = new ConfigurableSessionContextService(FixedAccountId, SessionPermissionMode.Normal);
        var auth = new RecordingAuthService(FixedAccountId, CorrectPin);
        var audit = new RecordingSecurityAuditService();
        var service = new EmergencyEnableService(repo, auth, session, audit);

        string wrongPin = CorrectPin + "_WRONG";
        var result = await service.TryEmergencyEnableAsync(FixedAccountId, wrongPin);

        // 即用：错误 PIN 被透传校验并被拒绝，权限模式保持不变。
        Assert.False(result.Succeeded);
        Assert.Equal(1, auth.VerifyPinCallCount);
        Assert.Equal(wrongPin, auth.LastPinSeen);
        Assert.Equal(SessionPermissionMode.Normal, session.CurrentPermissionMode);

        // 即清：错误 PIN 同样不得残留在服务实例中。
        AssertServiceRetainsNoPlaintextPin(service, wrongPin);

        // 不写日志/诊断/审计明文。
        AssertSinksContainNoPlaintextPin(audit.AllSubjectsAndDetails, wrongPin);
    }

    // ------------------------------------------------------------------------------------
    // 敏感页面门禁：校验后明文 PIN 不残留、不写日志/诊断（需求 18.5 / 14.5）
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task SensitivePageGuard_correct_pin_does_not_retain_plaintext_pin()
    {
        var session = new ConfigurableSessionContextService(FixedAccountId, SessionPermissionMode.Normal);
        var auth = new RecordingAuthService(FixedAccountId, CorrectPin);
        var guard = new SensitivePageGuard(session, auth);

        var result = await guard.TryEnterAsync("Cashflow", CorrectPin);

        // 即用：放行且明文 PIN 确实被透传到校验通道。
        Assert.Equal(SensitiveAccessResult.Granted, result);
        Assert.Equal(1, auth.VerifyPinCallCount);
        Assert.Equal(CorrectPin, auth.LastPinSeen);

        // 即清：门禁实例不持有/不缓存明文 PIN，且无可写日志/诊断的字段。
        AssertServiceRetainsNoPlaintextPin(guard, CorrectPin);
    }

    [Fact]
    public async Task SensitivePageGuard_wrong_pin_does_not_retain_plaintext_pin()
    {
        var session = new ConfigurableSessionContextService(FixedAccountId, SessionPermissionMode.Normal);
        var auth = new RecordingAuthService(FixedAccountId, CorrectPin);
        var guard = new SensitivePageGuard(session, auth);

        string wrongPin = CorrectPin + "_WRONG";
        var result = await guard.TryEnterAsync("Cashflow", wrongPin);

        // 即用：错误 PIN 被透传校验并被拒绝（机密内容不渲染由调用方保证）。
        Assert.Equal(SensitiveAccessResult.PinRejected, result);
        Assert.Equal(1, auth.VerifyPinCallCount);
        Assert.Equal(wrongPin, auth.LastPinSeen);

        // 即清：错误 PIN 同样不得残留在门禁实例中。
        AssertServiceRetainsNoPlaintextPin(guard, wrongPin);
    }

    [Fact]
    public async Task SensitivePageGuard_restricted_mode_short_circuit_retains_no_pin()
    {
        var session = new ConfigurableSessionContextService(FixedAccountId, SessionPermissionMode.Restricted_Permission);
        var auth = new RecordingAuthService(FixedAccountId, CorrectPin);
        var guard = new SensitivePageGuard(session, auth);

        var result = await guard.TryEnterAsync("Cashflow", CorrectPin);

        // 受限模式短路：先于 PIN 校验拒绝，PIN 通道未被触达。
        Assert.Equal(SensitiveAccessResult.BlockedByRestricted, result);
        Assert.Equal(0, auth.VerifyPinCallCount);
        Assert.Null(auth.LastPinSeen);

        // 即清：即便走短路分支，门禁实例也不得持有明文 PIN。
        AssertServiceRetainsNoPlaintextPin(guard, CorrectPin);
    }

    // ------------------------------------------------------------------------------------
    // 断言工具
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// 反射检查服务实例的所有实例字段（含私有、含基类），断言：
    /// (1) 不存在任何 <see cref="string"/> 字段保存了明文 PIN 原文或其哨兵前缀；
    /// (2) 不存在 <c>ILogger</c> / 诊断相关字段（服务不应具备写日志/诊断能力）。
    /// </summary>
    private static void AssertServiceRetainsNoPlaintextPin(object service, string pin)
    {
        for (Type? type = service.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // 服务不得持有任何日志/诊断 sink（否则存在把明文写入诊断的途径）。
                string fieldTypeName = field.FieldType.Name;
                Assert.DoesNotContain("Logger", fieldTypeName, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Diagnostic", fieldTypeName, StringComparison.OrdinalIgnoreCase);

                if (field.FieldType != typeof(string))
                {
                    continue;
                }

                var value = field.GetValue(service) as string;
                if (value is null)
                {
                    continue;
                }

                // 任一字符串字段都不得等于明文 PIN，也不得包含其哨兵前缀。
                Assert.NotEqual(pin, value);
                Assert.DoesNotContain(PinSentinelPrefix, value, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>断言任一审计/日志沉淀字符串绝不含明文 PIN 原文或其哨兵前缀。</summary>
    private static void AssertSinksContainNoPlaintextPin(IEnumerable<string> sinks, string pin)
    {
        foreach (string sink in sinks)
        {
            Assert.DoesNotContain(pin, sink, StringComparison.Ordinal);
            Assert.DoesNotContain(PinSentinelPrefix, sink, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------------------------
    // 记录调用的依赖替身（fake/stub）
    // ------------------------------------------------------------------------------------

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

        public void SetCurrent(LocalSessionContext session) => SessionChanged?.Invoke(this, EventArgs.Empty);
        public void SuspendDataKey() { }
        public bool TryRestoreDataKey(string accountId) => false;
        public void Clear() { }
        public void SetPermissionMode(SessionPermissionMode mode) => CurrentPermissionMode = mode;
    }

    /// <summary>
    /// 记录 PIN 校验调用次数与最近一次明文 PIN 的认证替身；以相等比较模拟 PIN 正确与否。
    /// <see cref="LastPinSeen"/> 仅用于测试断言「明文确被透传校验（即用）」，不代表被测服务缓存了 PIN。
    /// </summary>
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

        public string? LastPinSeen { get; private set; }

        public Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default)
        {
            VerifyPinCallCount++;
            LastPinSeen = pin;
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
