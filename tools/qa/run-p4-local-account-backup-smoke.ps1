param()

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

function Import-OrderlyAssemblies {
    $binRoot = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows')
    $nativeRuntimePath = Join-Path $binRoot 'runtimes\\win-x64\\native'
    if (Test-Path -LiteralPath $nativeRuntimePath) {
        $env:PATH = "$nativeRuntimePath;$binRoot;$env:PATH"
        if (-not ('QaNativeLoader' -as [type])) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class QaNativeLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);
}
"@
        }

        [QaNativeLoader]::SetDllDirectory($nativeRuntimePath) | Out-Null
        $nativeLibrary = Join-Path $nativeRuntimePath 'e_sqlite3.dll'
        if ([QaNativeLoader]::LoadLibrary($nativeLibrary) -eq [IntPtr]::Zero) {
            throw "Failed to preload native SQLite library: $nativeLibrary"
        }
    }

    $assemblyNames = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll',
        'Orderly.Core.dll',
        'Orderly.Data.dll',
        'Orderly.Infrastructure.dll'
    )

    foreach ($assemblyName in $assemblyNames) {
        $assemblyPath = Join-Path $binRoot $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath)) {
            throw "Missing QA dependency assembly: $assemblyPath"
        }

        [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
    }

    [SQLitePCL.Batteries_V2]::Init()
}

function Initialize-BusinessDatabase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $initializer = [Orderly.Data.Sqlite.DatabaseInitializer]::new($connectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
    return $connectionFactory
}

function Initialize-LauncherDatabase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.LauncherConnectionFactory]::new($DatabasePath)
    $initializer = [Orderly.Data.Sqlite.LauncherDatabaseInitializer]::new($connectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
    return $connectionFactory
}

function Invoke-SqlNonQuery {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        [string]$CommandText
    )

    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$DatabasePath;Foreign Keys=True")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $CommandText
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        [string]$CommandText
    )

    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$DatabasePath;Foreign Keys=True")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $CommandText
        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Clear-BusinessTables {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    Invoke-SqlNonQuery -DatabasePath $DatabasePath -CommandText @"
PRAGMA foreign_keys = OFF;
DELETE FROM SyncRecords;
DELETE FROM AiSuggestions;
DELETE FROM OcrResults;
DELETE FROM ConversationMessages;
DELETE FROM ActivityLogs;
DELETE FROM PriceAdjustments;
DELETE FROM ReplyTemplates;
DELETE FROM CustomerNotes;
DELETE FROM FollowUps;
DELETE FROM Orders;
DELETE FROM Deals;
DELETE FROM Customers;
PRAGMA foreign_keys = ON;
"@
}

function Get-TableCounts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        [string[]]$TableNames
    )

    $counts = @{}
    foreach ($tableName in $TableNames) {
        $predicate = if ($tableName -eq 'ReplyTemplates') { '1=1' } else { 'DeletedAt IS NULL' }
        $counts[$tableName] = [int](Invoke-SqlScalar -DatabasePath $DatabasePath -CommandText "SELECT COUNT(1) FROM $tableName WHERE $predicate;")
    }

    return $counts
}

function New-SessionContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AccountId,
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [byte[]]$DataKey = $(,0 * 0)
    )

    $sessionContextService = [Orderly.Data.Services.SessionContextService]::new()
    $sessionContext = [Orderly.Core.Models.LocalSessionContext]@{
        AccountId = $AccountId
        Username = 'owner'
        DisplayName = 'Owner'
        Role = [Orderly.Core.Models.LocalAccountRole]::Owner
        DatabasePath = $DatabasePath
        DataKey = $DataKey
        SignedInAt = [DateTime]::Now
    }
    $sessionContextService.SetCurrent($sessionContext)
    return $sessionContextService
}

function New-BackupContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        $LauncherConnectionFactory,
        [Parameter(Mandatory = $true)]
        $SessionContextService
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $fieldEncryptionService = [Orderly.Data.Services.FieldEncryptionService]::new($SessionContextService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($connectionFactory, $syncService, $syncRecordRepository, $activityRepository, $LauncherConnectionFactory, $SessionContextService)

    return [pscustomobject]@{
        ConnectionFactory = $connectionFactory
        ActivityRepository = $activityRepository
        SyncRecordRepository = $syncRecordRepository
        BackupService = $backupService
    }
}

function New-LocalAccount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AccountId,
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $now = [DateTime]::Now
    return [Orderly.Core.Models.LocalAccount]@{
        AccountId = $AccountId
        Username = 'owner'
        DisplayName = 'Owner'
        PasswordHash = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        PasswordSalt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        PasswordIterations = 200000
        PinHash = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        PinSalt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        PinIterations = 200000
        RecoveryKeyHash = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        RecoveryKeySalt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        RecoveryKeyIterations = 200000
        RecoveryEncryptedDataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        RecoveryDataKeyNonce = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
        RecoveryDataKeyTag = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        EncryptedDataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        DataKeyNonce = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
        DataKeyTag = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
        DatabasePath = $DatabasePath
        Role = [Orderly.Core.Models.LocalAccountRole]::Owner
        IsEnabled = $true
        CreatedAt = $now
        UpdatedAt = $now
        LastLoginAt = $now
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P4 launcher + account backup smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

Import-OrderlyAssemblies
$runDirectory = New-QaSmokeRunDirectory

$sourceLauncherPath = Join-Path $runDirectory.Path 'source-launcher.db'
$sourceAccountPath = Join-Path $runDirectory.Path 'source-account.db'
$targetLauncherPath = Join-Path $runDirectory.Path 'target-launcher.db'
$targetAccountPath = Join-Path $runDirectory.Path 'target-account.db'
$backupPath = Join-Path $runDirectory.Path 'local-account-backup.json'
$accountId = '11111111111111111111111111111111'
$sourceDataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)

Write-Step "Step 1/7: initialize isolated source launcher/account databases"
$sourceLauncherFactory = Initialize-LauncherDatabase -DatabasePath $sourceLauncherPath
[void](Initialize-BusinessDatabase -DatabasePath $sourceAccountPath)
$sourceAccountRepository = [Orderly.Data.Repositories.LocalAccountRepository]::new($sourceLauncherFactory)
$sourceAccount = New-LocalAccount -AccountId $accountId -DatabasePath $sourceAccountPath
[void]$sourceAccountRepository.CreateAsync($sourceAccount).GetAwaiter().GetResult()
$sourceSessionContext = New-SessionContext -AccountId $accountId -DatabasePath $sourceAccountPath -DataKey $sourceDataKey
$sourceBackupContext = New-BackupContext -DatabasePath $sourceAccountPath -LauncherConnectionFactory $sourceLauncherFactory -SessionContextService $sourceSessionContext

Write-Step "Step 2/7: export backup and verify launcher snapshot is included"
$exportResult = $sourceBackupContext.BackupService.ExportAsync($backupPath, 'p4-local-account-smoke', $false).GetAwaiter().GetResult()
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Backup file was not created: $backupPath"
}

Write-Step "Step 3/7: validate backup"
$validationResult = $sourceBackupContext.BackupService.ValidateAsync($backupPath, 'p4-local-account-smoke', $false).GetAwaiter().GetResult()
if (-not $validationResult.IsValid) {
    throw "Backup validation failed: $($validationResult.Errors -join '; ')"
}

if (-not $validationResult.Manifest.Tables.ContainsKey('LocalAccountsSnapshot')) {
    throw 'Backup manifest is missing LocalAccountsSnapshot.'
}

if (-not $validationResult.Manifest.Counts.ContainsKey('ReplyTemplates')) {
    throw 'Backup manifest is missing ReplyTemplates count.'
}

$launcherSnapshot = $validationResult.Manifest.Tables['LocalAccountsSnapshot']
$snapshotRows = @($launcherSnapshot.EnumerateArray())
if ($snapshotRows.Count -ne 1) {
    throw "Expected exactly one LocalAccountsSnapshot row, got: $($snapshotRows.Count)"
}

if ($snapshotRows[0].GetProperty('accountId').GetString() -ne $accountId) {
    throw 'LocalAccountsSnapshot accountId mismatch.'
}

$expectedCounts = Get-TableCounts -DatabasePath $sourceAccountPath -TableNames @(
    'Customers','Deals','Orders','FollowUps','CustomerNotes','ReplyTemplates',
    'PriceAdjustments','ActivityLogs','ConversationMessages','AiSuggestions','OcrResults'
)

Write-Step "Step 4/7: initialize isolated target launcher/account databases"
$targetLauncherFactory = Initialize-LauncherDatabase -DatabasePath $targetLauncherPath
[void](Initialize-BusinessDatabase -DatabasePath $targetAccountPath)
Clear-BusinessTables -DatabasePath $targetAccountPath
$targetSessionContext = New-SessionContext -AccountId $accountId -DatabasePath $targetAccountPath -DataKey $sourceDataKey
$targetBackupContext = New-BackupContext -DatabasePath $targetAccountPath -LauncherConnectionFactory $targetLauncherFactory -SessionContextService $targetSessionContext

Write-Step "Step 5/7: preview restore and require empty target"
$preview = $targetBackupContext.BackupService.PreviewRestoreAsync($backupPath, 'p4-local-account-smoke').GetAwaiter().GetResult()
if ($preview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::EmptyDatabase) {
    throw "Expected empty target account database, got: $($preview.TargetState)"
}

Write-Step "Step 6/7: restore and verify business + launcher data"
$restoreResult = $targetBackupContext.BackupService.RestoreBackupAsync($backupPath, $false, 'p4-local-account-smoke').GetAwaiter().GetResult()
if ($restoreResult.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw "Restore did not succeed: $($restoreResult.SyncStatus)"
}

$actualCounts = Get-TableCounts -DatabasePath $targetAccountPath -TableNames @(
    'Customers','Deals','Orders','FollowUps','CustomerNotes','ReplyTemplates',
    'PriceAdjustments','ActivityLogs','ConversationMessages','AiSuggestions','OcrResults'
)

foreach ($tableName in @('Customers','Deals','Orders','FollowUps','CustomerNotes','ReplyTemplates','PriceAdjustments','ConversationMessages','AiSuggestions','OcrResults')) {
    if ($actualCounts[$tableName] -ne $expectedCounts[$tableName]) {
        throw "$tableName count mismatch after restore. Expected=$($expectedCounts[$tableName]), Actual=$($actualCounts[$tableName])"
    }
}

$targetLauncherConnection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$targetLauncherPath;Foreign Keys=True")
try {
    $targetLauncherConnection.Open()
    $command = $targetLauncherConnection.CreateCommand()
    $command.CommandText = "SELECT AccountId, DatabasePath, Role, IsEnabled FROM LocalAccounts LIMIT 1;"
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'Target launcher database does not contain restored LocalAccounts row.'
    }

    if ($reader.GetString(0) -ne $accountId) {
        throw 'Restored launcher account id mismatch.'
    }

    if ($reader.GetString(1) -ne $targetAccountPath) {
        throw 'Restored launcher database path was not rewritten to the target business database path.'
    }
}
finally {
    $targetLauncherConnection.Dispose()
}

Write-Step "Step 7/7: verify restore audit trail exists"
$restoreSync = $targetBackupContext.SyncRecordRepository.GetLatestByEntityTypeAsync('local-restore').GetAwaiter().GetResult()
if ($null -eq $restoreSync -or $restoreSync.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw 'Missing successful local-restore SyncRecord for launcher/account smoke.'
}

Write-Step "P4 launcher + account backup smoke completed"
