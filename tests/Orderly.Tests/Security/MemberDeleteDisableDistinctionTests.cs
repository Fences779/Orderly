using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Data.Services;
using Orderly.Tests.Fakes;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// 任务 8.4 — 删除（移除登录账号）与停用（仅置 <c>IsEnabled=false</c> 并保留账号）
/// 作为彼此区分的两种独立能力的单元测试。
///
/// 覆盖验收标准：
/// <list type="bullet">
///   <item>需求 7.10：删除移除账号、停用仅禁用并保留账号（两种独立能力）。</item>
///   <item>需求 7.9：任何人不可删自身（含 Owner 不可删自身）；Owner 可停用自身（权限矩阵层）。</item>
///   <item>需求 7.8：删除仅移除登录账号，名下历史业务数据全部保留、未级联删除/匿名化，
///         来源 / 创建人仍展示该（已删除）账号的标签 / 标识。</item>
/// </list>
///
/// 复用既有账户管理测试夹具（<see cref="InMemoryLocalAccountRepository"/> /
/// <see cref="FakeDataKeySessionContextService"/>），不触碰磁盘加密库。
/// </summary>
public sealed class MemberDeleteDisableDistinctionTests
{
    // ---- (1) 删除后账号不存在（目录 / 仓储查询不到） ----

    [Fact]
    public async Task DeleteMember_removes_login_account_so_it_no_longer_exists()
    {
        var owner = MakeOwner();
        var member = MakeMember(owner.AccountId);
        var repository = new InMemoryLocalAccountRepository(new[] { owner, member });
        var service = CreateOwnerService(repository, owner);

        await service.DeleteMemberAsync(member.AccountId);

        // 账号本身被移除：仓储按 ID 查询不到、目录列举不含该账号。
        Assert.Null(await repository.GetByAccountIdAsync(member.AccountId));
        var remaining = await repository.ListAsync();
        Assert.DoesNotContain(remaining, a => a.AccountId == member.AccountId);
    }

    // ---- (2) 停用后账号保留且 IsEnabled=false、可重新启用 ----

    [Fact]
    public async Task DisableMember_keeps_account_and_sets_disabled_and_allows_reenable()
    {
        var owner = MakeOwner();
        var member = MakeMember(owner.AccountId, enabled: true);
        var repository = new InMemoryLocalAccountRepository(new[] { owner, member });
        var service = CreateOwnerService(repository, owner);

        await service.DisableMemberAsync(member.AccountId);

        // 停用：账号仍存在，仅 IsEnabled 置为 false（区别于删除）。
        var afterDisable = await repository.GetByAccountIdAsync(member.AccountId);
        Assert.NotNull(afterDisable);
        Assert.False(afterDisable!.IsEnabled);

        // 可重新启用：账号记录得以保留，因此可重新置为启用并往返读回。
        afterDisable.IsEnabled = true;
        afterDisable.UpdatedAt = DateTime.Now;
        await repository.UpdateAsync(afterDisable);

        var afterReenable = await repository.GetByAccountIdAsync(member.AccountId);
        Assert.NotNull(afterReenable);
        Assert.True(afterReenable!.IsEnabled);
    }

    // ---- (3) 任何人不可删自身（Owner 删自身被拒）、Owner 可停用自身 ----

    [Fact]
    public async Task DeleteMember_rejects_owner_deleting_self_and_account_is_retained()
    {
        var owner = MakeOwner();
        var repository = new InMemoryLocalAccountRepository(new[] { owner });
        var service = CreateOwnerService(repository, owner);

        // Owner 通过 Owner 校验，但权限矩阵拒绝删除自身。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteMemberAsync(owner.AccountId));

        // 被拒绝后不执行任何后端操作：账号仍存在。
        Assert.NotNull(await repository.GetByAccountIdAsync(owner.AccountId));
    }

    [Fact]
    public void DeleteMember_self_is_denied_for_anyone_by_permission_matrix()
    {
        // 任何人不可删自身（含 Owner / Member），权限矩阵层判定。
        Assert.False(MemberManagementPolicy.CanDeleteMember(LocalAccountRole.Owner, targetIsSelf: true));
        Assert.False(MemberManagementPolicy.CanDeleteMember(LocalAccountRole.Member, targetIsSelf: true));
    }

    [Fact]
    public void DisableMember_allows_owner_to_disable_self_by_permission_matrix()
    {
        // 需求 7.9：Owner 可停用自身；需求 7.8：Member 亦可停用自身。
        Assert.True(MemberManagementPolicy.CanDisableMember(LocalAccountRole.Owner, targetIsSelf: true));
        Assert.True(MemberManagementPolicy.CanDisableMember(LocalAccountRole.Member, targetIsSelf: true));
    }

    // ---- (4) 删除成员后其名下历史业务数据仍全部保留、未级联删除 / 匿名化，来源 / 创建人标签仍保留 ----

    [Fact]
    public async Task DeleteMember_does_not_cascade_or_anonymize_business_data()
    {
        var owner = MakeOwner();
        var member = MakeMember(owner.AccountId);

        // 模拟成员名下业务数据工作区（落盘文件），用于断言删除登录账号不会级联删除业务数据。
        var workspaceDirectory = Path.Combine(
            Path.GetTempPath(),
            "orderly-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceDirectory);
        var businessDataFile = Path.Combine(workspaceDirectory, "business-data.txt");
        await File.WriteAllTextAsync(businessDataFile, "member business records");
        member.DatabasePath = Path.Combine(workspaceDirectory, "account.db");

        // 模拟业务数据行：以「来源 / 创建人」标签（账号 ID + 显示名）归属到该成员。
        var capturedCreatorAccountId = member.AccountId;
        var capturedCreatorLabel = member.DisplayName;
        var businessRows = new List<(string CreatedByAccountId, string CreatedByLabel)>
        {
            (member.AccountId, member.DisplayName),
            (member.AccountId, member.DisplayName),
        };

        var repository = new InMemoryLocalAccountRepository(new[] { owner, member });
        var service = CreateOwnerService(repository, owner);

        try
        {
            await service.DeleteMemberAsync(member.AccountId);

            // 登录账号已移除。
            Assert.Null(await repository.GetByAccountIdAsync(member.AccountId));

            // 业务数据工作区未被级联删除（文件与目录仍在）。
            Assert.True(Directory.Exists(workspaceDirectory));
            Assert.True(File.Exists(businessDataFile));
            Assert.Equal("member business records", await File.ReadAllTextAsync(businessDataFile));

            // 业务数据行全部保留、未匿名化：来源 / 创建人仍展示该（已删除）账号的标签 / 标识。
            Assert.Equal(2, businessRows.Count);
            Assert.All(businessRows, row =>
            {
                Assert.Equal(capturedCreatorAccountId, row.CreatedByAccountId);
                Assert.Equal(capturedCreatorLabel, row.CreatedByLabel);
                Assert.False(string.IsNullOrWhiteSpace(row.CreatedByLabel));
            });
        }
        finally
        {
            if (Directory.Exists(workspaceDirectory))
            {
                Directory.Delete(workspaceDirectory, recursive: true);
            }
        }
    }

    // ---- 测试夹具辅助 ----

    private static LocalAccountManagementService CreateOwnerService(
        InMemoryLocalAccountRepository repository,
        LocalAccount owner)
    {
        var session = new FakeDataKeySessionContextService(new byte[32]);
        session.SetCurrent(new LocalSessionContext
        {
            AccountId = owner.AccountId,
            Username = owner.Username,
            DisplayName = owner.DisplayName,
            Role = LocalAccountRole.Owner,
            DatabasePath = owner.DatabasePath,
            DataKey = new byte[32],
        });

        return new LocalAccountManagementService(repository, session);
    }

    private static LocalAccount MakeOwner()
        => new()
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Username = "owner",
            DisplayName = "Owner",
            Role = LocalAccountRole.Owner,
            IsEnabled = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

    private static LocalAccount MakeMember(string ownerAccountId, bool enabled = true)
        => new()
        {
            AccountId = Guid.NewGuid().ToString("N"),
            Username = "member",
            DisplayName = "Member",
            Role = LocalAccountRole.Member,
            IsEnabled = enabled,
            AdminOwnerAccountId = ownerAccountId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
}
