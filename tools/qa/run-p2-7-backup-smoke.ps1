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

function Get-CountFromStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $match = [regex]::Match($Output, [regex]::Escape($Label) + '\s*(\d+)')
    if (-not $match.Success) {
        throw "Unable to parse count from status output. Label: $Label"
    }

    return [int]$match.Groups[1].Value
}

function Import-OrderlyAssemblies {
    Import-OrderlyAssembliesForQa
}

function New-BackupServiceContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory)
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($connectionFactory, $syncService, $syncRecordRepository, $activityRepository)

    return [pscustomobject]@{
        SessionContextService = $fieldContext.SessionContextService
        ActivityRepository = $activityRepository
        SyncRecordRepository = $syncRecordRepository
        BackupService = $backupService
    }
}

function Assert-CountsContain {
    param(
        [Parameter(Mandatory = $true)]
        $Counts,
        [Parameter(Mandatory = $true)]
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        if (-not $Counts.PSObject.Properties.Name.Contains($key)) {
            throw "Backup counts missing key: $key"
        }
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.7 local backup smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/7: reset QA data"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/7: skip QA data reset"
}

Write-Step "Step 2/7: baseline QA status"
$baselineStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$baselineActivityCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA ActivityLogs count:'
$baselineSyncCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA SyncRecords count:'
Write-Step "Baseline ActivityLogs count: $baselineActivityCount"
Write-Step "Baseline SyncRecords count: $baselineSyncCount"

Write-Step "Step 3/7: export backup and inspect JSON"
Import-OrderlyAssemblies
$serviceContext = New-BackupServiceContext
$runDirectory = New-QaSmokeRunDirectory
$backupPath = Join-Path $runDirectory.Path ("orderly-backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$exportResult = $serviceContext.BackupService.ExportAsync($backupPath, 'p2.7', $true).GetAwaiter().GetResult()
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Backup file was not created: $backupPath"
}

$backupJson = Get-Content -LiteralPath $backupPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($backupJson.schemaVersion -ne 1) {
    throw "Unexpected schemaVersion: $($backupJson.schemaVersion)"
}

if ([string]::IsNullOrWhiteSpace([string]$backupJson.exportedAt)) {
    throw 'Backup JSON missing exportedAt.'
}

if ($null -eq $backupJson.counts) {
    throw 'Backup JSON missing counts.'
}

Assert-CountsContain -Counts $backupJson.counts -Keys @(
    'Customers',
    'Orders',
    'Deals',
    'ReplyTemplates',
    'ActivityLogs',
    'ConversationMessages',
    'AiSuggestions',
    'OcrResults'
)

if ([string]::IsNullOrWhiteSpace([string]$backupJson.checksum)) {
    throw 'Backup JSON missing checksum.'
}

Write-Step "Exported backup: $backupPath"

Write-Step "Step 4/7: validate the exported backup"
$validResult = $serviceContext.BackupService.ValidateAsync($backupPath, 'p2.7', $true).GetAwaiter().GetResult()
if (-not $validResult.IsValid) {
    throw "Expected backup validation success, but got: $($validResult.Errors -join '; ')"
}

Write-Step "Step 5/7: validate a tampered backup and expect failure"
$tamperedPath = Join-Path $runDirectory.Path 'orderly-backup-tampered.json'
Set-Content -LiteralPath $tamperedPath -Value '{ invalid json' -Encoding utf8
$invalidResult = $serviceContext.BackupService.ValidateAsync($tamperedPath, 'p2.7', $true).GetAwaiter().GetResult()
if ($invalidResult.IsValid) {
    throw 'Expected tampered backup validation to fail.'
}

if ($invalidResult.Errors.Count -lt 1) {
    throw 'Tampered backup validation did not return any errors.'
}

Write-Step "Step 6/7: verify SyncRecord and ActivityLog entries"
$latestSync = $serviceContext.SyncRecordRepository.GetLatestByEntityTypeAsync('local-backup').GetAwaiter().GetResult()
if ($null -eq $latestSync) {
    throw 'Missing latest local-backup SyncRecord.'
}

if ($latestSync.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw "Latest local-backup SyncRecord is not success: $($latestSync.SyncStatus)"
}

$syncMetadata = $latestSync.MetadataJson | ConvertFrom-Json
if ($syncMetadata.createdBy -ne 'p2.7') {
    throw 'SyncRecord metadata missing createdBy=p2.7.'
}

if ($syncMetadata.backupPath -ne $backupPath) {
    throw 'SyncRecord metadata backupPath mismatch.'
}

if ([string]::IsNullOrWhiteSpace([string]$syncMetadata.checksum)) {
    throw 'SyncRecord metadata missing checksum.'
}

Write-Step "Step 7/7: reset QA data again and verify baseline is restored"
Invoke-QaScript -Path $resetScript
$finalStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$finalActivityCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA ActivityLogs count:'
$finalSyncCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA SyncRecords count:'

if ($finalActivityCount -ne $baselineActivityCount) {
    throw "ActivityLogs count did not restore after reset. Baseline=$baselineActivityCount, Final=$finalActivityCount"
}

if ($finalSyncCount -ne $baselineSyncCount) {
    throw "SyncRecords count did not restore after reset. Baseline=$baselineSyncCount, Final=$finalSyncCount"
}

Write-Step "Final ActivityLogs count restored to: $finalActivityCount"
Write-Step "Final SyncRecords count restored to: $finalSyncCount"
Write-Step "P2.7 local backup smoke completed"
