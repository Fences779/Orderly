using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ILocalAccountManagementService
{
    Task<IReadOnlyList<LocalAccountSummary>> ListAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalAccountSummary>> ListAccountDirectoryAsync(CancellationToken cancellationToken = default);
    Task<LocalAccountSummary> CreateMemberAsync(CreateMemberAccountRequest request, CancellationToken cancellationToken = default);
    Task VerifyOwnerCredentialsAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default);
    Task<LocalAccountSummary> CreateMemberWithOwnerVerificationAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CreateMemberAccountRequest request,
        CancellationToken cancellationToken = default);
    Task DisableMemberAsync(string memberAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除成员（移除其登录账号本身），与停用（仅置 <c>IsEnabled=false</c> 并保留账号）作为彼此区分的两种独立能力（需求 7.4 / 7.6 / 7.10）。
    /// 删除仅移除登录账号记录（<c>LocalAccount</c>），保留其名下全部历史业务数据：不级联删除业务数据工作区、不匿名化，
    /// 业务数据的「来源 / 创建人」仍保留该（已删除）账号标签 / 标识用于归属展示。
    /// 服务层依据成员管理权限矩阵（<see cref="Orderly.Core.Security.MemberManagementPolicy"/>）双重校验（仅 Owner 且目标非自身），被拒绝时不执行任何后端操作。
    /// </summary>
    /// <param name="memberAccountId">待删除的成员账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteMemberAsync(string memberAccountId, CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(string ownerUsername, string ownerMasterPassword, string ownerPin, string targetAccountId, CancellationToken cancellationToken = default);
    Task ChangeCurrentMasterPasswordAsync(string currentMasterPassword, string newMasterPassword, CancellationToken cancellationToken = default);
    Task ChangeCurrentPinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default);
    Task ResetMemberMasterPasswordAsync(string memberAccountId, string newMasterPassword, CancellationToken cancellationToken = default);
    Task VerifyMemberPasswordResetAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default);
    Task ResetMemberMasterPasswordWithOwnerVerificationAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string newMasterPassword,
        CancellationToken cancellationToken = default);
    Task ResetMemberPinAsync(string memberAccountId, string newPin, CancellationToken cancellationToken = default);
    Task ResetOwnerMasterPasswordWithRecoveryKeyAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        string newMasterPassword,
        CancellationToken cancellationToken = default);
    Task VerifyOwnerPasswordRecoveryAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        CancellationToken cancellationToken = default);
}
