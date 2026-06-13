using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Orderly.Core.Models;
using Orderly.Core.Security;
using Orderly.Tests.Support;
using Xunit;

namespace Orderly.Tests.Security;

/// <summary>
/// Property-based test for the member management permission matrix
/// (design §8.1.1 真值表 / §11 Property 12).
///
/// <para><b>Property 12: 成员管理权限矩阵.</b>
/// 对任意 (角色 ∈ {Owner, Member}, 是否目标为自身) 组合，<see cref="MemberManagementPolicy"/>
/// 的三个纯函数判定恒满足以下等价关系：
/// <list type="bullet">
///   <item><c>CanCreateMember ⟺ role == Owner</c>（创建仅 Owner，需求 7.5）。</item>
///   <item><c>CanDeleteMember ⟺ role == Owner ∧ ¬自身</c>（删除仅 Owner 且目标非自身，任何人不可删自身，需求 7.6 / 7.8 / 7.9 / 7.10）。</item>
///   <item><c>CanDisableMember ⟺ role == Owner ∨ 自身</c>（停用 Owner 任意，或目标为自身，需求 7.7 / 7.8 / 7.9）。</item>
/// </list></para>
///
/// <para>判定为纯函数，输入空间仅 2 角色 × 2 是否自身 = 4 种组合。测试既以 CsCheck 在该
/// 离散空间上随机抽样断言等价关系（不读取被测产物，独立重算期望，避免循环论证），又显式
/// <b>全覆盖枚举</b>全部 4 种组合逐一断言，确保真值表每一格都被验证。</para>
///
/// **Validates: Requirements 7.5, 7.6, 7.7, 7.8, 7.9, 7.10**
/// </summary>
public sealed class MemberManagementPolicyMatrixPropertyTests
{
    private static readonly Gen<LocalAccountRole> RoleGen =
        Gen.OneOfConst(LocalAccountRole.Owner, LocalAccountRole.Member);

    private static readonly Gen<(LocalAccountRole Role, bool TargetIsSelf)> CaseGen =
        from role in RoleGen
        from targetIsSelf in Gen.Bool
        select (role, targetIsSelf);

    [Fact]
    public void Property12_create_delete_disable_match_truth_table_for_arbitrary_combination()
    {
        CaseGen.Sample(
            c =>
            {
                bool isOwner = c.Role == LocalAccountRole.Owner;

                // 创建：role == Owner（创建无目标，仅看角色）。
                Assert.Equal(isOwner, MemberManagementPolicy.CanCreateMember(c.Role));

                // 删除：role == Owner ∧ ¬自身（任何人不可删自身，含 Owner 不可删自身）。
                Assert.Equal(
                    isOwner && !c.TargetIsSelf,
                    MemberManagementPolicy.CanDeleteMember(c.Role, c.TargetIsSelf));

                // 停用：role == Owner ∨ 自身（Owner 可停用任何人含自身；Member 仅可停用自身）。
                Assert.Equal(
                    isOwner || c.TargetIsSelf,
                    MemberManagementPolicy.CanDisableMember(c.Role, c.TargetIsSelf));
            },
            iter: PbtConfig.MinIterations);
    }

    [Fact]
    public void Property12_exhaustive_enumeration_covers_all_four_combinations()
    {
        // 全覆盖枚举：2 角色 × 2 是否自身 = 4 种组合，逐一断言真值表的每一格。
        var roles = new[] { LocalAccountRole.Owner, LocalAccountRole.Member };
        var selfFlags = new[] { true, false };

        var seen = new List<(LocalAccountRole, bool)>();

        foreach (LocalAccountRole role in roles)
        {
            foreach (bool targetIsSelf in selfFlags)
            {
                seen.Add((role, targetIsSelf));
                bool isOwner = role == LocalAccountRole.Owner;

                Assert.Equal(isOwner, MemberManagementPolicy.CanCreateMember(role));
                Assert.Equal(
                    isOwner && !targetIsSelf,
                    MemberManagementPolicy.CanDeleteMember(role, targetIsSelf));
                Assert.Equal(
                    isOwner || targetIsSelf,
                    MemberManagementPolicy.CanDisableMember(role, targetIsSelf));
            }
        }

        // 确认 4 种组合全部被覆盖且互不重复。
        Assert.Equal(4, seen.Distinct().Count());
    }
}
