using CsCheck;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property 7 (Preservation) — 登录选择器可用性保持不变。
///
/// 本测试遵循"观察优先（observation-first）"方法学：在**未修复代码**上观察
/// 喂给登录选择器的后端数据源 <c>ListAccountDirectoryAsync</c> 的可观察行为，
/// 并将其固化为属性测试，作为修复（任务 7.1）必须保持的回归防护契约。
///
/// **范围与 UI 禁令（已记录）**：
///   登录选择器入口 <c>LoadSignInAccounts</c> 位于 <c>LoginViewModel</c>（UI 层 / 受 AGENTS.md 锁定），
///   本测试**绝不触碰**该 UI 表面。改为断言其后端数据源 <c>ListAccountDirectoryAsync</c> 的输出：
///   经 design.md 代码核对，登录选择器仅消费最小字段集合 <c>Username</c> / <c>DisplayName</c> /
///   <c>IsEnabled</c>。本测试断言这些最小字段在修复前后均被正确填充、足以让选择器列出并确认登录账号。
///
/// 对应 design.md：
///   - 未认证目录读取在低频（非突发）下恒为 ¬isBugCondition（CASE "UnauthDirectoryRead" 仅在
///     过度披露元数据 或 未限频 时为 true；单次低频读取不触发限频抑制）。
///   - Preservation Checking：FOR ALL input WHERE NOT isBugCondition(input):
///                            originalFunction(input) == fixedFunction(input)
///
/// **观察到的基线行为（未修复代码）**：
///   1. <c>ListAccountDirectoryAsync</c> 为每个账户返回恰一条摘要（数量一致）。
///   2. 每条摘要的 <c>Username</c> / <c>DisplayName</c> / <c>IsEnabled</c> 与源账户一致。
///   3. 结果按源账户 <c>CreatedAt</c> 升序排列（选择器的稳定列举顺序）。
///   4. 启用/禁用标记被如实保留，使选择器可据此过滤可登录账户。
///
/// **注意**：本测试**不**断言 <c>Role</c>（修复 7.1 将把 Role 规约为中性值，属预期变化，
/// 非保持项；Role 的最小化由 Property 1 的 Bug Condition 测试覆盖）。
///
/// **EXPECTED OUTCOME**: 在未修复代码上 PASS（确认登录选择器最小字段可用性基线）。
///
/// **Validates: Requirements 3.6, 3.10**
/// </summary>
public sealed class LoginSelectorPreservationTests
{
    private static readonly Gen<LocalAccountRole> RoleGen =
        Gen.Bool.Select(isOwner => isOwner ? LocalAccountRole.Owner : LocalAccountRole.Member);

    // 单个账户的可观察字段（约束在登录选择器消费的最小输入空间内）。
    private readonly record struct AccountSpec(string Display, bool Enabled, LocalAccountRole Role);

    private static readonly Gen<AccountSpec> AccountSpecGen =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 12].Select(chars => new string(chars)),
            Gen.Bool,
            RoleGen,
            (display, enabled, role) => new AccountSpec(display, enabled, role));

    /// <summary>
    /// Property 7 — 目录最小字段集合（Username / DisplayName / IsEnabled）足以列出并确认登录账号，
    /// 且数量、顺序与启用标记均保持基线行为。
    /// </summary>
    [Fact]
    public void Directory_preserves_minimal_fields_usable_by_login_selector()
    {
        AccountSpecGen.List[1, 8].Sample(specs =>
        {
            // 用递增 CreatedAt + 唯一用户名构造确定可判定的账户集合。
            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);
            var accounts = specs
                .Select((spec, index) => new LocalAccount
                {
                    AccountId = Guid.NewGuid().ToString("N"),
                    Username = $"user-{index:D3}",
                    DisplayName = spec.Display,
                    Role = spec.Role,
                    IsEnabled = spec.Enabled,
                    CreatedAt = baseTime.AddMinutes(index),
                    UpdatedAt = baseTime.AddMinutes(index),
                })
                .ToList();

            var service = new LocalAccountManagementService(
                new InMemoryLocalAccountRepository(accounts),
                new FakeSessionContextService());

            var directory = service.ListAccountDirectoryAsync().GetAwaiter().GetResult();

            // (1) 数量一致：每个账户恰一条。
            Assert.Equal(accounts.Count, directory.Count);

            // (3) 顺序：按源 CreatedAt 升序（这里即构造顺序 user-000, user-001, ...）。
            var expectedUsernameOrder = accounts
                .OrderBy(a => a.CreatedAt)
                .Select(a => a.Username)
                .ToList();
            Assert.Equal(expectedUsernameOrder, directory.Select(s => s.Username).ToList());

            // (2)(4) 最小字段逐账户对齐：DisplayName 与 IsEnabled 如实保留，选择器据此列出/过滤。
            foreach (var account in accounts)
            {
                var summary = Assert.Single(
                    directory.Where(s => s.Username == account.Username));
                Assert.Equal(account.DisplayName, summary.DisplayName);
                Assert.Equal(account.IsEnabled, summary.IsEnabled);
                Assert.False(string.IsNullOrEmpty(summary.Username));
            }
        });
    }

    /// <summary>
    /// Property 7 边界（确定性单元用例）：仅设置最小字段（Username/DisplayName/IsEnabled）的账户，
    /// 目录仍能正确列出并确认（启用与禁用各一条）。
    /// </summary>
    [Fact]
    public void Directory_lists_accounts_with_only_minimal_fields_set()
    {
        var accounts = new[]
        {
            new LocalAccount
            {
                AccountId = Guid.NewGuid().ToString("N"),
                Username = "enabled-user",
                DisplayName = "Enabled User",
                IsEnabled = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local),
            },
            new LocalAccount
            {
                AccountId = Guid.NewGuid().ToString("N"),
                Username = "disabled-user",
                DisplayName = "Disabled User",
                IsEnabled = false,
                CreatedAt = new DateTime(2024, 1, 1, 0, 1, 0, DateTimeKind.Local),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 1, 0, DateTimeKind.Local),
            },
        };

        var service = new LocalAccountManagementService(
            new InMemoryLocalAccountRepository(accounts),
            new FakeSessionContextService());

        var directory = service.ListAccountDirectoryAsync().GetAwaiter().GetResult();

        Assert.Equal(2, directory.Count);

        var enabled = Assert.Single(directory.Where(s => s.Username == "enabled-user"));
        Assert.Equal("Enabled User", enabled.DisplayName);
        Assert.True(enabled.IsEnabled);

        var disabled = Assert.Single(directory.Where(s => s.Username == "disabled-user"));
        Assert.Equal("Disabled User", disabled.DisplayName);
        Assert.False(disabled.IsEnabled);
    }
}
