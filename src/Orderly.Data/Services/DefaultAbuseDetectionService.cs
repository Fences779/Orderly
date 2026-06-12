using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

/// <summary>
/// <see cref="IAbuseDetectionService"/> 的保守后端实现（需求 2.5 / design Property 5）。
///
/// 跨事件聚合思路：
/// - 所有观测信号进入一个统一的滑动时间窗（<see cref="DetectionWindow"/>），
///   不再按"凭证 + 标识"单维度分桶，从而能聚合跨账户枚举与反复越权拒绝等
///   原本会落入互不相同计数桶、各自永远达不到锁定阈值的事件。
/// - 当窗口内的事件总量达到 <see cref="EventCountThreshold"/>，
///   且覆盖的不同主体数达到 <see cref="DistinctSubjectThreshold"/>，
///   且同时出现枚举与越权拒绝两类信号（混合模式）时，判定为疑似滥用模式，
///   触发可插拔的 <see cref="IAbuseDetectionService.AlertThrottleHook"/>。
///
/// 保守性（不影响正常使用）：
/// - 阈值显著高于正常登录选择器/越权交互量级，正常使用不会触发。
/// - 默认钩子为 null（无操作）：检测到模式仅做内部标记，不改变任何调用方控制流。
/// - 观测入口吞掉所有异常，绝不向调用方传播。
///
/// 隐私：主体仅以单向哈希（<see cref="HashSubject"/>）参与计数与告警，绝不存明文。
///
/// 线程安全：所有状态变更均在 <see cref="_syncRoot"/> 下进行。
/// </summary>
public sealed class DefaultAbuseDetectionService : IAbuseDetectionService
{
    // 跨事件聚合的滑动时间窗。
    private static readonly TimeSpan DetectionWindow = TimeSpan.FromSeconds(10);

    // 触发滥用判定所需的窗口内事件总量（保守：显著高于正常交互）。
    private const int EventCountThreshold = 20;

    // 触发滥用判定所需覆盖的不同主体数（跨账户特征）。
    private const int DistinctSubjectThreshold = 8;

    // 主体哈希的域分隔前缀，避免与其他哈希用途交叉。
    private const string SubjectHashDomain = "orderly.abuse-detection.subject.v1:";
    private const string AnonymousSubject = "<anonymous>";

    private readonly object _syncRoot = new();
    private readonly Queue<ObservedSignal> _window = new();
    private bool _patternActive;

    public AbuseAlertThrottleHook? AlertThrottleHook { get; set; }

    public bool ObserveSecurityEvent(AbuseSignalKind kind, string? subject)
    {
        try
        {
            AbuseDetectionAlert? alert = null;

            lock (_syncRoot)
            {
                var now = DateTimeOffset.UtcNow;
                var subjectHash = HashSubject(subject);
                _window.Enqueue(new ObservedSignal(kind, subjectHash, now));
                TrimExpired(now);

                if (TryEvaluatePattern(now, subjectHash, out var built))
                {
                    _patternActive = true;
                    alert = built;
                }
                else
                {
                    _patternActive = false;
                }
            }

            if (alert is not null)
            {
                InvokeHookSafely(alert);
                return true;
            }

            return false;
        }
        catch
        {
            // 跨事件检测为防御性增强：绝不改变调用方控制流与返回语义。
            return false;
        }
    }

    public bool IsAbusePatternActive()
    {
        lock (_syncRoot)
        {
            TrimExpired(DateTimeOffset.UtcNow);
            return _patternActive && _window.Count >= EventCountThreshold;
        }
    }

    private void TrimExpired(DateTimeOffset now)
    {
        var cutoff = now - DetectionWindow;
        while (_window.Count > 0 && _window.Peek().ObservedAt < cutoff)
        {
            _window.Dequeue();
        }
    }

    private bool TryEvaluatePattern(DateTimeOffset now, string subjectHash, out AbuseDetectionAlert? alert)
    {
        alert = null;

        if (_window.Count < EventCountThreshold)
        {
            return false;
        }

        var distinctSubjects = _window.Select(s => s.SubjectHash).Distinct().Count();
        if (distinctSubjects < DistinctSubjectThreshold)
        {
            return false;
        }

        var kinds = _window.Select(s => s.Kind).Distinct().ToArray();

        // 混合模式：同时存在枚举类与越权拒绝类信号，才认定为跨事件滥用，
        // 这正是单维度失败锁定无法覆盖的输入空间。
        var hasEnumeration = kinds.Contains(AbuseSignalKind.CrossAccountEnumeration)
            || kinds.Contains(AbuseSignalKind.CrossAccountOperationProbe);
        var hasDenial = kinds.Contains(AbuseSignalKind.AuthorizationDenied);
        if (!hasEnumeration || !hasDenial)
        {
            return false;
        }

        alert = new AbuseDetectionAlert(
            Reason: "检测到跨事件滥用模式：跨账户枚举/探测与反复越权拒绝在短时间窗内混合出现。",
            EventCount: _window.Count,
            DistinctSubjectCount: distinctSubjects,
            ObservedKinds: kinds,
            SubjectIdentifier: subjectHash,
            DetectedAt: now);
        return true;
    }

    private void InvokeHookSafely(AbuseDetectionAlert alert)
    {
        var hook = AlertThrottleHook;
        if (hook is null)
        {
            return;
        }

        try
        {
            hook(alert);
        }
        catch
        {
            // 钩子异常不得影响调用方控制流。
        }
    }

    private static string HashSubject(string? subject)
    {
        var normalized = string.IsNullOrWhiteSpace(subject)
            ? AnonymousSubject
            : subject.Trim().ToLowerInvariant();

        var bytes = Encoding.UTF8.GetBytes(SubjectHashDomain + normalized);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private readonly record struct ObservedSignal(AbuseSignalKind Kind, string SubjectHash, DateTimeOffset ObservedAt);
}
