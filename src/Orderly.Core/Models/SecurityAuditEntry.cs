namespace Orderly.Core.Models;

/// <summary>
/// 安全审计读取模型（BC-6）。由安全审计读取 API 返回，feed 进 <c>MeProfileViewModel.SecurityAuditEntries</c>，
/// 驱动我的页「账户安全 / 登录记录」卡片展示登录历史 / 失败次数 / 账户锁定 / 凭证变更 / 成员删除等记录
/// （design §6.4、§10.2）。
///
/// 安全约束（P0 / P4）：仅承载事件类型、时间、账号标签与脱敏 <see cref="Detail"/>，
/// 绝不含明文密码 / PIN（需求 12.4 / Property 7）。
/// </summary>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="Kind">事件类型（取值对应 <see cref="SecurityAuditEventKind"/>），用于分类与展示。</param>
/// <param name="AccountLabel">账号标签 / 标识，用于归属展示；不含明文凭证。</param>
/// <param name="Detail">脱敏后的事件详情说明；绝不含明文凭证。</param>
public sealed record SecurityAuditEntry(
    DateTime OccurredAt,
    string Kind,
    string AccountLabel,
    string Detail);
