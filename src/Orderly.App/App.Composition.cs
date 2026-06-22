using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Orderly.App.ViewModels;
using Orderly.App.Views;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Repositories;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using Orderly.Infrastructure.Hotkeys;
using Orderly.Infrastructure.Services;
using Orderly.Infrastructure.Tray;

namespace Orderly.App;

public partial class App
{
    private async Task EnsureIdentityPreparedAsync()
    {
        if (_launcherConnectionFactory is not null)
        {
            return;
        }

        var launcherPath = DatabasePaths.GetLauncherDatabasePath();
        Func<byte[]?> launcherKeyProvider = static () => LauncherDatabaseKeyStore.GetOrCreateKeyCopy();
        SqliteDatabaseEncryptionMigrator.EnsureEncrypted(launcherPath, launcherKeyProvider, "启动器数据库");
        _launcherConnectionFactory = new LauncherConnectionFactory(launcherPath, launcherKeyProvider);
        var launcherInitializer = new LauncherDatabaseInitializer(_launcherConnectionFactory);
        await launcherInitializer.InitializeAsync();
    }

    private void EnsureAuthServicesPrepared()
    {
        if (_localAuthService is not null
            && _localAccountManagementService is not null
            && _sessionContextService is not null
            && _sessionLockService is not null
            && _fieldEncryptionService is not null)
        {
            return;
        }

        var launcherConnectionFactory = _launcherConnectionFactory ?? throw new InvalidOperationException("Launcher connection factory is not initialized.");
        var accountRepository = new LocalAccountRepository(launcherConnectionFactory);
        var credentialAttemptTracker = new CredentialAttemptTracker();
        var legacyMigrationService = new LegacyDatabaseMigrationService();

        _sessionContextService = new SessionContextService();
        _sessionLockService = new SessionLockService(_sessionContextService);
        _fieldEncryptionService = new FieldEncryptionService(_sessionContextService);
        // 启动期 fail-closed 断言：生产路径必须装配真实 AES-GCM 字段加密器，绝不允许空操作加密器静默注入。
        FieldEncryptionGuard.EnsureProductionGrade(_fieldEncryptionService, nameof(EnsureAuthServicesPrepared));

        // BC-6 / 任务 21.1：共享的安全审计服务实例，经 ISessionContextService 解析会话加密本账号库
        // （SQLCipher），使 RecordAsync 真正防篡改持久化、QueryAsync 真正读取。该单一实例同时注入
        // 认证 / 账户服务（登录成功·失败 / 账户锁定 / 凭证变更 / 成员创建·重置·停用·删除审计），
        // 并在 InitializeWorkspaceAsync 中复用注入 MeProfileViewModel（IsSecurityAuditAvailable=true）
        // 与凭证修改会话转移协调器 / Owner 紧急启用服务，确保全程同一审计链。
        _securityAuditService = new SecurityAuditService(_sessionContextService);
        _localAccountRepository = accountRepository;

        _localAuthService = new LocalAuthService(accountRepository, legacyMigrationService, _sessionContextService, credentialAttemptTracker, _securityAuditService);
        _localAccountManagementService = new LocalAccountManagementService(accountRepository, _sessionContextService, credentialAttemptTracker, _securityAuditService);
        _quickLoginService = new QuickLoginService(accountRepository, _sessionContextService, _localAuthService, _securityAuditService);
        _windowsHelloService = new Orderly.App.Services.WindowsHelloService();

        _sessionLockService.LockStateChanged += OnSessionLockStateChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void InitializeQaSessionContext(string databasePath, bool markSessionSignedIn = true)
    {
        var sessionContextService = _sessionContextService ?? throw new InvalidOperationException("Session context service is not initialized.");
        var sessionLockService = _sessionLockService ?? throw new InvalidOperationException("Session lock service is not initialized.");

        byte[]? qaDataKey = null;
        try
        {
            qaDataKey = LoadOrCreateQaSessionDataKey(databasePath);

            var session = new LocalSessionContext
            {
                AccountId = QaSessionAccountId,
                Username = QaSessionUsername,
                DisplayName = QaSessionDisplayName,
                Role = LocalAccountRole.Owner,
                DatabasePath = databasePath,
                DataKey = qaDataKey,
                SignedInAt = DateTime.Now
            };

            sessionContextService.SetCurrent(session);
            if (markSessionSignedIn)
            {
                sessionLockService.MarkSignedIn();
            }
        }
        finally
        {
            if (qaDataKey is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(qaDataKey);
            }
        }
    }

    private async Task PrepareQaSeedDatabaseAsync(string databasePath)
    {
        InitializeQaSessionContext(databasePath, markSessionSignedIn: false);
        try
        {
            await EnsureDatabasePreparedAsync(databasePath);

            var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
            await BackfillSensitiveFieldsAsync(connectionFactory);
        }
        finally
        {
            ClearQaSessionContextIfCurrent();
        }
    }

    private static byte[] LoadOrCreateQaSessionDataKey(string databasePath)
    {
        var keyPath = GetQaSessionDataKeyPath(databasePath);
        if (LocalDataFileSecurity.IsReparsePoint(keyPath))
        {
            throw new InvalidOperationException("QA session data key file cannot be a linked file.");
        }

        DeleteLegacyRawQaSessionDataKeyFile(databasePath);
        if (File.Exists(keyPath))
        {
            var existingKey = ReadProtectedQaSessionDataKey(keyPath);
            HardenQaSessionDataKeyFile(keyPath);
            return existingKey;
        }

        var key = RandomNumberGenerator.GetBytes(QaSessionDataKeyLength);
        WriteProtectedQaSessionDataKeyFile(keyPath, key);
        return key;
    }

    private static byte[] ReadProtectedQaSessionDataKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("当前系统不支持受保护的 QA session data key。");
        }

        using var stream = new FileStream(
            keyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: QaSessionDataKeyLength,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > MaxProtectedQaSessionDataKeyBytes)
        {
            throw new InvalidOperationException("QA session data key file length is invalid.");
        }

        var protectedKey = new byte[stream.Length];
        try
        {
            stream.ReadExactly(protectedKey);
            LocalDataFileSecurity.EnsureFileIsNotLinked(keyPath, "QA session data key file");
            var key = ProtectedData.Unprotect(
                protectedKey,
                GetQaSessionDataKeyProtectedEntropy(),
                DataProtectionScope.CurrentUser);
            if (key.Length == QaSessionDataKeyLength)
            {
                return key;
            }

            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("QA session data key length is invalid.");
        }
        catch
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            throw;
        }
    }

    private static void WriteProtectedQaSessionDataKeyFile(string keyPath, byte[] key)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("当前系统不支持受保护的 QA session data key。");
        }

        if (key.Length != QaSessionDataKeyLength)
        {
            throw new InvalidOperationException("QA session data key length is invalid.");
        }

        var directory = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("QA session data key directory is invalid.");
        }

        LocalDataFileSecurity.EnsureDirectoryExistsAndIsNotLinked(directory, "QA session data key directory");
        var protectedKey = ProtectedData.Protect(
            key,
            GetQaSessionDataKeyProtectedEntropy(),
            DataProtectionScope.CurrentUser);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(keyPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            LocalDataFileSecurity.EnsureFileIsNotLinked(tempPath, "QA session temporary data key file");
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: protectedKey.Length,
                FileOptions.WriteThrough))
            {
                stream.Write(protectedKey);
                stream.Flush(flushToDisk: true);
            }

            HardenQaSessionDataKeyFile(tempPath);
            LocalDataFileSecurity.EnsureFileIsNotLinked(keyPath, "QA session data key file");
            File.Move(tempPath, keyPath, overwrite: true);
            LocalDataFileSecurity.EnsureFileIsNotLinked(keyPath, "QA session data key file");
            HardenQaSessionDataKeyFile(keyPath);
        }
        catch
        {
            DeleteTemporaryQaSessionDataKeyFile(tempPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
        }
    }

    private static void DeleteTemporaryQaSessionDataKeyFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetQaSessionDataKeyPath(string databasePath)
    {
        return GetQaSessionDataKeyPath(databasePath, QaSessionProtectedKeyFileExtension);
    }

    private static string GetLegacyRawQaSessionDataKeyPath(string databasePath)
    {
        return GetQaSessionDataKeyPath(databasePath, QaSessionLegacyRawKeyFileExtension);
    }

    private static string GetQaSessionDataKeyPath(string databasePath, string extension)
    {
        var normalizedDatabasePath = Path.GetFullPath(databasePath).ToUpperInvariant();
        var pathBytes = Encoding.UTF8.GetBytes(normalizedDatabasePath);
        var hash = SHA256.HashData(pathBytes);
        try
        {
            return Path.Combine(
                DatabasePaths.GetIdentityDirectoryPath(),
                $"{QaSessionKeyFilePrefix}{Convert.ToHexString(hash).ToLowerInvariant()}{extension}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pathBytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static byte[] GetQaSessionDataKeyProtectedEntropy()
    {
        return Encoding.UTF8.GetBytes(QaSessionProtectedEntropyPurpose);
    }

    private static void DeleteLegacyRawQaSessionDataKeyFile(string databasePath)
    {
        var legacyPath = GetLegacyRawQaSessionDataKeyPath(databasePath);
        try
        {
            if (File.Exists(legacyPath) && !LocalDataFileSecurity.IsReparsePoint(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("旧版裸 QA session data key file 清理失败。", ex);
        }
    }

    private static void HardenQaSessionDataKeyFile(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or SystemException)
        {
            throw new InvalidOperationException("无法加固 QA session data key file。", ex);
        }

        LocalDataFileSecurity.HardenFile(path);
    }

    private async Task RunQaMaintenanceCommandAsync()
    {
        var databasePath = DatabasePaths.GetDefaultDatabasePath(allowQaOverride: true);
        InitializeQaSessionContext(databasePath, markSessionSignedIn: false);

        try
        {
            await EnsureDatabasePreparedAsync(databasePath);

            var connectionFactory = _connectionFactory ?? throw new InvalidOperationException("Database connection factory is not initialized.");
            var maintenanceService = new QaDataMaintenanceService(connectionFactory);

            object result = _qaMaintenanceCommand switch
            {
                QaDataMaintenanceService.QaDataMaintenanceCommand.Status => await maintenanceService.GetStatusAsync(),
                QaDataMaintenanceService.QaDataMaintenanceCommand.Clear => await maintenanceService.ClearAsync(),
                QaDataMaintenanceService.QaDataMaintenanceCommand.Reset => await maintenanceService.ResetAsync(),
                _ => throw new InvalidOperationException("Unsupported QA data maintenance command.")
            };

            if (_qaMaintenanceCommand == QaDataMaintenanceService.QaDataMaintenanceCommand.Reset)
            {
                await BackfillSensitiveFieldsAsync(connectionFactory);
            }

            Console.WriteLine(result);
        }
        finally
        {
            ClearQaSessionContextIfCurrent();
        }
    }

    private void ClearQaSessionContextIfCurrent()
    {
        if (_sessionContextService?.Current?.AccountId == QaSessionAccountId)
        {
            _sessionContextService.Clear();
        }
    }

    private async Task<string> EnsureDatabasePreparedAsync(string databasePath)
    {
        if (_connectionFactory is not null
            && !string.IsNullOrWhiteSpace(_preparedDatabasePath)
            && string.Equals(_preparedDatabasePath, databasePath, StringComparison.OrdinalIgnoreCase))
        {
            return _preparedDatabasePath;
        }

        _databasePath = databasePath;
        var sessionContextService = _sessionContextService ?? throw new InvalidOperationException("Session context service is not initialized.");
        Func<byte[]?> accountKeyProvider = () => sessionContextService.Current?.DataKey?.ToArray();
        SqliteDatabaseEncryptionMigrator.EnsureEncrypted(_databasePath, accountKeyProvider, "账号数据库");
        _connectionFactory = new SqliteConnectionFactory(_databasePath, accountKeyProvider);

        var initializer = new DatabaseInitializer(_connectionFactory);
        await initializer.InitializeAsync();
        Console.WriteLine("Database initialized");

        if (DemoDataSeeder.IsRequested(_startupArgs))
        {
            EnsurePrivilegedStartupModesAllowed();
            var demoDataSeeder = new DemoDataSeeder(_connectionFactory);
            await demoDataSeeder.SeedIfNeededAsync();
            Console.WriteLine("Demo data seeding checked");
        }

        if (QaDataSeeder.IsRequested(_startupArgs))
        {
            EnsurePrivilegedStartupModesAllowed();
            var qaDataSeeder = new QaDataSeeder(_connectionFactory);
            var qaSeedResult = await qaDataSeeder.SeedIfNeededAsync();
            Console.WriteLine($"QA data seeding checked: {qaSeedResult}");
        }

        _preparedDatabasePath = _databasePath;
        return _databasePath;
    }
}
