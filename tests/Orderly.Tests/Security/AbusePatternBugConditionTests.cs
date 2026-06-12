using System.Reflection;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 5 (Bug Condition) — 跨事件异常检测与告警/限流钩子。
///
/// 本测试编码了"修复后"的期望行为（design.md Property 5 / Requirements 2.5）：
///   对任意疑似滥用模式（跨账户枚举 + 反复越权拒绝的混合序列），系统 SHALL 触发
///   跨事件异常检测，并提供告警/限流钩子（遵循最小权限与零信任原则），在不破坏正常
///   使用前提下提升对枚举与越权探测的抵御能力。
///
/// 对应 design.md Bug Condition：
///   isBugCondition CASE "AbusePattern"
///     = isAbusePattern(input) AND NOT abuseDetectionHookTriggered(input)
///
/// **CRITICAL**: 在未修复代码上本测试预期 FAIL（失败即确认缺陷存在）。
/// 根因（design.md Hypothesized Root Cause #5）：
///   现有 <see cref="CredentialAttemptTracker"/> 仅按"凭证 + 标识"（purpose + identifier）
///   单一维度做失败锁定（见 <c>IsBlocked</c> / <c>RecordFailure</c>，键为 purpose:identifier 的哈希），
///   无法跨账户、跨事件类型聚合。跨账户枚举（每次命中不同 identifier）与越权拒绝（不同的
///   purpose）会落入互不相同的计数桶，永远不会达到任一桶的锁定阈值，因此跨事件滥用模式
///   完全逃逸检测。系统中也不存在任何跨事件聚合检测抽象（<c>IAbuseDetectionService</c> 尚不存在），
///   亦无告警/限流钩子。
///
/// **Seam 约束（已记录）**:
///   修复（任务 7.5）将在既有"凭证 + 标识"失败锁定之上新增跨事件检测抽象 —— 在
///   <c>Orderly.Core</c> / <c>Orderly.Data</c> 引入名称包含 "AbuseDetection" 的检测服务接口
///   （如 <c>IAbuseDetectionService</c>），并在目录枚举、越权拒绝、跨账户操作探测处埋点；
///   该服务应：
///     1. 暴露跨事件观测/上报入口（Observe / Report / Record / Track / Evaluate 安全事件）。
///     2. 提供可插拔的告警/限流钩子（Hook / Alert / Throttle / Callback / Handler / Notify），
///        默认实现保守、不影响正常使用。
///   由于该抽象当前尚不存在，本测试通过反射断言这一"缝隙契约"是否满足。
///   该契约在未修复代码上无法满足（缝隙不存在）→ FAIL；任务 7.5 落地后 → PASS。
///   本测试项目目标框架为 net8.0，仅引用 Orderly.Core / Orderly.Data，
///   故以这两个程序集为反射搜索范围（接口预期在 Core，实现在 Data）。
///
/// **Validates: Requirements 2.5**
/// </summary>
public sealed class AbusePatternBugConditionTests
{
    // 锚定待搜索的程序集：LocalAccount 位于 Orderly.Core，CredentialAttemptTracker 位于 Orderly.Data。
    private static readonly Assembly CoreAssembly = typeof(LocalAccount).Assembly;
    private static readonly Assembly DataAssembly = typeof(CredentialAttemptTracker).Assembly;

    /// <summary>
    /// 跨事件滥用模式中的单个安全事件种类，作为 Scoped PBT 的输入域：kind = "AbusePattern"。
    /// 涵盖跨账户枚举与反复越权拒绝两类相互独立、会落入不同失败计数桶的事件。
    /// </summary>
    private enum AbuseEventKind
    {
        CrossAccountEnumeration, // 跨账户目录枚举（每次命中不同 identifier）
        AuthorizationDenied,     // 越权拒绝（不同 purpose / 资源）
    }

    /// <summary>
    /// 单个滥用事件：事件种类 + 主体标识。不同的 identifier 模拟跨账户枚举，
    /// 使其在现有"凭证 + 标识"维度下落入互不相同的计数桶。
    /// </summary>
    private readonly record struct AbuseEvent(AbuseEventKind Kind, string Identifier);

    private static readonly Gen<AbuseEvent> AbuseEventGen =
        Gen.Select(
            Gen.Int[0, 1].Select(i => (AbuseEventKind)i),
            Gen.Int[0, 1_000_000],
            (kindIndex, id) => new AbuseEvent(kindIndex, $"subject-{id:D7}"));

    /// <summary>
    /// 智能序列生成器：约束为"混合序列"——同时包含跨账户枚举与越权拒绝两类事件，
    /// 且规模足以构成滥用模式（≥ 阈值）。这正是单维度失败锁定无法覆盖的输入空间。
    /// </summary>
    private static readonly Gen<List<AbuseEvent>> AbusePatternSequenceGen =
        AbuseEventGen.List[12, 40]
            .Where(seq =>
                seq.Any(e => e.Kind == AbuseEventKind.CrossAccountEnumeration)
                && seq.Any(e => e.Kind == AbuseEventKind.AuthorizationDenied));

    // 观测/上报入口候选方法名词元（服务应能接收跨事件安全信号）。
    private static readonly string[] ObserveMethodTokens =
        { "observe", "report", "record", "track", "register", "evaluate", "inspect", "notice", "ingest" };

    // 告警/限流钩子候选词元（方法、属性或委托类型名命中任一即视为提供钩子）。
    private static readonly string[] HookTokens =
        { "hook", "alert", "throttle", "callback", "handler", "notify", "trigger", "ratelimit", "block" };

    // 跨事件 / 聚合语义词元：用于确认检测是"跨事件"而非单维度失败锁定。
    private static readonly string[] CrossEventTokens =
        { "abuse", "anomaly", "pattern", "detection", "aggregate", "crossevent", "correlat" };

    /// <summary>
    /// Property 5 — 跨事件异常检测与告警/限流钩子缝隙契约。
    ///
    /// 对任意"跨账户枚举 + 反复越权拒绝"的混合滥用序列，系统都应提供一个跨事件聚合的
    /// 异常检测能力，并在模式成立时触发告警/限流钩子。
    /// 修复前：该缝隙完全不存在（无 IAbuseDetectionService、无告警/限流钩子，
    /// CredentialAttemptTracker 仅按 purpose:identifier 单维度锁定）→ 断言失败
    /// （确认缺陷：跨事件滥用模式无任何检测/告警/限流响应）。
    /// </summary>
    [Fact]
    public void Cross_event_abuse_patterns_trigger_a_detection_and_alert_or_throttle_hook()
    {
        AbusePatternSequenceGen.Sample(sequence =>
        {
            var enumerationCount = sequence.Count(e => e.Kind == AbuseEventKind.CrossAccountEnumeration);
            var denialCount = sequence.Count(e => e.Kind == AbuseEventKind.AuthorizationDenied);
            var distinctSubjects = sequence.Select(e => e.Identifier).Distinct().Count();

            // (1) 跨事件异常检测服务抽象必须存在。
            var detectionServiceType = FindAbuseDetectionServiceType();
            Assert.True(
                detectionServiceType is not null,
                $"滥用模式（跨账户枚举×{enumerationCount} + 越权拒绝×{denialCount}，"
                + $"覆盖 {distinctSubjects} 个不同主体）缺少跨事件异常检测抽象："
                + "未在 Orderly.Core / Orderly.Data 中找到名称包含 \"AbuseDetection\" 的检测服务"
                + "（期望任务 7.5 引入 IAbuseDetectionService）。"
                + "现有 CredentialAttemptTracker 仅按 purpose:identifier 单维度做失败锁定，"
                + "跨账户、跨事件类型的滥用会落入互不相同的计数桶而逃逸检测。");

            // (2) 该服务应暴露跨事件观测/上报入口。
            var observeMethod = detectionServiceType!
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => ContainsAnyToken(m.Name, ObserveMethodTokens));
            Assert.True(
                observeMethod is not null,
                $"跨事件检测服务 {detectionServiceType.Name} 未暴露任何观测/上报入口"
                + "（期望提供 Observe/Report/Record/Evaluate 等接收跨事件安全信号的方法），"
                + "无法接入目录枚举与越权拒绝的埋点。");

            // (3) 该服务应提供可插拔的告警/限流钩子（方法 / 属性 / 委托参数 命中钩子词元）。
            Assert.True(
                ExposesAlertOrThrottleHook(detectionServiceType),
                $"跨事件检测服务 {detectionServiceType.Name} 未提供任何告警/限流钩子"
                + "（期望可插拔的 Hook/Alert/Throttle/Callback 回调），"
                + "无法在滥用模式成立时触发限流或告警。");

            // (4) 检测语义应为"跨事件 / 聚合 / 异常"，而非单维度失败锁定。
            Assert.True(
                HasCrossEventSemantics(detectionServiceType),
                $"检测服务 {detectionServiceType.Name} 不具备跨事件/聚合/异常检测语义"
                + "（期望命名或成员体现 abuse/anomaly/pattern/aggregate/correlation），"
                + "仅复用单维度失败锁定不足以覆盖跨账户枚举 + 越权拒绝的混合模式。");
        });
    }

    /// <summary>
    /// 反例（确定性单元用例）：一段明确的跨账户枚举 + 越权拒绝混合序列，
    /// 在现有实现下没有任何跨事件检测/告警/限流响应。
    /// 明确记录根因 —— 系统不存在跨事件检测抽象，单维度失败锁定无法聚合此类模式。
    /// </summary>
    [Fact]
    public void Mixed_enumeration_and_denial_sequence_has_no_cross_event_detection_seam()
    {
        // 构造一个具体反例：10 次跨账户枚举（各不相同的主体）+ 10 次越权拒绝。
        var sequence = new List<AbuseEvent>();
        for (var i = 0; i < 10; i++)
        {
            sequence.Add(new AbuseEvent(AbuseEventKind.CrossAccountEnumeration, $"victim-{i:D3}"));
            sequence.Add(new AbuseEvent(AbuseEventKind.AuthorizationDenied, $"resource-{i:D3}"));
        }

        var detectionServiceType = FindAbuseDetectionServiceType();

        Assert.True(
            detectionServiceType is not null
                && FindObserveMethod(detectionServiceType) is not null
                && ExposesAlertOrThrottleHook(detectionServiceType),
            "跨账户枚举 + 反复越权拒绝构成滥用模式，但系统缺少跨事件检测/告警/限流缝隙："
            + $"IAbuseDetectionService={(detectionServiceType?.FullName ?? "<不存在>")}；"
            + $"序列含 {sequence.Count(e => e.Kind == AbuseEventKind.CrossAccountEnumeration)} 次跨账户枚举 + "
            + $"{sequence.Count(e => e.Kind == AbuseEventKind.AuthorizationDenied)} 次越权拒绝，"
            + $"覆盖 {sequence.Select(e => e.Identifier).Distinct().Count()} 个不同主体。"
            + "现有 CredentialAttemptTracker 以 SHA256(purpose:identifier) 为键分桶计数，"
            + "每个不同主体/资源各自独立计数，永远无法达到单桶锁定阈值，故跨事件滥用全程逃逸检测。"
            + "期望任务 7.5 引入跨事件检测与告警/限流钩子后本断言通过。");
    }

    private static Type? FindAbuseDetectionServiceType()
        => EnumerateCandidateTypes()
            .Where(t => t.IsInterface)
            .FirstOrDefault(t => t.Name.Contains("AbuseDetection", StringComparison.OrdinalIgnoreCase));

    private static MethodInfo? FindObserveMethod(Type serviceType)
        => serviceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => ContainsAnyToken(m.Name, ObserveMethodTokens));

    private static bool ExposesAlertOrThrottleHook(Type serviceType)
    {
        // 钩子可表现为：命中钩子词元的方法名、属性名，或接受委托类型参数（可插拔回调）。
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        if (methods.Any(m => ContainsAnyToken(m.Name, HookTokens)))
        {
            return true;
        }

        var properties = serviceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (properties.Any(p => ContainsAnyToken(p.Name, HookTokens)
                                || typeof(Delegate).IsAssignableFrom(p.PropertyType)))
        {
            return true;
        }

        // 任一方法接受委托型参数（如 Action / Func 回调），视为提供可插拔钩子。
        return methods.Any(m => m.GetParameters()
            .Any(p => typeof(Delegate).IsAssignableFrom(p.ParameterType)
                      || ContainsAnyToken(p.Name, HookTokens)));
    }

    private static bool HasCrossEventSemantics(Type serviceType)
    {
        if (ContainsAnyToken(serviceType.Name, CrossEventTokens))
        {
            return true;
        }

        return serviceType
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Any(n => ContainsAnyToken(n, CrossEventTokens));
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
