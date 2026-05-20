param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'
$p28Script = Join-Path $PSScriptRoot 'run-p2-8-restore-smoke.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$Arguments = @()
    )

    & $Path @Arguments

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

function Import-OrderlyAssemblies {
    Import-OrderlyAssembliesForQa -IncludeAppAssembly
}

function New-BackupServiceContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $DatabasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory)
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($connectionFactory, $syncService, $syncRecordRepository, $activityRepository)

    return [pscustomobject]@{
        DatabasePath         = $DatabasePath
        ConnectionFactory    = $connectionFactory
        SessionContextService = $fieldContext.SessionContextService
        ActivityRepository   = $activityRepository
        SyncRecordRepository = $syncRecordRepository
        BackupService        = $backupService
    }
}

function New-MainViewModel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)

    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $DatabasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory, $fieldEncryptionService)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory, $fieldEncryptionService)
    $noteRepository = [Orderly.Data.Repositories.CustomerNoteRepository]::new($connectionFactory, $fieldEncryptionService)
    $conversationMessageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory, $fieldEncryptionService)
    $ocrResultRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory, $fieldEncryptionService)
    $aiSuggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityLogRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory, $fieldEncryptionService)
    $replyTemplateRepository = [Orderly.Data.Repositories.ReplyTemplateRepository]::new($connectionFactory, $fieldEncryptionService)
    $settingRepository = [Orderly.Data.Repositories.AppSettingRepository]::new($connectionFactory)
    $clipboardService = [Orderly.Infrastructure.Services.InMemoryClipboardService]::new()
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityLogRepository)
    $aiProviderOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $localAiSuggestionProvider = [Orderly.Data.Services.LocalAiSuggestionProvider]::new()
    $primaryAiSuggestionProvider = [Orderly.Data.Services.AiSuggestionProviderFactory]::CreatePrimaryProvider($aiProviderOptions, $localAiSuggestionProvider)

    $customerService = [Orderly.Data.Services.CustomerService]::new($customerRepository, $activityLogRepository)
    $orderService = [Orderly.Data.Services.OrderService]::new($orderRepository, $activityLogRepository)
    $dealService = [Orderly.Data.Services.DealService]::new($dealRepository, $activityLogRepository)
    $followUpService = [Orderly.Data.Services.FollowUpService]::new($followUpRepository, $activityLogRepository)
    $noteService = [Orderly.Data.Services.NoteService]::new($noteRepository, $activityLogRepository)
    $conversationService = [Orderly.Data.Services.ConversationService]::new($conversationMessageRepository, $activityLogRepository)
    $ocrService = [Orderly.Data.Services.LocalOcrService]::new($ocrResultRepository, $activityLogRepository, $conversationService, $conversationMessageRepository)
    $aiAssistantService = [Orderly.Data.Services.LocalAiAssistantService]::new(
        $customerRepository,
        $orderRepository,
        $conversationMessageRepository,
        $aiSuggestionRepository,
        $activityLogRepository,
        $primaryAiSuggestionProvider,
        $localAiSuggestionProvider,
        $aiProviderOptions)
    $autoReplyService = [Orderly.Data.Services.LocalAutoReplyService]::new($aiSuggestionRepository, $orderRepository, $activityLogRepository, $clipboardService)
    $activityLogService = [Orderly.Data.Services.ActivityLogService]::new($activityLogRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($connectionFactory, $syncService, $syncRecordRepository, $activityLogRepository)
    $priceAdjustmentService = [Orderly.Data.Services.PriceAdjustmentService]::new($priceAdjustmentRepository, $activityLogRepository)

    return [Orderly.App.ViewModels.MainViewModel]::new(
        $customerRepository,
        $orderRepository,
        $customerService,
        $orderService,
        $dealService,
        $followUpService,
        $noteService,
        $conversationService,
        $ocrService,
        $aiAssistantService,
        $autoReplyService,
        $activityLogService,
        $backupService,
        $priceAdjustmentService,
        $replyTemplateRepository,
        $settingRepository,
        $clipboardService,
        $DatabasePath)
}

function Initialize-Database {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    $initializer = [Orderly.Data.Sqlite.DatabaseInitializer]::new($connectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
    Invoke-QaCiphertextBackfill -DatabasePath $DatabasePath
    return $connectionFactory
}

function Seed-QaOnlyData {
    param(
        [Parameter(Mandatory = $true)]
        $ConnectionFactory
    )

    $qaSeeder = [Orderly.Data.Services.QaDataSeeder]::new($ConnectionFactory)
    [void]$qaSeeder.SeedIfNeededAsync().GetAwaiter().GetResult()
    Invoke-QaCiphertextBackfill -DatabasePath $ConnectionFactory.DatabasePath
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

function New-ChecksumTamperedBackup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $backupJson = Get-Content -LiteralPath $SourcePath -Raw -Encoding utf8 | ConvertFrom-Json
    $backupJson.checksum = 'checksum-tampered-by-p2-9'
    $backupJson | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $TargetPath -Encoding utf8
}

function Assert-CountsContain {
    param(
        [Parameter(Mandatory = $true)]
        $Counts,
        [Parameter(Mandatory = $true)]
        [string[]]$Keys
    )

    foreach ($key in $Keys) {
        if (-not $Counts.ContainsKey($key)) {
            throw "Counts missing key: $key"
        }
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.9 restore preview smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/9: reset QA baseline"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/9: skip QA reset"
}

Write-Step "Step 2/9: export P2.7-format backup"
Import-OrderlyAssemblies
$sourceDbPath = Get-DefaultDatabasePath
$sourceContext = New-BackupServiceContext -DatabasePath $sourceDbPath
$runDirectory = New-QaSmokeRunDirectory
$backupPath = Join-Path $runDirectory.Path ("orderly-backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
$exportResult = $sourceContext.BackupService.ExportAsync($backupPath, 'p2.9', $true).GetAwaiter().GetResult()
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Backup file was not created: $backupPath"
}

$expectedCounts = $exportResult.Manifest.Counts

Write-Step "Step 3/9: preview empty target and verify required fields"
$emptyTargetPath = Join-Path $runDirectory.Path 'p29-empty-target.db'
[void](Initialize-Database -DatabasePath $emptyTargetPath)
Clear-BusinessTables -DatabasePath $emptyTargetPath
$emptyContext = New-BackupServiceContext -DatabasePath $emptyTargetPath
$emptyPreview = $emptyContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.9').GetAwaiter().GetResult()

if ($emptyPreview.BackupPath -ne $backupPath) { throw 'Preview backupPath mismatch.' }
if ([string]::IsNullOrWhiteSpace([string]$emptyPreview.FileName)) { throw 'Preview missing fileName.' }
if ($null -eq $emptyPreview.ExportedAt) { throw 'Preview missing exportedAt.' }
if ($emptyPreview.SchemaVersion -ne 1) { throw "Preview schemaVersion mismatch: $($emptyPreview.SchemaVersion)" }
if ([string]::IsNullOrWhiteSpace([string]$emptyPreview.Checksum)) { throw 'Preview missing checksum.' }
if (-not $emptyPreview.IsChecksumValid) { throw 'Expected preview checksum valid for exported backup.' }
Assert-CountsContain -Counts $emptyPreview.Counts -Keys @('Customers','Deals','Orders','FollowUps','CustomerNotes','ReplyTemplates','PriceAdjustments','ActivityLogs','ConversationMessages','AiSuggestions','OcrResults')
if ($emptyPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::EmptyDatabase) { throw "Expected empty target state, got: $($emptyPreview.TargetState)" }
if ($emptyPreview.WillClearQaData) { throw 'Empty target should not clear QA data.' }
if (-not $emptyPreview.CanRestore) { throw "Empty target preview should allow restore: $($emptyPreview.RefuseReason)" }
if (-not [string]::IsNullOrWhiteSpace([string]$emptyPreview.RefuseReason)) { throw "Empty target preview should not have refuse reason: $($emptyPreview.RefuseReason)" }

Write-Step "Step 4/9: preview QA-only target"
$qaTargetPath = Join-Path $runDirectory.Path 'p29-qa-target.db'
$qaTargetFactory = Initialize-Database -DatabasePath $qaTargetPath
Clear-BusinessTables -DatabasePath $qaTargetPath
Seed-QaOnlyData -ConnectionFactory $qaTargetFactory
$qaContext = New-BackupServiceContext -DatabasePath $qaTargetPath
$qaPreview = $qaContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.9').GetAwaiter().GetResult()
if ($qaPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::QaDatabase) { throw "Expected QA-only target state, got: $($qaPreview.TargetState)" }
if (-not $qaPreview.WillClearQaData) { throw 'QA-only preview should require QA clear.' }
if (-not $qaPreview.CanRestore) { throw "QA-only preview should allow restore: $($qaPreview.RefuseReason)" }

Write-Step "Step 5/9: preview production-like non-empty target"
$productionTargetPath = Join-Path $runDirectory.Path 'p29-production-target.db'
[void](Initialize-Database -DatabasePath $productionTargetPath)
$productionContext = New-BackupServiceContext -DatabasePath $productionTargetPath
$productionPreview = $productionContext.BackupService.PreviewRestoreAsync($backupPath, 'p2.9').GetAwaiter().GetResult()
if ($productionPreview.TargetState -ne [Orderly.Core.Models.BackupRestoreTargetState]::NonEmptyProductionDatabase) { throw "Expected production target state, got: $($productionPreview.TargetState)" }
if ($productionPreview.CanRestore) { throw 'Production preview must reject restore.' }
if (-not $productionPreview.RefuseReason.Contains('禁止覆盖恢复')) { throw "Production preview missing refuse reason: $($productionPreview.RefuseReason)" }

Write-Step "Step 6/9: preview checksum-tampered backup"
$checksumTamperedPath = Join-Path $runDirectory.Path 'p29-checksum-tampered.json'
New-ChecksumTamperedBackup -SourcePath $backupPath -TargetPath $checksumTamperedPath
$checksumPreview = $emptyContext.BackupService.PreviewRestoreAsync($checksumTamperedPath, 'p2.9').GetAwaiter().GetResult()
if ($checksumPreview.IsChecksumValid) { throw 'Checksum-tampered preview should be invalid.' }
if ($checksumPreview.CanRestore) { throw 'Checksum-tampered preview must reject restore.' }
if (-not $checksumPreview.RefuseReason.Contains('checksum 校验失败')) { throw "Checksum preview missing refuse reason: $($checksumPreview.RefuseReason)" }

Write-Step "Step 7/9: verify ViewModel confirmation gate resets on file switch and re-preview"
$vmTargetPath = Join-Path $runDirectory.Path 'p29-vm-target.db'
[void](Initialize-Database -DatabasePath $vmTargetPath)
Clear-BusinessTables -DatabasePath $vmTargetPath
$vm = New-MainViewModel -DatabasePath $vmTargetPath
$vm.SelectedBackupPath = $backupPath
if ($vm.RestoreBackupCommand.CanExecute($null)) { throw 'Restore command must be disabled before preview.' }

[void]$vm.ValidateBackupCommand.ExecuteAsync($null).GetAwaiter().GetResult()
if ($null -eq $vm.RestorePreview) { throw 'ViewModel did not store preview result.' }
if (-not $vm.RestorePreview.CanRestore) { throw "ViewModel preview should allow restore on empty target: $($vm.RestorePreview.RefuseReason)" }
if ($vm.IsRestoreRiskConfirmed) { throw 'Confirmation should be cleared after preview generation.' }
if ($vm.RestoreBackupCommand.CanExecute($null)) { throw 'Restore command must stay disabled before confirmation.' }

$vm.IsRestoreRiskConfirmed = $true
if (-not $vm.RestoreBackupCommand.CanExecute($null)) { throw 'Restore command should enable after confirmation on allowed preview.' }

$vm.SelectedBackupPath = $checksumTamperedPath
if ($vm.IsRestoreRiskConfirmed) { throw 'Switching backup file should clear confirmation state.' }
if ($null -ne $vm.RestorePreview) { throw 'Switching backup file should clear previous preview.' }
if ($vm.RestoreBackupCommand.CanExecute($null)) { throw 'Restore command must disable after file switch.' }

$vm.SelectedBackupPath = $backupPath
[void]$vm.ValidateBackupCommand.ExecuteAsync($null).GetAwaiter().GetResult()
$vm.IsRestoreRiskConfirmed = $true
[void]$vm.ValidateBackupCommand.ExecuteAsync($null).GetAwaiter().GetResult()
if ($vm.IsRestoreRiskConfirmed) { throw 'Re-running preview should clear confirmation state.' }
if ($vm.RestoreBackupCommand.CanExecute($null)) { throw 'Restore command must stay disabled after re-preview until re-confirmed.' }

Write-Step "Step 8/9: invoke P2.8 restore smoke to preserve restore boundary"
Invoke-QaScript -Path $p28Script

Write-Step "Step 9/9: verify exported preview counts remain aligned with source backup"
Assert-CountsContain -Counts $expectedCounts -Keys @('Customers','Deals','Orders','FollowUps','CustomerNotes','ReplyTemplates','PriceAdjustments','ActivityLogs','ConversationMessages','AiSuggestions','OcrResults')
Write-Step "P2.9 restore preview smoke completed"
