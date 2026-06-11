param()

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

function New-SqliteConnectionFactory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    return [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
}

function New-LauncherConnectionFactory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    return [Orderly.Data.Sqlite.LauncherConnectionFactory]::new($DatabasePath)
}

function Initialize-LauncherDatabase {
    param(
        [Parameter(Mandatory = $true)]
        $LauncherConnectionFactory
    )

    $initializer = [Orderly.Data.Sqlite.LauncherDatabaseInitializer]::new($LauncherConnectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
}

function Initialize-BusinessDatabase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $connectionFactory = New-SqliteConnectionFactory -DatabasePath $DatabasePath
    $initializer = [Orderly.Data.Sqlite.DatabaseInitializer]::new($connectionFactory)
    [void]$initializer.InitializeAsync().GetAwaiter().GetResult()
    Clear-BusinessTables -DatabasePath $DatabasePath
    return $connectionFactory
}

function Clear-BusinessTables {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $commandText = @"
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
DELETE FROM ReplyTemplates;
"@

    Invoke-SqlNonQuery -DatabasePath $DatabasePath -CommandText $commandText
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

function Invoke-SqlQuerySingle {
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
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) {
            throw "Query returned no rows."
        }

        $row = @{}
        for ($index = 0; $index -lt $reader.FieldCount; $index++) {
            $row[$reader.GetName($index)] = if ($reader.IsDBNull($index)) { $null } else { $reader.GetValue($index) }
        }

        return $row
    }
    finally {
        $connection.Dispose()
    }
}

function New-SessionContextService {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AccountId,
        [Parameter(Mandatory = $true)]
        [string]$Username,
        [Parameter(Mandatory = $true)]
        [string]$DisplayName,
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        [byte[]]$DataKey
    )

    $sessionContextService = [Orderly.Data.Services.SessionContextService]::new()
    $sessionContext = [Orderly.Core.Models.LocalSessionContext]@{
        AccountId    = $AccountId
        Username     = $Username
        DisplayName  = $DisplayName
        Role         = [Orderly.Core.Models.LocalAccountRole]::Owner
        DatabasePath = $DatabasePath
        DataKey      = $DataKey
        SignedInAt   = [DateTime]::Now
    }
    $sessionContextService.SetCurrent($sessionContext)
    return $sessionContextService
}

function Get-HashBytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [byte[]]$Salt,
        [Parameter(Mandatory = $true)]
        [int]$Iterations
    )

    return [System.Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2(
        $Value,
        $Salt,
        $Iterations,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        32)
}

function Protect-DataKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Secret,
        [Parameter(Mandatory = $true)]
        [byte[]]$Salt,
        [Parameter(Mandatory = $true)]
        [int]$Iterations,
        [Parameter(Mandatory = $true)]
        [byte[]]$DataKey
    )

    $key = Get-HashBytes -Value $Secret -Salt $Salt -Iterations $Iterations
    $nonce = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
    $ciphertext = [byte[]]::new($DataKey.Length)
    $tag = [byte[]]::new(16)
    $aes = [System.Security.Cryptography.AesGcm]::new($key, $tag.Length)
    try {
        $aes.Encrypt($nonce, $DataKey, $ciphertext, $tag)
    }
    finally {
        $aes.Dispose()
    }

    return [pscustomobject]@{
        Ciphertext = $ciphertext
        Nonce = $nonce
        Tag = $tag
    }
}

function New-TestLocalAccount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $passwordIterations = 200000
    $pinIterations = 200000
    $masterPassword = 'Owner#Smoke#2026'
    $pin = '246810'
    $dataKey = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $passwordSalt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $pinSalt = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $wrapped = Protect-DataKey -Secret $masterPassword -Salt $passwordSalt -Iterations $passwordIterations -DataKey $dataKey

    return [pscustomobject]@{
        Account = [Orderly.Core.Models.LocalAccount]@{
            AccountId = ([guid]::NewGuid().ToString('N'))
            Username = 'owner_smoke'
            DisplayName = 'Owner Smoke'
            PasswordHash = (Get-HashBytes -Value $masterPassword -Salt $passwordSalt -Iterations $passwordIterations)
            PasswordSalt = $passwordSalt
            PasswordIterations = $passwordIterations
            PinHash = (Get-HashBytes -Value $pin -Salt $pinSalt -Iterations $pinIterations)
            PinSalt = $pinSalt
            PinIterations = $pinIterations
            EncryptedDataKey = $wrapped.Ciphertext
            DataKeyNonce = $wrapped.Nonce
            DataKeyTag = $wrapped.Tag
            DatabasePath = $DatabasePath
            Role = [Orderly.Core.Models.LocalAccountRole]::Owner
            IsEnabled = $true
            CreatedAt = [DateTime]::Now
            UpdatedAt = [DateTime]::Now
        }
        MasterPassword = $masterPassword
        Pin = $pin
        DataKey = $dataKey
    }
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

    $connectionFactory = New-SqliteConnectionFactory -DatabasePath $DatabasePath
    $fieldEncryptionService = [Orderly.Data.Services.FieldEncryptionService]::new($SessionContextService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory, $fieldEncryptionService)
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($syncRecordRepository, $activityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new(
        $connectionFactory,
        $syncService,
        $syncRecordRepository,
        $activityRepository,
        $LauncherConnectionFactory,
        $SessionContextService)

    return [pscustomobject]@{
        ConnectionFactory = $connectionFactory
        FieldEncryptionService = $fieldEncryptionService
        ActivityRepository = $activityRepository
        SyncRecordRepository = $syncRecordRepository
        BackupService = $backupService
    }
}

function Insert-EncryptedReplyTemplate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [Parameter(Mandatory = $true)]
        $FieldEncryptionService,
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $ciphertext = $FieldEncryptionService.Encrypt($Content)
    $now = [DateTime]::Now.ToString('O')
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$DatabasePath;Foreign Keys=True")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @'
INSERT INTO ReplyTemplates (Title, Scene, Content, ContentCiphertext, IsFavorite, SourcePlatform, CreatedAt, UpdatedAt)
VALUES ($title, 'smoke-scene', '', $contentCiphertext, 1, 'smoke-platform', $createdAt, $updatedAt);
'@
        [void]$command.Parameters.AddWithValue('$title', $Title)
        [void]$command.Parameters.AddWithValue('$contentCiphertext', $ciphertext)
        [void]$command.Parameters.AddWithValue('$createdAt', $now)
        [void]$command.Parameters.AddWithValue('$updatedAt', $now)
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

Assert-NoRunningOrderlyProcess
Import-OrderlyAssembliesForQa

$runDirectory = New-QaSmokeRunDirectory
$sourceLauncherPath = Join-Path $runDirectory.Path 'source-launcher.db'
$sourceAccountPath = Join-Path $runDirectory.Path 'source-account.db'
$targetLauncherPath = Join-Path $runDirectory.Path 'target-launcher.db'
$targetAccountPath = Join-Path $runDirectory.Path 'target-account.db'
$backupPath = Join-Path $runDirectory.Path 'real-encrypted-local-account-backup.json'

Write-Step "Starting P4 real encryption restore smoke"
Write-Step "Repo root: $(Get-RepoRoot)"
Write-Step "Step 1/8: initialize isolated launcher/account databases"

$sourceLauncherFactory = New-LauncherConnectionFactory -DatabasePath $sourceLauncherPath
Initialize-LauncherDatabase -LauncherConnectionFactory $sourceLauncherFactory
[void](Initialize-BusinessDatabase -DatabasePath $sourceAccountPath)

$sourceAccountSeed = New-TestLocalAccount -DatabasePath $sourceAccountPath
$sourceAccountRepository = [Orderly.Data.Repositories.LocalAccountRepository]::new($sourceLauncherFactory)
[void]$sourceAccountRepository.CreateAsync($sourceAccountSeed.Account).GetAwaiter().GetResult()
$sourceSessionContext = New-SessionContextService `
    -AccountId $sourceAccountSeed.Account.AccountId `
    -Username $sourceAccountSeed.Account.Username `
    -DisplayName $sourceAccountSeed.Account.DisplayName `
    -DatabasePath $sourceAccountPath `
    -DataKey $sourceAccountSeed.DataKey
$sourceBackupContext = New-BackupContext `
    -DatabasePath $sourceAccountPath `
    -LauncherConnectionFactory $sourceLauncherFactory `
    -SessionContextService $sourceSessionContext

Write-Step "Step 2/8: write encrypted customer/order/template data"

$customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($sourceBackupContext.ConnectionFactory, $sourceBackupContext.FieldEncryptionService)
$orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($sourceBackupContext.ConnectionFactory, $sourceBackupContext.FieldEncryptionService)
$replyTemplateRepository = [Orderly.Data.Repositories.ReplyTemplateRepository]::new($sourceBackupContext.ConnectionFactory, $sourceBackupContext.FieldEncryptionService)

$customer = [Orderly.Core.Models.Customer]@{
    Name = 'Smoke Customer'
    Status = [Orderly.Core.Models.CustomerStatus]::Active
    Priority = [Orderly.Core.Models.CustomerPriority]::High
    SourcePlatform = 'smoke-platform'
    Channel = 'smoke-channel'
    ContactHandle = 'smoke_handle'
    Phone = '13800138000'
    Remark = 'real encryption smoke customer marker'
    ExternalId = 'smoke-customer-001'
    RawPayload = '{"marker":"real-encryption"}'
    LastContactAt = [DateTime]::Parse('2026-05-17T09:30:00+08:00')
}
$customer = $customerRepository.CreateAsync($customer).GetAwaiter().GetResult()

$expectedOrderAmount = [decimal]'1880.50'
$order = [Orderly.Core.Models.MerchantOrder]@{
    CustomerId = $customer.Id
    Title = 'Smoke Encrypted Order'
    Status = [Orderly.Core.Models.OrderStatus]::PendingQuote
    Amount = $expectedOrderAmount
    Requirement = 'restore-and-relogin-marker'
    SourcePlatform = 'smoke-platform'
    Channel = 'smoke-channel'
    ExternalId = 'smoke-order-001'
    RawPayload = '{"scope":"restore"}'
    NextFollowUpAt = [DateTime]::Parse('2026-05-18T10:15:00+08:00')
}
$order = $orderRepository.CreateAsync($order).GetAwaiter().GetResult()

Insert-EncryptedReplyTemplate `
    -DatabasePath $sourceAccountPath `
    -FieldEncryptionService $sourceBackupContext.FieldEncryptionService `
    -Title 'Smoke Reply Template' `
    -Content 'reply-template-encrypted-marker'

$sourceCustomerRow = Invoke-SqlQuerySingle -DatabasePath $sourceAccountPath -CommandText @"
SELECT Name, NameCiphertext, Phone, PhoneCiphertext, Remark, RemarkCiphertext
FROM Customers
WHERE Id = $($customer.Id);
"@
if ($sourceCustomerRow.Name -ne '' -or $sourceCustomerRow.Phone -ne '' -or $sourceCustomerRow.Remark -ne '') {
    throw 'Plaintext customer columns were not cleared on write.'
}

if (-not ([string]$sourceCustomerRow.NameCiphertext).StartsWith('v1:')) {
    throw 'Customer NameCiphertext was not written with real encryption.'
}

$sourceTemplateRow = Invoke-SqlQuerySingle -DatabasePath $sourceAccountPath -CommandText @"
SELECT Content, ContentCiphertext
FROM ReplyTemplates
WHERE Title = 'Smoke Reply Template';
"@
if ($sourceTemplateRow.Content -ne '') {
    throw 'Plaintext reply template content was not cleared.'
}

if (-not ([string]$sourceTemplateRow.ContentCiphertext).StartsWith('v1:')) {
    throw 'ReplyTemplate ContentCiphertext was not written with real encryption.'
}

Write-Step "Step 3/8: export and validate encrypted backup"
$exportResult = $sourceBackupContext.BackupService.ExportAsync($backupPath, 'p4-real-encryption', $false).GetAwaiter().GetResult()
if (-not (Test-Path -LiteralPath $backupPath)) {
    throw "Backup file was not created: $backupPath"
}

$validationResult = $sourceBackupContext.BackupService.ValidateAsync($backupPath, 'p4-real-encryption', $false).GetAwaiter().GetResult()
if (-not $validationResult.IsValid) {
    throw "Encrypted backup validation failed: $($validationResult.Errors -join '; ')"
}

Write-Step "Step 4/8: initialize isolated restore target"
$targetLauncherFactory = New-LauncherConnectionFactory -DatabasePath $targetLauncherPath
Initialize-LauncherDatabase -LauncherConnectionFactory $targetLauncherFactory
[void](Initialize-BusinessDatabase -DatabasePath $targetAccountPath)

$targetRestoreSession = New-SessionContextService `
    -AccountId $sourceAccountSeed.Account.AccountId `
    -Username $sourceAccountSeed.Account.Username `
    -DisplayName $sourceAccountSeed.Account.DisplayName `
    -DatabasePath $targetAccountPath `
    -DataKey $sourceAccountSeed.DataKey
$targetBackupContext = New-BackupContext `
    -DatabasePath $targetAccountPath `
    -LauncherConnectionFactory $targetLauncherFactory `
    -SessionContextService $targetRestoreSession

Write-Step "Step 5/8: preview and restore backup"
$preview = $targetBackupContext.BackupService.PreviewRestoreAsync($backupPath, 'p4-real-encryption').GetAwaiter().GetResult()
if (-not $preview.CanRestore) {
    throw "Restore preview unexpectedly refused: $($preview.RefuseReason)"
}

$restoreResult = $targetBackupContext.BackupService.RestoreBackupAsync($backupPath, $false, 'p4-real-encryption').GetAwaiter().GetResult()
if ($restoreResult.SyncStatus -ne [Orderly.Core.Models.SyncStatus]::Synced) {
    throw "Restore failed: $($restoreResult.SyncStatus)"
}

$targetLauncherRow = Invoke-SqlQuerySingle -DatabasePath $targetLauncherPath -CommandText @"
SELECT AccountId, Username, DatabasePath
FROM LocalAccounts
WHERE AccountId = '$($sourceAccountSeed.Account.AccountId)';
"@
if ($targetLauncherRow.DatabasePath -ne $targetAccountPath) {
    throw 'Restored launcher database path was not rewritten to the target account path.'
}

Write-Step "Step 6/8: re-login against restored launcher/account database"
$postRestoreSessionContext = [Orderly.Data.Services.SessionContextService]::new()
$targetAccountRepository = [Orderly.Data.Repositories.LocalAccountRepository]::new($targetLauncherFactory)
$targetAuthService = [Orderly.Data.Services.LocalAuthService]::new(
    $targetAccountRepository,
    [Orderly.Data.Services.LegacyDatabaseMigrationService]::new(),
    $postRestoreSessionContext)
$signInResult = $targetAuthService.SignInAsync($sourceAccountSeed.Account.Username, $sourceAccountSeed.MasterPassword).GetAwaiter().GetResult()
if (-not $signInResult.Succeeded -or $null -eq $signInResult.Session) {
    throw "Re-login after restore failed: $($signInResult.ErrorMessage)"
}

Write-Step "Step 7/8: verify decrypted reads after re-login"
$restoredFieldEncryptionService = [Orderly.Data.Services.FieldEncryptionService]::new($postRestoreSessionContext)
$restoredConnectionFactory = New-SqliteConnectionFactory -DatabasePath $targetAccountPath
$restoredCustomerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($restoredConnectionFactory, $restoredFieldEncryptionService)
$restoredOrderRepository = [Orderly.Data.Repositories.OrderRepository]::new($restoredConnectionFactory, $restoredFieldEncryptionService)
$restoredReplyTemplateRepository = [Orderly.Data.Repositories.ReplyTemplateRepository]::new($restoredConnectionFactory, $restoredFieldEncryptionService)

$restoredCustomer = $restoredCustomerRepository.GetByIdAsync($customer.Id).GetAwaiter().GetResult()
if ($null -eq $restoredCustomer -or $restoredCustomer.Phone -ne '13800138000' -or $restoredCustomer.Remark -ne 'real encryption smoke customer marker') {
    throw 'Restored customer decrypted fields do not match expected values.'
}

$restoredOrder = $restoredOrderRepository.GetByIdAsync($order.Id).GetAwaiter().GetResult()
if ($null -eq $restoredOrder -or $restoredOrder.Amount -ne $expectedOrderAmount -or $restoredOrder.Requirement -ne 'restore-and-relogin-marker') {
    throw 'Restored order decrypted fields do not match expected values.'
}

$restoredTemplate = @($restoredReplyTemplateRepository.GetAllAsync().GetAwaiter().GetResult() | Where-Object { $_.Title -eq 'Smoke Reply Template' }) | Select-Object -First 1
if ($null -eq $restoredTemplate -or $restoredTemplate.Content -ne 'reply-template-encrypted-marker') {
    throw 'Restored reply template could not be decrypted after re-login.'
}

Write-Step "Step 8/8: verify restored ciphertext stayed encrypted at rest"
$restoredCustomerRow = Invoke-SqlQuerySingle -DatabasePath $targetAccountPath -CommandText @"
SELECT Name, NameCiphertext, Requirement, RequirementCiphertext
FROM Customers
JOIN Orders ON Orders.CustomerId = Customers.Id
WHERE Customers.Id = $($customer.Id) AND Orders.Id = $($order.Id);
"@
if ($restoredCustomerRow.Name -ne '' -or $restoredCustomerRow.Requirement -ne '') {
    throw 'Restored plaintext columns were unexpectedly populated.'
}

if (-not ([string]$restoredCustomerRow.NameCiphertext).StartsWith('v1:') -or -not ([string]$restoredCustomerRow.RequirementCiphertext).StartsWith('v1:')) {
    throw 'Restored ciphertext columns are not using real encrypted payloads.'
}

Write-Step "P4 real encryption restore smoke completed"
