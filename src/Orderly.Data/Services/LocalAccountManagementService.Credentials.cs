using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Security;

namespace Orderly.Data.Services;

public sealed partial class LocalAccountManagementService
{
    private const string GenericOwnerCredentialFailureMessage = "管理员验证失败。";
    private const string GenericOwnerRecoveryFailureMessage = "恢复验证失败。";
    private const string GenericMemberRecoveryFailureMessage = "成员恢复验证失败。";

    public async Task VerifyOwnerCredentialsAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        var (_, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        CryptographicOperations.ZeroMemory(ownerDataKey);
    }

    private async Task<(LocalAccount Owner, byte[] OwnerDataKey)> VerifyOwnerCredentialsInternalAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken)
    {
        var owner = await VerifyOwnerIdentityInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        try
        {
            return (
                owner,
                UnwrapDataKey(
                    ownerMasterPassword,
                    owner.PasswordSalt,
                    owner.PasswordIterations,
                    owner.EncryptedDataKey,
                    owner.DataKeyNonce,
                    owner.DataKeyTag));
        }
        catch (CryptographicException)
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposeSignIn, owner.Username);
            throw new InvalidOperationException(GenericOwnerCredentialFailureMessage);
        }
        catch (InvalidOperationException)
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposeSignIn, owner.Username);
            throw new InvalidOperationException(GenericOwnerCredentialFailureMessage);
        }
    }

    private async Task<LocalAccount> VerifyOwnerIdentityInternalAsync(
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            throw new InvalidOperationException("管理员验证信息不完整。");
        }

        if (!LocalCredentialSecurity.IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var normalizedOwnerUsername = LocalCredentialSecurity.NormalizeAccountUsername(ownerUsername);
        ThrowIfCredentialAttemptBlocked(CredentialAttemptPurposeSignIn, normalizedOwnerUsername);

        var owner = await _accountRepository.GetByUsernameAsync(normalizedOwnerUsername, cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposeSignIn, normalizedOwnerUsername);
            throw new InvalidOperationException(GenericOwnerCredentialFailureMessage);
        }

        if (!LocalCredentialSecurity.VerifyHash(ownerMasterPassword, owner.PasswordSalt, owner.PasswordIterations, owner.PasswordHash))
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposeSignIn, normalizedOwnerUsername);
            throw new InvalidOperationException(GenericOwnerCredentialFailureMessage);
        }

        RecordCredentialAttemptResult(CredentialAttemptPurposeSignIn, normalizedOwnerUsername, success: true);
        ThrowIfCredentialAttemptBlocked(CredentialAttemptPurposePin, owner.AccountId);

        if (!LocalCredentialSecurity.VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposePin, owner.AccountId);
            throw new InvalidOperationException(GenericOwnerCredentialFailureMessage);
        }

        RecordCredentialAttemptResult(CredentialAttemptPurposePin, owner.AccountId, success: true);
        return owner;
    }

    public async Task ChangeCurrentMasterPasswordAsync(string currentMasterPassword, string newMasterPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentMasterPassword))
        {
            throw new InvalidOperationException("当前主密码不能为空。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var session = RequireCurrentSession();
        var account = await GetAccountRequiredAsync(session.AccountId, cancellationToken);
        if (!account.IsEnabled)
        {
            throw new InvalidOperationException("当前账号已被禁用。");
        }

        if (!LocalCredentialSecurity.VerifyHash(currentMasterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
        {
            throw new InvalidOperationException("当前主密码错误。");
        }

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = LocalCredentialSecurity.ComputeHash(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, session.DataKey);

        try
        {
            account.PasswordSalt = passwordSalt;
            account.PasswordHash = passwordHash;
            account.PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations;
            account.EncryptedDataKey = wrappedByPassword.Ciphertext;
            account.DataKeyNonce = wrappedByPassword.Nonce;
            account.DataKeyTag = wrappedByPassword.Tag;
            account.UpdatedAt = DateTime.Now;

            await _accountRepository.UpdateAsync(account, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(passwordSalt, passwordHash);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByPassword);
        }
    }

    public async Task ChangeCurrentPinAsync(string currentPin, string newPin, CancellationToken cancellationToken = default)
    {
        if (!LocalCredentialSecurity.IsValidPin(currentPin) || !LocalCredentialSecurity.IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var session = RequireCurrentSession();
        var account = await GetAccountRequiredAsync(session.AccountId, cancellationToken);
        if (!account.IsEnabled)
        {
            throw new InvalidOperationException("当前账号已被禁用。");
        }

        if (!LocalCredentialSecurity.VerifyHash(currentPin, account.PinSalt, account.PinIterations, account.PinHash))
        {
            throw new InvalidOperationException("当前 PIN 错误。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputeHash(newPin, pinSalt, LocalCredentialSecurity.DefaultPinIterations);

        try
        {
            account.PinSalt = pinSalt;
            account.PinHash = pinHash;
            account.PinIterations = LocalCredentialSecurity.DefaultPinIterations;
            account.UpdatedAt = DateTime.Now;
            await _accountRepository.UpdateAsync(account, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(pinSalt, pinHash);
        }
    }

    public async Task ResetMemberMasterPasswordAsync(string memberAccountId, string newMasterPassword, CancellationToken cancellationToken = default)
    {
        var ownerSession = RequireOwnerSession();
        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许重置 Member 主密码。");
        }

        var memberDataKey = UnwrapDataKeyWithKey(ownerSession.DataKey, member.AdminEncryptedDataKey, member.AdminDataKeyNonce, member.AdminDataKeyTag);
        byte[] passwordSalt = [];
        byte[] passwordHash = [];
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        try
        {
            passwordSalt = RandomNumberGenerator.GetBytes(16);
            passwordHash = LocalCredentialSecurity.ComputeHash(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, memberDataKey);

            member.PasswordSalt = passwordSalt;
            member.PasswordHash = passwordHash;
            member.PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations;
            member.EncryptedDataKey = wrappedByPassword.Ciphertext;
            member.DataKeyNonce = wrappedByPassword.Nonce;
            member.DataKeyTag = wrappedByPassword.Tag;
            member.UpdatedAt = DateTime.Now;

            await _accountRepository.UpdateAsync(member, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(memberDataKey, passwordSalt, passwordHash);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByPassword);
        }
    }

    public async Task VerifyMemberPasswordResetAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken = default)
    {
        var (member, ownerDataKey) = await VerifyMemberPasswordResetInternalAsync(
            memberUsername,
            memberPin,
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        CryptographicOperations.ZeroMemory(ownerDataKey);
        GC.KeepAlive(member);
    }

    public async Task ResetMemberMasterPasswordWithOwnerVerificationAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrEmpty(newMasterPassword))
        {
            throw new InvalidOperationException("重置 Member 主密码所需参数不完整。");
        }

        if (!LocalCredentialSecurity.IsValidPin(memberPin.Trim()) || !LocalCredentialSecurity.IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var (member, ownerDataKey) = await VerifyMemberPasswordResetInternalAsync(
            memberUsername,
            memberPin,
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        byte[]? memberDataKey = null;
        byte[] passwordSalt = [];
        byte[] passwordHash = [];
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        try
        {
            memberDataKey = UnwrapDataKeyWithKey(ownerDataKey, member.AdminEncryptedDataKey, member.AdminDataKeyNonce, member.AdminDataKeyTag);
            passwordSalt = RandomNumberGenerator.GetBytes(16);
            passwordHash = LocalCredentialSecurity.ComputeHash(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, memberDataKey);

            member.PasswordSalt = passwordSalt;
            member.PasswordHash = passwordHash;
            member.PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations;
            member.EncryptedDataKey = wrappedByPassword.Ciphertext;
            member.DataKeyNonce = wrappedByPassword.Nonce;
            member.DataKeyTag = wrappedByPassword.Tag;
            member.UpdatedAt = DateTime.Now;

            await _accountRepository.UpdateAsync(member, cancellationToken);
        }
        finally
        {
            if (memberDataKey is not null)
            {
                SensitiveBuffer.Clear(memberDataKey);
            }

            SensitiveBuffer.Clear(ownerDataKey, passwordSalt, passwordHash);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByPassword);
        }
    }

    public async Task ResetMemberPinAsync(string memberAccountId, string newPin, CancellationToken cancellationToken = default)
    {
        RequireOwnerSession();
        if (!LocalCredentialSecurity.IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许重置 Member PIN。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = LocalCredentialSecurity.ComputeHash(newPin, pinSalt, LocalCredentialSecurity.DefaultPinIterations);

        try
        {
            member.PinSalt = pinSalt;
            member.PinHash = pinHash;
            member.PinIterations = LocalCredentialSecurity.DefaultPinIterations;
            member.UpdatedAt = DateTime.Now;
            await _accountRepository.UpdateAsync(member, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(pinSalt, pinHash);
        }
    }

    public async Task ResetOwnerMasterPasswordWithRecoveryKeyAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        string newMasterPassword,
        CancellationToken cancellationToken = default)
    {
        await VerifyOwnerPasswordRecoveryAsync(ownerUsername, ownerPin, recoveryKey, cancellationToken);

        if (string.IsNullOrEmpty(newMasterPassword))
        {
            throw new InvalidOperationException("恢复流程参数不完整。");
        }

        if (!MasterPasswordPolicy.TryValidate(newMasterPassword, out var newPasswordValidationError))
        {
            throw new InvalidOperationException(newPasswordValidationError);
        }

        var normalizedOwnerUsername = LocalCredentialSecurity.NormalizeAccountUsername(ownerUsername);
        var owner = await _accountRepository.GetByUsernameAsync(normalizedOwnerUsername, cancellationToken)
            ?? throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);

        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        byte[] ownerDataKey;
        try
        {
            ownerDataKey = UnwrapDataKey(
                normalizedRecoveryKey,
                owner.RecoveryKeySalt,
                owner.RecoveryKeyIterations,
                owner.RecoveryEncryptedDataKey,
                owner.RecoveryDataKeyNonce,
                owner.RecoveryDataKeyTag);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }

        byte[] passwordSalt = [];
        byte[] passwordHash = [];
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        try
        {
            passwordSalt = RandomNumberGenerator.GetBytes(16);
            passwordHash = LocalCredentialSecurity.ComputeHash(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, LocalCredentialSecurity.DefaultPasswordIterations, ownerDataKey);

            owner.PasswordSalt = passwordSalt;
            owner.PasswordHash = passwordHash;
            owner.PasswordIterations = LocalCredentialSecurity.DefaultPasswordIterations;
            owner.EncryptedDataKey = wrappedByPassword.Ciphertext;
            owner.DataKeyNonce = wrappedByPassword.Nonce;
            owner.DataKeyTag = wrappedByPassword.Tag;
            owner.UpdatedAt = DateTime.Now;
            await _accountRepository.UpdateAsync(owner, cancellationToken);
        }
        finally
        {
            SensitiveBuffer.Clear(ownerDataKey, passwordSalt, passwordHash);
            SensitiveBuffer.ClearWrappedDataKey(wrappedByPassword);
        }
    }

    public async Task VerifyOwnerPasswordRecoveryAsync(
        string ownerUsername,
        string ownerPin,
        string recoveryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerPin)
            || string.IsNullOrWhiteSpace(recoveryKey))
        {
            throw new InvalidOperationException("恢复流程参数不完整。");
        }

        if (!LocalCredentialSecurity.IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var normalizedOwnerUsername = LocalCredentialSecurity.NormalizeAccountUsername(ownerUsername);
        var owner = await _accountRepository.GetByUsernameAsync(normalizedOwnerUsername, cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }

        ThrowIfCredentialAttemptBlocked(CredentialAttemptPurposePin, owner.AccountId);
        if (!LocalCredentialSecurity.VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposePin, owner.AccountId);
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }
        RecordCredentialAttemptResult(CredentialAttemptPurposePin, owner.AccountId, success: true);

        if (!LocalCredentialSecurity.HasUsableHashParameters(owner.RecoveryKeySalt, owner.RecoveryKeyIterations, owner.RecoveryKeyHash)
            || !LocalCredentialSecurity.HasUsableWrappedDataKey(owner.RecoveryEncryptedDataKey, owner.RecoveryDataKeyNonce, owner.RecoveryDataKeyTag))
        {
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }

        var normalizedRecoveryKey = LocalCredentialSecurity.NormalizeRecoveryKey(recoveryKey);
        ThrowIfCredentialAttemptBlocked(CredentialAttemptPurposeRecovery, owner.AccountId);
        if (!LocalCredentialSecurity.VerifyHash(normalizedRecoveryKey, owner.RecoveryKeySalt, owner.RecoveryKeyIterations, owner.RecoveryKeyHash))
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposeRecovery, owner.AccountId);
            throw new InvalidOperationException(GenericOwnerRecoveryFailureMessage);
        }

        RecordCredentialAttemptResult(CredentialAttemptPurposeRecovery, owner.AccountId, success: true);
    }

    private async Task<(LocalAccount Member, byte[] OwnerDataKey)> VerifyMemberPasswordResetInternalAsync(
        string memberUsername,
        string memberPin,
        string ownerUsername,
        string ownerMasterPassword,
        string ownerPin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(memberUsername)
            || string.IsNullOrWhiteSpace(memberPin)
            || string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            throw new InvalidOperationException("重置 Member 主密码所需参数不完整。");
        }

        if (!LocalCredentialSecurity.IsValidPin(memberPin.Trim()) || !LocalCredentialSecurity.IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var normalizedMemberUsername = LocalCredentialSecurity.NormalizeAccountUsername(memberUsername);
        var member = await _accountRepository.GetByUsernameAsync(normalizedMemberUsername, cancellationToken);
        if (member is null || member.Role != LocalAccountRole.Member || !member.IsEnabled)
        {
            throw new InvalidOperationException(GenericMemberRecoveryFailureMessage);
        }

        ThrowIfCredentialAttemptBlocked(CredentialAttemptPurposePin, member.AccountId);
        if (!LocalCredentialSecurity.VerifyHash(memberPin.Trim(), member.PinSalt, member.PinIterations, member.PinHash))
        {
            RecordCredentialAttemptFailure(CredentialAttemptPurposePin, member.AccountId);
            throw new InvalidOperationException(GenericMemberRecoveryFailureMessage);
        }
        RecordCredentialAttemptResult(CredentialAttemptPurposePin, member.AccountId, success: true);

        var (owner, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        if (!string.Equals(member.AdminOwnerAccountId, owner.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            CryptographicOperations.ZeroMemory(ownerDataKey);
            throw new InvalidOperationException(GenericMemberRecoveryFailureMessage);
        }

        return (member, ownerDataKey);
    }
}
