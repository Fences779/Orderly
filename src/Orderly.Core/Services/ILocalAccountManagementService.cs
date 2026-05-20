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
