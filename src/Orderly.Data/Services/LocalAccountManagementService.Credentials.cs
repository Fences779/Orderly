using System.Security.Cryptography;
using Orderly.Core.Models;
using Orderly.Core.Security;

namespace Orderly.Data.Services;

public sealed partial class LocalAccountManagementService
{
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
        if (string.IsNullOrWhiteSpace(ownerUsername)
            || string.IsNullOrWhiteSpace(ownerMasterPassword)
            || string.IsNullOrWhiteSpace(ownerPin))
        {
            throw new InvalidOperationException("管理员验证信息不完整。");
        }

        if (!IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException("主账号不存在或不可用。");
        }

        if (!VerifyHash(ownerMasterPassword, owner.PasswordSalt, owner.PasswordIterations, owner.PasswordHash))
        {
            throw new InvalidOperationException("主账号主密码错误。");
        }

        if (!VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            throw new InvalidOperationException("主账号 PIN 错误。");
        }

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
            throw new InvalidOperationException("主账号密钥解包失败。");
        }
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

        if (!VerifyHash(currentMasterPassword, account.PasswordSalt, account.PasswordIterations, account.PasswordHash))
        {
            throw new InvalidOperationException("当前主密码错误。");
        }

        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
        var wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, session.DataKey);

        try
        {
            account.PasswordSalt = passwordSalt;
            account.PasswordHash = passwordHash;
            account.PasswordIterations = DefaultPasswordIterations;
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
        if (!IsValidPin(currentPin) || !IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var session = RequireCurrentSession();
        var account = await GetAccountRequiredAsync(session.AccountId, cancellationToken);
        if (!account.IsEnabled)
        {
            throw new InvalidOperationException("当前账号已被禁用。");
        }

        if (!VerifyHash(currentPin, account.PinSalt, account.PinIterations, account.PinHash))
        {
            throw new InvalidOperationException("当前 PIN 错误。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = ComputeHash(newPin, pinSalt, DefaultPinIterations);

        try
        {
            account.PinSalt = pinSalt;
            account.PinHash = pinHash;
            account.PinIterations = DefaultPinIterations;
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
            passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);

            member.PasswordSalt = passwordSalt;
            member.PasswordHash = passwordHash;
            member.PasswordIterations = DefaultPasswordIterations;
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

        if (!IsValidPin(memberPin.Trim()) || !IsValidPin(ownerPin.Trim()))
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
            passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, memberDataKey);

            member.PasswordSalt = passwordSalt;
            member.PasswordHash = passwordHash;
            member.PasswordIterations = DefaultPasswordIterations;
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
        if (!IsValidPin(newPin))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var member = await GetAccountRequiredAsync(memberAccountId, cancellationToken);
        if (member.Role != LocalAccountRole.Member)
        {
            throw new InvalidOperationException("仅允许重置 Member PIN。");
        }

        var pinSalt = RandomNumberGenerator.GetBytes(16);
        var pinHash = ComputeHash(newPin, pinSalt, DefaultPinIterations);

        try
        {
            member.PinSalt = pinSalt;
            member.PinHash = pinHash;
            member.PinIterations = DefaultPinIterations;
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

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken)
            ?? throw new InvalidOperationException("Owner 账号不存在或不可用。");

        var normalizedRecoveryKey = recoveryKey.Trim().ToUpperInvariant();
        var ownerDataKey = UnwrapDataKey(
            normalizedRecoveryKey,
            owner.RecoveryKeySalt,
            owner.RecoveryKeyIterations,
            owner.RecoveryEncryptedDataKey,
            owner.RecoveryDataKeyNonce,
            owner.RecoveryDataKeyTag);

        byte[] passwordSalt = [];
        byte[] passwordHash = [];
        (byte[] Ciphertext, byte[] Nonce, byte[] Tag) wrappedByPassword = ([], [], []);
        try
        {
            passwordSalt = RandomNumberGenerator.GetBytes(16);
            passwordHash = ComputeHash(newMasterPassword, passwordSalt, DefaultPasswordIterations);
            wrappedByPassword = WrapDataKey(newMasterPassword, passwordSalt, DefaultPasswordIterations, ownerDataKey);

            owner.PasswordSalt = passwordSalt;
            owner.PasswordHash = passwordHash;
            owner.PasswordIterations = DefaultPasswordIterations;
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

        if (!IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var owner = await _accountRepository.GetByUsernameAsync(ownerUsername.Trim(), cancellationToken);
        if (owner is null || owner.Role != LocalAccountRole.Owner || !owner.IsEnabled)
        {
            throw new InvalidOperationException("Owner 账号不存在或不可用。");
        }

        if (!VerifyHash(ownerPin.Trim(), owner.PinSalt, owner.PinIterations, owner.PinHash))
        {
            throw new InvalidOperationException("PIN 校验失败。");
        }

        var normalizedRecoveryKey = recoveryKey.Trim().ToUpperInvariant();
        if (!VerifyHash(normalizedRecoveryKey, owner.RecoveryKeySalt, owner.RecoveryKeyIterations, owner.RecoveryKeyHash))
        {
            throw new InvalidOperationException("Recovery Key 校验失败。");
        }

        if (owner.RecoveryEncryptedDataKey.Length == 0 || owner.RecoveryDataKeyNonce.Length == 0 || owner.RecoveryDataKeyTag.Length == 0)
        {
            throw new InvalidOperationException("Owner 账号缺少恢复密钥包裹的数据密钥，无法执行恢复。");
        }
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

        if (!IsValidPin(memberPin.Trim()) || !IsValidPin(ownerPin.Trim()))
        {
            throw new InvalidOperationException("PIN 必须为 6 位数字。");
        }

        var member = await _accountRepository.GetByUsernameAsync(memberUsername.Trim(), cancellationToken);
        if (member is null || member.Role != LocalAccountRole.Member || !member.IsEnabled)
        {
            throw new InvalidOperationException("成员账号不存在或不可用。");
        }

        if (!VerifyHash(memberPin.Trim(), member.PinSalt, member.PinIterations, member.PinHash))
        {
            throw new InvalidOperationException("成员账号 PIN 错误。");
        }

        var (owner, ownerDataKey) = await VerifyOwnerCredentialsInternalAsync(
            ownerUsername,
            ownerMasterPassword,
            ownerPin,
            cancellationToken);

        if (!string.Equals(member.AdminOwnerAccountId, owner.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            CryptographicOperations.ZeroMemory(ownerDataKey);
            throw new InvalidOperationException("该成员账号不属于当前主账号，无法重置主密码。");
        }

        return (member, ownerDataKey);
    }
}
