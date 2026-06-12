using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 1 (Bug Condition) — 未认证目录最小化与枚举抑制。
///
/// 本测试编码了"修复后"的期望行为（design.md Property 1 / Requirements 2.1）：
///   1. <c>ListAccountDirectoryAsync</c> 的投影应为最小字段集合（Username、DisplayName、
///      IsEnabled），Role 等元数据须被规约为中性值，不随真实角色变化。
///   2. 未认证目录读取应施加频率限制，高频调用超阈值后被抑制（节流/受控失败）。
///
/// 对应 design.md Bug Condition：
///   isBugCondition CASE "UnauthDirectoryRead"
///     = directoryExposesNonMinimalMetadata(input) OR NOT unauthenticatedReadRateLimited(input)
///
/// **CRITICAL**: 在未修复代码上本测试预期 FAIL（失败即确认缺陷存在）。
///
/// **Validates: Requirements 2.1**
/// </summary>
public sealed class UnauthDirectoryReadBugConditionTests
{
    // 随机角色：同时覆盖 Owner 与 Member，用于检验目录是否泄露角色元数据。
    private static readonly Gen<LocalAccountRole> RoleGen =
        Gen.Bool.Select(isOwner => isOwner ? LocalAccountRole.Owner : LocalAccountRole.Member);

    // 智能账户生成器：约束在目录读取真正消费的输入空间内。
    private static readonly Gen<LocalAccount> AccountGen =
        Gen.Select(Gen.String, Gen.String, Gen.Bool, RoleGen,
            (username, display, enabled, role) => MakeAccount(username, display, enabled, role));

    /// <summary>
    /// Property 1a — 字段最小化 / 抑制元数据披露。
    /// 对任意随机账户集合，未认证目录投影的 Role 不得随真实角色变化
    /// （应被规约为单一中性值），否则即泄露了角色名册。
    /// </summary>
    [Fact]
    public void Directory_projection_does_not_disclose_role_metadata()
    {
        AccountGen.List[0, 8].Sample(accounts =>
        {
            // 保证样本中同时存在 Owner 与 Member，使"是否泄露角色区分"可判定。
            var ownerProbe = MakeAccount("owner-probe", "Owner Probe", true, LocalAccountRole.Owner);
            var memberProbe = MakeAccount("member-probe", "Member Probe", true, LocalAccountRole.Member);
            var allAccounts = accounts.Concat(new[] { ownerProbe, memberProbe }).ToList();

            var service = CreateService(allAccounts);
            var directory = service.ListAccountDirectoryAsync().GetAwaiter().GetResult();

            // 期望（修复后）：Role 被规约为中性值，所有条目一致，不暴露 Owner/Member 区分。
            var distinctRoles = directory.Select(summary => summary.Role).Distinct().Count();
            Assert.True(
                distinctRoles <= 1,
                $"未认证目录泄露了角色元数据：返回了 {distinctRoles} 种不同的 Role 值，"
                + "应被规约为单一中性值（最小字段集合仅含 Username / DisplayName / IsEnabled）。");
        });
    }

    /// <summary>
    /// Property 1b — 枚举抑制 / 频率限制。
    /// 对任意高频突发调用，超过合理阈值后应至少有一次被抑制
    /// （抛出受控错误或返回空/受控结果）。
    /// </summary>
    [Fact]
    public void Unauthenticated_directory_reads_are_rate_limited_under_burst()
    {
        var accounts = new[]
        {
            MakeAccount("alice", "Alice", true, LocalAccountRole.Owner),
            MakeAccount("bob", "Bob", true, LocalAccountRole.Member),
        };

        Gen.Int[200, 1000].Sample(burst =>
        {
            // 每个样本使用全新服务实例，确保频率限制状态从零开始累积。
            var service = CreateService(accounts);

            var suppressed = 0;
            for (var i = 0; i < burst; i++)
            {
                try
                {
                    var result = service.ListAccountDirectoryAsync().GetAwaiter().GetResult();
                    if (result.Count == 0)
                    {
                        suppressed++;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 与既有"认证尝试过于频繁"一致的受控失败。
                    suppressed++;
                }
            }

            Assert.True(
                suppressed > 0,
                $"未认证目录读取无频率限制：在一次突发中连续 {burst} 次调用全部全量返回，"
                + "未触发任何抑制。");
        }, iter: 10);
    }

    private static LocalAccountManagementService CreateService(IEnumerable<LocalAccount> accounts)
        => new(new InMemoryLocalAccountRepository(accounts), new FakeSessionContextService());

    private static LocalAccount MakeAccount(string? username, string? displayName, bool enabled, LocalAccountRole role)
        => new()
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Username = username ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Role = role,
            IsEnabled = enabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
}
