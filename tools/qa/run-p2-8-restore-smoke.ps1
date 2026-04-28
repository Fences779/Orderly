param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    & $Path

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

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

function New-BackupServiceContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory)
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($connectionFactory, $syncService, $syncRecordRepository, $activityRepository)

    return [pscustomobject]@{
        DatabasePath          = $DatabasePath
        ConnectionFactory     = $connectionFactory
        ActivityRepository    = $activityRepository
        SyncRecordRepository  = $syncRecordRepository
        BackupService         = $backupService
    }
}

function Initialize-Database {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $initializer = [Orderly.Data.Sqlite.DatabaseInitializer]::new($connectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
    return $connectionFactory
}

function Seed-QaOnlyData {
    param(
        [Parameter(Mandatory = $true)]
        $ConnectionFactory
    )

    $qaSeeder = [Orderly.Data.Services.QaDataSeeder]::new($ConnectionFactory)
    [void]$qaSeeder.SeedIfNeededAsync().GetAwaiter().GetResult()
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
        $counts[$tableName] = [int](Invoke-SqlScalar -DatabasePath $DatabasePath -CommandText "SELECT COUNT(1) FROM $tableName WHERE DeletedAt IS NULL;")
    }

    return $counts
}

function Get-StringValue {
    param(
        [Parameter(Mandatory = $true)]
        $Value
    )

    if ($null -eq $Value) {
        return ''
    }

    return [string]$Value
}

function Get-QaStatusMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $map = @{}
    foreach ($line in ($Output -split "`r?`n")) {
        if ($line -match '^QA ([A-Za-z]+) count:\s*(\d+)$') {
            $map[$matches[1]] = [int]$matches[2]
        }
    }

    return $map
}

function Get-QaStatusForDatabase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $maintenanceService = [Orderly.Data.Services.QaDataMaintenanceService]::new($connectionFactory)
    return $maintenanceService.GetStatusAsync().GetAwaiter().GetResult()
}

function Assert-CountsMatchForRestore {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$ActualCounts,
        [Parameter(Mandatory = $true)]
        $ExpectedCounts
    )

    $strictTables = @(
        'Customers',
        'Deals',
        'Orders',
        'FollowUps',
        'CustomerNotes',
        'PriceAdjustments',
        'ConversationMessages',
        'AiSuggestions',
        'OcrResults'
    )

    foreach ($tableName in $strictTables) {
        $expected = [int]$ExpectedCounts[$tableName]
        $actual = [int]$ActualCounts[$tableName]
        if ($actual -ne $expected) {
            throw "$tableName count mismatch after restore. Expected=$expected, Actual=$actual"
        }
    }

    $expectedActivityLogs = [int]$ExpectedCounts['ActivityLogs'] + 2
    if ([int]$ActualCounts['ActivityLogs'] -ne $expectedActivityLogs) {
        throw "ActivityLogs count mismatch after restore. Expected=$expectedActivityLogs, Actual=$($ActualCounts['ActivityLogs'])"
    }
}

function Assert-RestoreAudit {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        [string]$CreatedBy,
        [Parameter(Mandatory = $true)]
        [string]$BackupPath,
        [Parameter(Mandatory = $true)]
        [Orderly.Core.Models.SyncStatus]$ExpectedStatus
    )

    $latestSync = $Context.SyncRecordRepository.GetLatestByEntityTypeAsync('local-restore').GetAwaiter().GetResult()
    if ($null -eq $latestSync) {
        throw 'Missing latest local-restore SyncRecord.'
    }

    if ($latestSync.SyncStatus -ne $ExpectedStatus) {
        throw "Unexpected local-restore SyncStatus: $($latestSync.SyncStatus)"
    }

    $metadata = $latestSync.MetadataJson | ConvertFrom-Json
    if ($metadata.createdBy -ne $CreatedBy) {
        throw "local-restore SyncRecord missing createdBy=$CreatedBy."
    }

    if ($metadata.backupPath -ne $BackupPath) {
        throw 'local-restore SyncRecord backupPath mismatch.'
    }

    $allActivities = $Context.ActivityRepository.ListAsync().GetAwaiter().GetResult()
    $restoreActivities = @($allActivities | Where-Object { $_.MetadataJson.Contains('"createdBy":"' + $CreatedBy + '"') })

    if ($ExpectedStatus -eq [Orderly.Core.Models.SyncStatus]::Synced) {
        $started = @($restoreActivities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::BackupRestoreStarted })
        $succeeded = @($restoreActivities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::BackupRestoreSucceeded })
        if ($started.Count -lt 1) {
            throw 'Missing BackupRestoreStarted ActivityLog entry.'
        }

        if ($succeeded.Count -lt 1) {
            throw 'Missing BackupRestoreSucceeded ActivityLog entry.'
        }
    } else {
        $failed = @($restoreActivities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::BackupRestoreFailed })
        if ($failed.Count -lt 1) {
            throw 'Missing BackupRestoreFailed ActivityLog entry.'
        }
    }
}

function New-ChecksumTamperedBackup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $backupJson = Get-Content -LiteralPath $SourcePath -Raw -Encoding utf8 | ConvertFrom-Json
    $backupJson.checksum = 'checksum-tampered-by-p2-8'
    $backupJson | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $TargetPath -Encoding utf8
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.8 controlled restore smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/10: reset QA baseline"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/10: skip QA reset"
}

Write-Step "Step 2/10: capture baseline QA status"
$baselineStatusResult = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$baselineStatus = Get-QaStatusMap -Output $baselineStatusResult.StdOut

Write-Step "Step 3/10: export P2.7-format backup and validate it"
Import-OrderlyAssemblies
$sourceDbPath = Get-DefaultDatabasePath
$sourceContext = New-BackupServiceContext -DatabasePath $sourceDbPath
$runDirectory = New-QaSmokeRunDirectory
$backupPath = Join-Path $runDirectory.Path ("orderly-backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$exportResult = $sourceContext.BackupService.ExportAsync($backupPath, 'p2.8', $true).GetAwaiter().GetResult()
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Backup file was not created: $backupPath"
}

$validationResult = $sourceContext.BackupService.ValidateAsync($backupPath, 'p2.8', $true).GetAwaiter().GetResult()
if (-not $validationResult.IsValid) {
    throw "Expected backup validation success, but got: $($validationResult.Errors -join '; ')"
}

$expectedCounts = $exportResult.Manifest.Counts

Write-Step "Step 4/10: restore into a prepared empty database"
$emptyTargetPath = Join-Path $runDirectory.Path 'restore-empty-target.db'
[void](Initialize-Database -DatabasePath $emptyTargetPath)
Clear-BusinessTables -DatabasePath $emptyTargetPath
$emptyTargetContext = New-BackupServiceContext -DatabasePath $emptyTargetPath
$emptyPreview = $emptyTargetContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.8').GetAwaiter().GetResult()
if ($emptyPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::EmptyDatabase) {
    throw "Expected empty target database, got: $($emptyPreview.TargetState)"
}

$emptyRestoreResult = $emptyTargetContext.BackupService.RestoreBackupAsync($backupPath, $false, 'p2.8').GetAwaiter().GetResult()
if ($emptyRestoreResult.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw "Empty target restore did not succeed: $($emptyRestoreResult.SyncStatus)"
}

$emptyCounts = Get-TableCounts -DatabasePath $emptyTargetPath -TableNames @(
    'Customers','Deals','Orders','FollowUps','CustomerNotes','PriceAdjustments',
    'ActivityLogs','ConversationMessages','AiSuggestions','OcrResults'
)
Assert-CountsMatchForRestore -ActualCounts $emptyCounts -ExpectedCounts $expectedCounts
Assert-RestoreAudit -Context $emptyTargetContext -CreatedBy 'p2.8' -BackupPath $backupPath -ExpectedStatus ([Orderly.Core.Models.SyncStatus]::Synced)

Write-Step "Step 5/10: restore into QA/test database after controlled clear"
$qaTargetPath = Join-Path $runDirectory.Path 'restore-qa-target.db'
$qaTargetFactory = Initialize-Database -DatabasePath $qaTargetPath
Clear-BusinessTables -DatabasePath $qaTargetPath
Seed-QaOnlyData -ConnectionFactory $qaTargetFactory
$qaBaselineStatus = Get-QaStatusForDatabase -DatabasePath $qaTargetPath
$qaTargetContext = New-BackupServiceContext -DatabasePath $qaTargetPath
$qaPreview = $qaTargetContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.8').GetAwaiter().GetResult()
if ($qaPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::QaDatabase) {
    throw "Expected QA target database, got: $($qaPreview.TargetState)"
}

if (-not $qaPreview.RequiresQaDataClear) {
    throw 'QA target restore preview did not require QA data clear.'
}

$qaRestoreResult = $qaTargetContext.BackupService.RestoreBackupAsync($backupPath, $true, 'p2.8').GetAwaiter().GetResult()
if ($qaRestoreResult.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw "QA target restore did not succeed: $($qaRestoreResult.SyncStatus)"
}

$qaCounts = Get-TableCounts -DatabasePath $qaTargetPath -TableNames @(
    'Customers','Deals','Orders','FollowUps','CustomerNotes','PriceAdjustments',
    'ActivityLogs','ConversationMessages','AiSuggestions','OcrResults'
)
Assert-CountsMatchForRestore -ActualCounts $qaCounts -ExpectedCounts $expectedCounts
Assert-RestoreAudit -Context $qaTargetContext -CreatedBy 'p2.8' -BackupPath $backupPath -ExpectedStatus ([Orderly.Core.Models.SyncStatus]::Synced)

Write-Step "Step 6/10: reject restore into non-empty production-like database"
$productionTargetPath = Join-Path $runDirectory.Path 'restore-production-target.db'
[void](Initialize-Database -DatabasePath $productionTargetPath)
$productionContext = New-BackupServiceContext -DatabasePath $productionTargetPath
$productionPreview = $productionContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.8').GetAwaiter().GetResult()
if ($productionPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::NonEmptyProductionDatabase) {
    throw "Expected production target database, got: $($productionPreview.TargetState)"
}

$productionRejected = $false
try {
    [void]$productionContext.BackupService.RestoreBackupAsync($backupPath, $true, 'p2.8').GetAwaiter().GetResult()
}
catch {
    $productionRejected = $_.Exception.Message.Contains('禁止覆盖恢复')
}

if (-not $productionRejected) {
    throw 'Expected non-empty production restore to be rejected.'
}

Assert-RestoreAudit -Context $productionContext -CreatedBy 'p2.8' -BackupPath $backupPath -ExpectedStatus ([Orderly.Core.Models.SyncStatus]::Failed)

Write-Step "Step 7/10: invalid JSON restore must fail"
$invalidJsonPath = Join-Path $runDirectory.Path 'invalid-backup.json'
Set-Content -LiteralPath $invalidJsonPath -Value '{ invalid json' -Encoding utf8
$invalidTargetPath = Join-Path $runDirectory.Path 'restore-invalid-target.db'
[void](Initialize-Database -DatabasePath $invalidTargetPath)
Clear-BusinessTables -DatabasePath $invalidTargetPath
$invalidContext = New-BackupServiceContext -DatabasePath $invalidTargetPath
$invalidRejected = $false
try {
    [void]$invalidContext.BackupService.RestoreBackupAsync($invalidJsonPath, $false, 'p2.8').GetAwaiter().GetResult()
}
catch {
    $invalidRejected = $_.Exception.Message.Contains('JSON 解析失败')
}

if (-not $invalidRejected) {
    throw 'Expected invalid JSON restore to fail.'
}

Assert-RestoreAudit -Context $invalidContext -CreatedBy 'p2.8' -BackupPath $invalidJsonPath -ExpectedStatus ([Orderly.Core.Models.SyncStatus]::Failed)

Write-Step "Step 8/10: checksum mismatch restore must fail"
$checksumTamperedPath = Join-Path $runDirectory.Path 'checksum-tampered-backup.json'
New-ChecksumTamperedBackup -SourcePath $backupPath -TargetPath $checksumTamperedPath
$checksumTargetPath = Join-Path $runDirectory.Path 'restore-checksum-target.db'
[void](Initialize-Database -DatabasePath $checksumTargetPath)
Clear-BusinessTables -DatabasePath $checksumTargetPath
$checksumContext = New-BackupServiceContext -DatabasePath $checksumTargetPath
$checksumRejected = $false
try {
    [void]$checksumContext.BackupService.RestoreBackupAsync($checksumTamperedPath, $false, 'p2.8').GetAwaiter().GetResult()
}
catch {
    $checksumRejected = $_.Exception.Message.Contains('checksum 校验失败')
}

if (-not $checksumRejected) {
    throw 'Expected checksum-mismatched restore to fail.'
}

Assert-RestoreAudit -Context $checksumContext -CreatedBy 'p2.8' -BackupPath $checksumTamperedPath -ExpectedStatus ([Orderly.Core.Models.SyncStatus]::Failed)

Write-Step "Step 9/10: reset QA-only target and verify baseline is stable"
$qaMaintenanceService = [Orderly.Data.Services.QaDataMaintenanceService]::new($qaTargetFactory)
[void]$qaMaintenanceService.ResetAsync().GetAwaiter().GetResult()
$qaFinalStatus = Get-QaStatusForDatabase -DatabasePath $qaTargetPath

foreach ($propertyName in @(
    'CustomersCount','OrdersCount','DealsCount','FollowUpsCount','NotesCount',
    'PriceAdjustmentsCount','ActivityLogsCount','ConversationMessagesCount',
    'AiSuggestionsCount','OcrResultsCount','SyncRecordsCount'
)) {
    if ($qaFinalStatus.$propertyName -ne $qaBaselineStatus.$propertyName) {
        throw "QA-only target baseline mismatch after reset for $propertyName. Baseline=$($qaBaselineStatus.$propertyName), Final=$($qaFinalStatus.$propertyName)"
    }
}

Write-Step "Step 10/10: reset default QA data and verify source baseline is stable"
Invoke-QaScript -Path $resetScript
$finalStatusResult = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$finalStatus = Get-QaStatusMap -Output $finalStatusResult.StdOut

foreach ($key in $baselineStatus.Keys) {
    if (-not $finalStatus.ContainsKey($key)) {
        throw "Final QA status missing key: $key"
    }

    if ($finalStatus[$key] -ne $baselineStatus[$key]) {
        throw "QA baseline mismatch after reset for $key. Baseline=$($baselineStatus[$key]), Final=$($finalStatus[$key])"
    }
}

Write-Step "Controlled restore smoke completed"
