using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed class QuickLoginService : IQuickLoginService
{
    private const int DataKeyLength = 32;
    private const int MaxTicketLength = 4096;
    private const int SystemBootEnvironmentInformationClass = 90;
    private static readonly byte[] ProtectionEntropy = SHA256.HashData("Orderly.QuickLoginTicket.v1"u8);

    private readonly ILocalAccountRepository _accounts;
    private readonly ISessionContextService _sessionContext;
    private readonly ILocalAuthService _authService;
    private readonly ISecurityAuditService? _audit;

    public QuickLoginService(
        ILocalAccountRepository accounts,
        ISessionContextService sessionContext,
        ILocalAuthService authService,
        ISecurityAuditService? audit = null)
    {
        _accounts = accounts;
        _sessionContext = sessionContext;
        _authService = authService;
        _audit = audit;
    }

    public async Task<QuickLoginStatus> GetStatusAsync(string username, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByUsernameAsync(username, cancellationToken);
        if (account is null || !account.IsEnabled || !account.QuickLoginEnabled)
        {
            return new QuickLoginStatus(false, false);
        }

        if (!await LocalAccountDatabasePathRepair.TryRepairAsync(_accounts, account, cancellationToken))
        {
            return new QuickLoginStatus(account.QuickLoginEnabled, false);
        }

        var available = TryReadTicket(account, out var dataKey);
        CryptographicOperations.ZeroMemory(dataKey);
        return new QuickLoginStatus(true, available);
    }

    public async Task SetEnabledForCurrentAccountAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var session = _sessionContext.Current ?? throw new InvalidOperationException("当前没有已登录账号。");
        var account = await _accounts.GetByAccountIdAsync(session.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("当前账号不存在。");

        if (!await LocalAccountDatabasePathRepair.TryRepairAsync(_accounts, account, cancellationToken))
        {
            throw new InvalidOperationException("当前账号数据路径异常，无法修改快速登录设置。");
        }

        if (!enabled)
        {
            account.QuickLoginEnabled = false;
            account.UpdatedAt = DateTime.Now;
            await _accounts.UpdateAsync(account, cancellationToken);
            DeleteTicket(account.AccountId);
            return;
        }

        if (session.DataKey.Length != DataKeyLength)
        {
            throw new InvalidOperationException("当前会话密钥不可用，请使用主密码重新登录后再开启。");
        }

        account.QuickLoginEnabled = true;
        account.UpdatedAt = DateTime.Now;
        WriteTicket(account, session.DataKey);
        try
        {
            await _accounts.UpdateAsync(account, cancellationToken);
        }
        catch
        {
            DeleteTicket(account.AccountId);
            throw;
        }
    }

    public async Task CaptureCurrentPasswordSessionAsync(
        string username,
        bool enableQuickLogin,
        CancellationToken cancellationToken = default)
    {
        var session = _sessionContext.Current ?? throw new InvalidOperationException("密码登录会话不可用。");
        var account = await _accounts.GetByUsernameAsync(username, cancellationToken)
            ?? throw new InvalidOperationException("登录账号不存在。");
        if (!string.Equals(account.AccountId, session.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("登录账号与当前会话不一致。");
        }

        if (!await LocalAccountDatabasePathRepair.TryRepairAsync(_accounts, account, cancellationToken))
        {
            throw new InvalidOperationException("登录账号数据路径异常，无法保存快速登录状态。");
        }

        if (!enableQuickLogin)
        {
            return;
        }

        if (session.DataKey.Length != DataKeyLength)
        {
            throw new InvalidOperationException("密码登录会话密钥不可用。");
        }

        account.QuickLoginEnabled = true;
        account.UpdatedAt = DateTime.Now;
        WriteTicket(account, session.DataKey);
        try
        {
            await _accounts.UpdateAsync(account, cancellationToken);
        }
        catch
        {
            DeleteTicket(account.AccountId);
            throw;
        }
    }

    public async Task<LocalSignInResult> SignInWithPinAsync(
        string username,
        string pin,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByUsernameAsync(username, cancellationToken);
        if (account is null || !account.IsEnabled || !account.QuickLoginEnabled)
        {
            return LocalSignInResult.Failure("当前账号不能使用快速登录，请改用主密码。");
        }

        if (!await LocalAccountDatabasePathRepair.TryRepairAsync(_accounts, account, cancellationToken))
        {
            return LocalSignInResult.Failure("账号数据路径异常，请改用主密码登录。");
        }

        if (!await _authService.VerifyPinAsync(account.AccountId, pin, cancellationToken))
        {
            return LocalSignInResult.Failure("PIN 不正确，请重试或改用主密码。");
        }

        return await CompleteQuickSignInAsync(account, "quick-signin-pin", cancellationToken);
    }

    public async Task<LocalSignInResult> SignInWithWindowsHelloAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetByUsernameAsync(username, cancellationToken);
        if (account is null || !account.IsEnabled || !account.QuickLoginEnabled)
        {
            return LocalSignInResult.Failure("当前账号不能使用快速登录，请改用主密码。");
        }

        if (!await LocalAccountDatabasePathRepair.TryRepairAsync(_accounts, account, cancellationToken))
        {
            return LocalSignInResult.Failure("账号数据路径异常，请改用主密码登录。");
        }

        return await CompleteQuickSignInAsync(account, "quick-signin-windows-hello", cancellationToken);
    }

    private async Task<LocalSignInResult> CompleteQuickSignInAsync(
        LocalAccount account,
        string detail,
        CancellationToken cancellationToken)
    {
        if (!TryReadTicket(account, out var dataKey))
        {
            return LocalSignInResult.Failure("本次开机的快速登录已失效，请使用主密码登录。");
        }

        try
        {
            var now = DateTime.Now;
            account.LastLoginAt = now;
            account.UpdatedAt = now;
            await _accounts.UpdateAsync(account, cancellationToken);
            WriteTicket(account, dataKey);

            var session = new LocalSessionContext
            {
                AccountId = account.AccountId,
                Username = account.Username,
                DisplayName = account.DisplayName,
                Role = account.Role,
                DatabasePath = account.DatabasePath,
                DataKey = dataKey,
                SignedInAt = now
            };
            _sessionContext.SetCurrent(session);
            CryptographicOperations.ZeroMemory(session.DataKey);
            if (_audit is not null)
            {
                try
                {
                    await _audit.RecordAsync(SecurityAuditEventKind.LoginSucceeded, account.Username, detail, cancellationToken);
                }
                catch
                {
                }
            }

            return LocalSignInResult.Success(session);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static void WriteTicket(LocalAccount account, byte[] dataKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("快速登录仅支持 Windows。");
        }

        var bootId = GetCurrentBootId();
        var stamp = ComputeCredentialStamp(account);
        byte[] plaintext;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(bootId.ToByteArray());
            writer.Write(account.AccountId);
            writer.Write(stamp.Length);
            writer.Write(stamp);
            writer.Write(dataKey.Length);
            writer.Write(dataKey);
            writer.Flush();
            plaintext = stream.ToArray();
        }

        byte[] protectedTicket = [];
        try
        {
            protectedTicket = ProtectedData.Protect(plaintext, ProtectionEntropy, DataProtectionScope.CurrentUser);
            var path = GetTicketPath(account.AccountId);
            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                LocalDataFileSecurity.EnsureFileIsNotLinked(path, "快速登录票据");
                LocalDataFileSecurity.EnsureFileIsNotLinked(temporaryPath, "快速登录临时票据");
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(protectedTicket);
                    stream.Flush(flushToDisk: true);
                }

                LocalDataFileSecurity.HardenFile(temporaryPath);
                File.Move(temporaryPath, path, overwrite: true);
                LocalDataFileSecurity.HardenFile(path);
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath) && !LocalDataFileSecurity.IsReparsePoint(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stamp);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedTicket);
        }
    }

    private static bool TryReadTicket(LocalAccount account, out byte[] dataKey)
    {
        dataKey = [];
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var path = GetTicketPath(account.AccountId);
        if (!File.Exists(path) || LocalDataFileSecurity.IsReparsePoint(path))
        {
            return false;
        }

        byte[] protectedTicket = [];
        byte[] plaintext = [];
        byte[] expectedStamp = [];
        byte[] storedStamp = [];
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxTicketLength)
            {
                DeleteTicket(account.AccountId);
                return false;
            }

            protectedTicket = File.ReadAllBytes(path);
            plaintext = ProtectedData.Unprotect(protectedTicket, ProtectionEntropy, DataProtectionScope.CurrentUser);
            using var stream = new MemoryStream(plaintext, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (reader.ReadInt32() != 1
                || new Guid(reader.ReadBytes(16)) != GetCurrentBootId()
                || !string.Equals(reader.ReadString(), account.AccountId, StringComparison.Ordinal))
            {
                DeleteTicket(account.AccountId);
                return false;
            }

            var stampLength = reader.ReadInt32();
            if (stampLength != 32)
            {
                return false;
            }

            storedStamp = reader.ReadBytes(stampLength);
            expectedStamp = ComputeCredentialStamp(account);
            if (!CryptographicOperations.FixedTimeEquals(storedStamp, expectedStamp))
            {
                DeleteTicket(account.AccountId);
                return false;
            }

            var keyLength = reader.ReadInt32();
            if (keyLength != DataKeyLength)
            {
                return false;
            }

            dataKey = reader.ReadBytes(keyLength);
            return dataKey.Length == DataKeyLength && stream.Position == stream.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or EndOfStreamException or InvalidOperationException)
        {
            DeleteTicket(account.AccountId);
            CryptographicOperations.ZeroMemory(dataKey);
            dataKey = [];
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedTicket);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(expectedStamp);
            CryptographicOperations.ZeroMemory(storedStamp);
        }
    }

    private static byte[] ComputeCredentialStamp(LocalAccount account)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(account.PasswordHash);
        hash.AppendData(account.PinHash);
        var accountIdBytes = Encoding.UTF8.GetBytes(account.AccountId);
        try
        {
            hash.AppendData(accountIdBytes);
            return hash.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(accountIdBytes);
        }
    }

    private static string GetTicketPath(string accountId)
    {
        var idBytes = Encoding.UTF8.GetBytes(accountId);
        var hash = SHA256.HashData(idBytes);
        try
        {
            return Path.Combine(DatabasePaths.GetIdentityDirectoryPath(), $"quick-login-{Convert.ToHexString(hash).ToLowerInvariant()}.dpapi");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(idBytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void DeleteTicket(string accountId)
    {
        try
        {
            var path = GetTicketPath(accountId);
            if (File.Exists(path) && !LocalDataFileSecurity.IsReparsePoint(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Guid GetCurrentBootId()
    {
        var status = NtQuerySystemInformation(
            SystemBootEnvironmentInformationClass,
            out var information,
            Marshal.SizeOf<SystemBootEnvironmentInformation>(),
            out _);
        if (status != 0 || information.BootIdentifier == Guid.Empty)
        {
            throw new InvalidOperationException("无法读取本次 Windows 启动标识。");
        }

        return information.BootIdentifier;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        out SystemBootEnvironmentInformation systemInformation,
        int systemInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemBootEnvironmentInformation
    {
        public Guid BootIdentifier;
        public int FirmwareType;
        public long BootFlags;
    }
}
