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
        _launcherConnectionFactory = new LauncherConnectionFactory(launcherPath);
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
        var legacyMigrationService = new LegacyDatabaseMigrationService();

        _sessionContextService = new SessionContextService();
        _sessionLockService = new SessionLockService();
        _fieldEncryptionService = new FieldEncryptionService(_sessionContextService);
        _localAuthService = new LocalAuthService(accountRepository, legacyMigrationService, _sessionContextService);
        _localAccountManagementService = new LocalAccountManagementService(accountRepository, _sessionContextService);

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
        if (File.Exists(keyPath))
        {
            var existingKey = File.ReadAllBytes(keyPath);
            if (existingKey.Length == QaSessionDataKeyLength)
            {
                return existingKey;
            }

            CryptographicOperations.ZeroMemory(existingKey);
        }

        var key = RandomNumberGenerator.GetBytes(QaSessionDataKeyLength);
        File.WriteAllBytes(keyPath, key);
        TryHideFile(keyPath);
        return key;
    }

    private static string GetQaSessionDataKeyPath(string databasePath)
    {
        var normalizedDatabasePath = Path.GetFullPath(databasePath).ToUpperInvariant();
        var pathBytes = Encoding.UTF8.GetBytes(normalizedDatabasePath);
        var hash = SHA256.HashData(pathBytes);
        try
        {
            return Path.Combine(
                DatabasePaths.GetIdentityDirectoryPath(),
                $"{QaSessionKeyFilePrefix}{Convert.ToHexString(hash).ToLowerInvariant()}.key");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pathBytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void TryHideFile(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
        _connectionFactory = new SqliteConnectionFactory(_databasePath);

        var initializer = new DatabaseInitializer(_connectionFactory);
        await initializer.InitializeAsync();
        Console.WriteLine("Database initialized");

        if (DemoDataSeeder.IsRequested(_startupArgs))
        {
            var demoDataSeeder = new DemoDataSeeder(_connectionFactory);
            await demoDataSeeder.SeedIfNeededAsync();
            Console.WriteLine("Demo data seeding checked");
        }

        if (QaDataSeeder.IsRequested(_startupArgs))
        {
            var qaDataSeeder = new QaDataSeeder(_connectionFactory);
            var qaSeedResult = await qaDataSeeder.SeedIfNeededAsync();
            Console.WriteLine($"QA data seeding checked: {qaSeedResult}");
        }

        _preparedDatabasePath = _databasePath;
        return _databasePath;
    }
}
