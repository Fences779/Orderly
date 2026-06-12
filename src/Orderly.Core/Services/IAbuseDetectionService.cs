namespace Orderly.Core.Services;

/// <summary>
/// 跨事件滥用信号的种类（需求 2.5 / design Property 5）。
///
/// 这些事件在既有"凭证 + 标识"失败锁定维度下会落入互不相同的计数桶，
/// 因此需要一个跨事件聚合层来识别它们组合而成的滥用模式。
/// </summary>
public enum AbuseSignalKind
{
    /// <summary>跨账户目录枚举（未认证目录读取等，每次可能命中不同主体）。</summary>
    CrossAccountEnumeration = 0,

    /// <summary>越权拒绝（非 Owner 尝试受限操作、跨账户操作被拒绝等）。</summary>
    AuthorizationDenied = 1,

    /// <summary>跨账户操作探测（对不属于当前主体的账号发起的操作尝试）。</summary>
    CrossAccountOperationProbe = 2,
}

/// <summary>
/// 跨事件异常检测在判定出疑似滥用模式时产生的告警载荷。
/// 不含任何明文敏感信息：主体仅以哈希形式出现在 <see cref="SubjectIdentifier"/>。
/// </summary>
public sealed record AbuseDetectionAlert(
    string Reason,
    int EventCount,
    int DistinctSubjectCount,
    IReadOnlyCollection<AbuseSignalKind> ObservedKinds,
    string? SubjectIdentifier,
    DateTimeOffset DetectedAt);

/// <summary>
/// 可插拔的告警/限流回调委托。默认实现保守（不配置即为无操作），
/// 宿主可注入自定义实现以接入限流、告警或审计联动，遵循最小权限与零信任原则。
/// </summary>
public delegate void AbuseAlertThrottleHook(AbuseDetectionAlert alert);

/// <summary>
/// 跨事件异常检测服务抽象（需求 2.5 / design Property 5）。
///
/// 在既有 <c>CredentialAttemptTracker</c>"凭证 + 标识"单维度失败锁定之上，新增跨事件、
/// 跨账户的聚合检测：将目录枚举、越权拒绝、跨账户操作探测等相互独立的安全信号
/// 汇聚到统一的滑动时间窗内做模式判定。当混合滥用模式（如大量跨账户枚举叠加反复
/// 越权拒绝）成立时，触发可插拔的告警/限流钩子。
///
/// 设计约束：
/// - 观测/上报入口为"尽力而为"语义，绝不改变调用方原有控制流与返回语义。
/// - 默认实现保守、不影响正常使用（高阈值 + 默认无操作钩子），遵循最小权限与零信任。
/// - 不存储明文敏感信息，主体在告警与内部计数中均以单向哈希表示。
/// </summary>
public interface IAbuseDetectionService
{
    /// <summary>
    /// 可插拔的告警/限流钩子。默认 null（无操作）；宿主可注入自定义回调以接入限流或告警。
    /// 钩子在跨事件滥用模式成立时被调用，不应抛出异常以免影响调用方控制流。
    /// </summary>
    AbuseAlertThrottleHook? AlertThrottleHook { get; set; }

    /// <summary>
    /// 观测并上报一个跨事件安全信号。服务将其汇入跨事件聚合窗口并做模式判定，
    /// 命中滥用模式时触发 <see cref="AlertThrottleHook"/>。
    /// 语义为"尽力而为"：不抛出异常、不改变调用方控制流。
    /// </summary>
    /// <param name="kind">信号种类。</param>
    /// <param name="subject">主体标识（账户 ID / 用户名 / 资源标识）。实现将对其做单向哈希，绝不存储明文。可为 null。</param>
    /// <returns>本次观测是否触发了滥用模式告警。</returns>
    bool ObserveSecurityEvent(AbuseSignalKind kind, string? subject);

    /// <summary>
    /// 评估当前是否已进入疑似滥用状态（跨事件聚合窗口内已达模式阈值）。
    /// 默认实现保守，仅在显著超过正常使用量级时返回 true，供调用方按需做限流决策。
    /// </summary>
    bool IsAbusePatternActive();
}
