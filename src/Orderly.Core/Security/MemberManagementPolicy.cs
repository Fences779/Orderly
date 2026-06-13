using Orderly.Core.Models;

namespace Orderly.Core.Security;

/// <summary>
/// 成员管理权限矩阵纯函数判定（需求 7.5 / 7.6 / 7.7 / 7.8 / 7.9，design §8.1.1，Property 12）。
///
/// 「创建 / 删除 / 停用」三类成员操作的授权严格由「当前账号角色 + 是否目标为自身」决定，
/// 不读取任何 UI 状态、不依赖 ViewModel，便于属性测试与服务层 / VM 层复用。
///
/// 真值表（design §8.1.1）：
/// <list type="bullet">
///   <item>创建：<c>CanCreateMember = IsCurrentUserOwner</c>（仅 Owner）。</item>
///   <item>删除：<c>CanDeleteMember(t) = IsCurrentUserOwner AND (t 非自身)</c>（仅 Owner 且目标非自身，任何人不可删自身）。</item>
///   <item>停用：<c>CanDisableMember(t) = IsCurrentUserOwner OR (t 为自身)</c>（Owner 任意 / 或目标为自身）。</item>
/// </list>
/// </summary>
public static class MemberManagementPolicy
{
    /// <summary>是否允许创建成员（需求 7.5）：仅当前账号为 <see cref="LocalAccountRole.Owner"/> 时允许。</summary>
    /// <param name="currentUserRole">当前登录账号的角色。</param>
    public static bool CanCreateMember(LocalAccountRole currentUserRole)
        => currentUserRole == LocalAccountRole.Owner;

    /// <summary>
    /// 是否允许删除目标成员（需求 7.6 / 7.8 / 7.9 / 7.10）：仅当前账号为 <see cref="LocalAccountRole.Owner"/>
    /// 且目标不是当前账号自身时允许（任何人不可删自身，含 Owner 不可删自身）。
    /// </summary>
    /// <param name="currentUserRole">当前登录账号的角色。</param>
    /// <param name="targetIsSelf">删除目标是否为当前账号自身。</param>
    public static bool CanDeleteMember(LocalAccountRole currentUserRole, bool targetIsSelf)
        => currentUserRole == LocalAccountRole.Owner && !targetIsSelf;

    /// <summary>
    /// 是否允许停用目标成员（需求 7.7 / 7.8 / 7.9）：当前账号为 <see cref="LocalAccountRole.Owner"/>（可停用任何人，含自身），
    /// 或目标为当前账号自身（Member 可停用自身）时允许。
    /// </summary>
    /// <param name="currentUserRole">当前登录账号的角色。</param>
    /// <param name="targetIsSelf">停用目标是否为当前账号自身。</param>
    public static bool CanDisableMember(LocalAccountRole currentUserRole, bool targetIsSelf)
        => currentUserRole == LocalAccountRole.Owner || targetIsSelf;

    /// <summary>
    /// 便于 VM 层复用的删除判定重载：依据当前账号 ID 与目标账号 ID 推导「是否自身」。
    /// </summary>
    /// <param name="currentUserRole">当前登录账号的角色。</param>
    /// <param name="currentAccountId">当前登录账号 ID。</param>
    /// <param name="targetAccountId">删除目标账号 ID。</param>
    public static bool CanDeleteMember(LocalAccountRole currentUserRole, string? currentAccountId, string? targetAccountId)
        => CanDeleteMember(currentUserRole, IsSelf(currentAccountId, targetAccountId));

    /// <summary>
    /// 便于 VM 层复用的停用判定重载：依据当前账号 ID 与目标账号 ID 推导「是否自身」。
    /// </summary>
    /// <param name="currentUserRole">当前登录账号的角色。</param>
    /// <param name="currentAccountId">当前登录账号 ID。</param>
    /// <param name="targetAccountId">停用目标账号 ID。</param>
    public static bool CanDisableMember(LocalAccountRole currentUserRole, string? currentAccountId, string? targetAccountId)
        => CanDisableMember(currentUserRole, IsSelf(currentAccountId, targetAccountId));

    /// <summary>判定目标账号是否为当前账号自身（按账号 ID 序号比较，空 ID 视为非自身）。</summary>
    private static bool IsSelf(string? currentAccountId, string? targetAccountId)
        => !string.IsNullOrEmpty(currentAccountId)
            && string.Equals(currentAccountId, targetAccountId, StringComparison.Ordinal);
}
