using System.Reflection;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 4 (Bug Condition) — 统一防篡改安全审计。
///
/// 本测试编码了"修复后"的期望行为（design.md Property 4 / Requirements 2.4）：
///   对任意安全敏感事件（认证失败、账户锁定、越权拒绝、凭证/密钥变更与重封装），
///   系统 SHALL 写入统一、结构化、防篡改且不含明文敏感信息的安全审计记录。
///
/// 对应 design.md Bug Condition：
///   isBugCondition CASE "SecuritySensitiveEvent"
///     = isSecuritySensitiveEvent(input) AND NOT tamperEvidentAuditRecordWritten(input)
///
/// **CRITICAL**: 在未修复代码上本测试预期 FAIL（失败即确认缺陷存在）。
/// 根因：现有 <see cref="Orderly.Core.Models.ActivityLog"/> 面向业务活动
/// （强制要求 CustomerId / DealId / OrderId 等业务标识），并非面向安全事件的防篡改审计；
/// 安全敏感分支（如 <see cref="LocalAuthService"/> 的认证失败路径仅调用
/// <c>RecordCredentialFailure</c> 计入失败锁定）没有写入任何统一、结构化、防篡改的安全审计记录，
/// 系统中也不存在专用的安全审计抽象（<c>ISecurityAuditService</c> 尚不存在）。
///
/// **Seam 约束（已记录）**:
///   修复（任务 7.4）将新增后端审计抽象 —— 在 <c>Orderly.Core</c> 引入名称包含
///   "SecurityAudit" 的审计服务接口（如 <c>ISecurityAuditService</c>），并在 <c>Orderly.Data</c>
///   提供实现；同时定义结构化、不含明文敏感信息、含链式/完整性哈希（防篡改）的审计记录模型。
///   由于该抽象当前尚不存在，本测试通过反射断言这一"缝隙契约"是否满足：
///     1. 存在统一的安全审计服务抽象（unified）。
///     2. 存在结构化审计记录模型，携带：事件类型、时间、主体标识（哈希/账户 ID）、结果（structured）。
///     3. 审计记录携带完整性/链式哈希字段（tamper-evident）。
///     4. 审计记录不携带明文敏感字段（password / pin / plaintext / secret）。
///   该契约在未修复代码上无法满足（缝隙不存在）→ FAIL；任务 7.4 落地后 → PASS。
///   本测试项目目标框架为 net8.0，仅引用 Orderly.Core / Orderly.Data，
///   故以这两个程序集为反射搜索范围（接口预期在 Core，实现在 Data）。
///
/// **Validates: Requirements 2.4**
/// </summary>
public sealed class SecuritySensitiveEventAuditBugConditionTests
{
    // 锚定待搜索的程序集：LocalAccount 位于 Orderly.Core，LocalAuthService 位于 Orderly.Data。
    private static readonly Assembly CoreAssembly = typeof(LocalAccount).Assembly;
    private static readonly Assembly DataAssembly = typeof(LocalAuthService).Assembly;

    /// <summary>
    /// 安全敏感事件种类（对应 design.md / Requirements 2.4 列举的安全敏感事件），
    /// 作为 Scoped PBT 的输入域：kind = "SecuritySensitiveEvent"。
    /// </summary>
    private enum SecuritySensitiveEventKind
    {
        AuthenticationFailure, // 认证失败
        AccountLockout,        // 账户锁定
        AuthorizationDenied,   // 越权拒绝
        CredentialChange,      // 凭证变更
        KeyRewrap,             // 密钥变更与重封装
    }

    private static readonly Gen<SecuritySensitiveEventKind> EventKindGen =
        Gen.Int[0, 4].Select(i => (SecuritySensitiveEventKind)i);

    // 结构化审计记录应覆盖的字段类别（属性名包含任一同义词即视为覆盖）。
    private static readonly string[] EventTypeTokens = { "type", "kind", "category", "event", "action" };
    private static readonly string[] TimestampTokens = { "time", "timestamp", "at", "occurred", "when" };
    private static readonly string[] SubjectTokens = { "account", "subject", "actor", "identity", "principal", "user" };
    private static readonly string[] OutcomeTokens = { "outcome", "result", "status", "success", "succeeded", "disposition" };

    // 防篡改（完整性/链式哈希）字段类别。
    private static readonly string[] IntegrityTokens = { "hash", "chain", "integrity", "signature", "mac", "digest" };

    // 明文敏感信息：审计记录绝不应携带。
    private static readonly string[] ForbiddenPlaintextTokens = { "password", "plaintext", "secret", "masterpassword" };

    // 记录方法名候选（服务应提供写入安全审计记录的能力）。
    private static readonly string[] RecordMethodTokens = { "record", "append", "write", "log", "audit" };

    /// <summary>
    /// Property 4 — 统一防篡改安全审计缝隙契约。
    ///
    /// 对任意安全敏感事件种类，系统都应提供"统一、结构化、防篡改、不含明文敏感信息"的
    /// 安全审计能力。修复前：该缝隙完全不存在（无 ISecurityAuditService、无审计记录模型）
    /// → 断言失败（确认缺陷：安全敏感事件缺少统一防篡改审计记录）。
    /// </summary>
    [Fact]
    public void Security_sensitive_events_have_unified_tamper_evident_audit_record()
    {
        EventKindGen.Sample(kind =>
        {
            // (1) 统一安全审计服务抽象必须存在。
            var auditServiceType = FindSecurityAuditServiceType();
            Assert.True(
                auditServiceType is not null,
                $"安全敏感事件 [{kind}] 缺少统一安全审计抽象："
                + "未在 Orderly.Core / Orderly.Data 中找到名称包含 \"SecurityAudit\" 的审计服务"
                + "（期望任务 7.4 引入 ISecurityAuditService）。"
                + "现有 ActivityLog 面向业务活动（强制 CustomerId/DealId/OrderId），"
                + "并非面向安全事件的统一防篡改审计。");

            // (2) 该服务应暴露写入安全审计记录的方法。
            var recordMethod = auditServiceType!
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => ContainsAnyToken(m.Name, RecordMethodTokens));
            Assert.True(
                recordMethod is not null,
                $"安全敏感事件 [{kind}]：安全审计服务 {auditServiceType.Name} 未暴露任何记录方法"
                + "（期望提供 Record/Append/Write 等写入安全审计记录的能力）。");

            // (3) 结构化审计记录模型必须存在。
            var recordType = FindSecurityAuditRecordType();
            Assert.True(
                recordType is not null,
                $"安全敏感事件 [{kind}] 缺少结构化安全审计记录模型："
                + "未找到名称包含 \"SecurityAudit\" 且为记录/条目/事件语义（Record/Entry/Event/Log）的模型类型。");

            var propertyNames = recordType!
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToArray();

            // (3a) 结构化字段：事件类型、时间、主体标识、结果。
            Assert.True(
                propertyNames.Any(n => ContainsAnyToken(n, EventTypeTokens)),
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 缺少事件类型字段。");
            Assert.True(
                propertyNames.Any(n => ContainsAnyToken(n, TimestampTokens)),
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 缺少时间戳字段。");
            Assert.True(
                propertyNames.Any(n => ContainsAnyToken(n, SubjectTokens)),
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 缺少主体标识字段（账户 ID / 标识哈希）。");
            Assert.True(
                propertyNames.Any(n => ContainsAnyToken(n, OutcomeTokens)),
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 缺少结果/处置字段。");

            // (3b) 防篡改：完整性/链式哈希字段。
            Assert.True(
                propertyNames.Any(n => ContainsAnyToken(n, IntegrityTokens)),
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 缺少防篡改完整性字段"
                + "（期望链式/完整性哈希，如 PreviousHash / RecordHash / IntegrityHash）。");

            // (4) 不含明文敏感信息。
            var leaked = propertyNames
                .Where(n => ContainsAnyToken(n, ForbiddenPlaintextTokens))
                .ToArray();
            Assert.True(
                leaked.Length == 0,
                $"安全敏感事件 [{kind}]：审计记录 {recordType.Name} 暴露了明文敏感字段："
                + $"[{string.Join(", ", leaked)}]。审计记录不得含明文敏感信息。");
        });
    }

    /// <summary>
    /// 反例（确定性单元用例）：认证失败这一安全敏感事件当前没有统一防篡改审计记录。
    /// 明确记录根因 —— 系统不存在专用安全审计抽象，安全事件仅被计入失败锁定计数。
    /// </summary>
    [Fact]
    public void Authentication_failure_has_no_dedicated_tamper_evident_security_audit_seam()
    {
        var auditServiceType = FindSecurityAuditServiceType();
        var recordType = FindSecurityAuditRecordType();

        Assert.True(
            auditServiceType is not null && recordType is not null,
            "认证失败/账户锁定属于安全敏感事件，但系统缺少统一、防篡改的安全审计缝隙："
            + $"ISecurityAuditService={(auditServiceType?.FullName ?? "<不存在>")}，"
            + $"审计记录模型={(recordType?.FullName ?? "<不存在>")}。"
            + "现有 ActivityLog 面向业务活动（需 CustomerId/DealId/OrderId），无法承载无业务上下文的安全事件，"
            + "且不具备链式/完整性哈希等防篡改特性。期望任务 7.4 引入专用安全审计层后本断言通过。");
    }

    private static Type? FindSecurityAuditServiceType()
        => EnumerateCandidateTypes()
            .Where(t => t.IsInterface)
            .FirstOrDefault(t => t.Name.Contains("SecurityAudit", StringComparison.OrdinalIgnoreCase));

    private static Type? FindSecurityAuditRecordType()
    {
        // 候选：名称含 "SecurityAudit" 且为记录/条目/事件/日志语义的模型类型。
        // 注意：本期（settings-profile-refinement / BC-6）新增了读取投影模型 SecurityAuditEntry，
        // 它仅承载展示字段（无 Outcome / 完整性哈希），与本测试要断言的"防篡改审计记录模型"不同。
        // 因此在多个候选并存时，优先选中携带完整性/链式哈希字段的结构化记录模型（SecurityAuditRecord），
        // 使该缝隙契约断言保持确定性，不被展示用投影模型干扰。
        var candidates = EnumerateCandidateTypes()
            .Where(t => !t.IsInterface && (t.IsClass || t.IsValueType))
            .Where(t =>
                t.Name.Contains("SecurityAudit", StringComparison.OrdinalIgnoreCase)
                && (t.Name.Contains("Record", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Entry", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Event", StringComparison.OrdinalIgnoreCase)
                    || t.Name.Contains("Log", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        bool HasIntegrityField(Type t) => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => ContainsAnyToken(p.Name, IntegrityTokens));

        // 优先返回带完整性哈希字段的防篡改记录模型；否则回退到任一候选。
        return candidates.FirstOrDefault(HasIntegrityField) ?? candidates.FirstOrDefault();
    }

    private static IEnumerable<Type> EnumerateCandidateTypes()
    {
        IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t is not null)!;
            }
        }

        return SafeGetTypes(CoreAssembly).Concat(SafeGetTypes(DataAssembly));
    }

    private static bool ContainsAnyToken(string name, IEnumerable<string> tokens)
        => tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase));
}
