param(
    [switch]$SkipReset,
    [switch]$SkipFinalReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ArgumentList = @(),
        [hashtable]$NamedArguments = @{}
    )

    & $Path @ArgumentList @NamedArguments

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)] [bool]$Condition,
        [Parameter(Mandatory = $true)] [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-P1WriteContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService

    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $noteRepository = [Orderly.Data.Repositories.CustomerNoteRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)

    $orderService = [Orderly.Data.Services.OrderService]::new($orderRepository, $activityRepository)
    $noteService = [Orderly.Data.Services.NoteService]::new($noteRepository, $activityRepository)

    return [pscustomobject]@{
        DatabasePath = $databasePath
        CustomerRepository = $customerRepository
        OrderRepository = $orderRepository
        NoteRepository = $noteRepository
        ActivityRepository = $activityRepository
        OrderService = $orderService
        NoteService = $noteService
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P1 write smoke"
Write-Step "Repo root: $(Get-RepoRoot)"
$previousQaDbPath = $env:ORDERLY_QA_DB_PATH
if ([string]::IsNullOrWhiteSpace($env:ORDERLY_QA_DB_PATH)) {
    $env:ORDERLY_QA_DB_PATH = Join-RepoPath @('artifacts', 'qa-db', 'orderly.qa.db')
}

Write-Step "Database path: $(Get-DefaultDatabasePath)"

try {
    if (-not $SkipReset) {
        Write-Step "Step 1/5: reset QA data"
        Invoke-QaScript -Path $resetScript
    } else {
        Write-Step "Step 1/5: skip QA data reset"
    }

    Write-Step "Step 2/5: import assemblies and prepare context"
    Import-OrderlyAssembliesForQa
    $context = New-P1WriteContext

    Write-Step "Step 3/5: create runtime order and note through services"
    $customer = @($context.CustomerRepository.GetAllAsync().GetAwaiter().GetResult() | Where-Object { $_.RemoteId -eq 'p13qa-customer-a' }) | Select-Object -First 1
    Assert-True -Condition ($null -ne $customer) -Message "QA customer not found: p13qa-customer-a"

    $runtimeMarker = '[P1_QA_RUNTIME]'
    $runToken = "write-$([DateTime]::Now.ToString('yyyyMMddHHmmssfff'))"
    $runtimeKey = "p13qa-runtime-$runToken"
    $orderTitle = "$runtimeMarker ServiceOrder $runToken"
    $orderRequirement = "$runtimeMarker ServiceRequirement $runToken"
    $noteContent = "$runtimeMarker ServiceNote $runToken"

    $beforeActivities = @($context.ActivityRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult())

    $order = [Orderly.Core.Models.MerchantOrder]::new()
    $order.CustomerId = $customer.Id
    $order.Title = $orderTitle
    $order.Status = [Orderly.Core.Models.OrderStatus]::PendingCommunication
    $order.Amount = 199
    $order.Requirement = $orderRequirement
    $order.SourcePlatform = $customer.SourcePlatform
    $order.Channel = $customer.Channel
    $order.ExternalId = $runtimeKey
    $order.RemoteId = $runtimeKey

    $createdOrder = $context.OrderService.SaveOrderAsync($order).GetAwaiter().GetResult()
    Assert-True -Condition ($createdOrder.Id -gt 0) -Message "OrderService.SaveOrderAsync did not return a valid order id."

    $activityMetadata = @{
        qa = @{
            tag = 'p13qa'
            source = 'runtime'
            key = $runtimeKey
            markers = @('[P1.3_QA]', '[P1_QA_RUNTIME]')
        }
    } | ConvertTo-Json -Depth 8 -Compress

    $note = [Orderly.Core.Models.CustomerNote]::new()
    $note.CustomerId = $customer.Id
    $note.OrderId = $createdOrder.Id
    $note.Type = [Orderly.Core.Models.NoteType]::General
    $note.Content = $noteContent
    $note.RemoteId = "$runtimeKey-note"

    $createdNote = $context.NoteService.SaveNoteAsync($note, $activityMetadata).GetAwaiter().GetResult()
    Assert-True -Condition ($createdNote.Id -gt 0) -Message "NoteService.SaveNoteAsync did not return a valid note id."

    Write-Step "Step 4/5: verify write chain and activity logs"
    $orders = @($context.OrderRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult())
    $notes = @($context.NoteRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult())
    $afterActivities = @($context.ActivityRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult())

    $savedOrder = @($orders | Where-Object { $_.Id -eq $createdOrder.Id -and $_.Title -eq $orderTitle -and $_.Requirement -eq $orderRequirement }) | Select-Object -First 1
    Assert-True -Condition ($null -ne $savedOrder) -Message "Created order not found after save."

    $savedNote = @($notes | Where-Object { $_.Id -eq $createdNote.Id -and $_.OrderId -eq $createdOrder.Id -and $_.Content -eq $noteContent }) | Select-Object -First 1
    Assert-True -Condition ($null -ne $savedNote) -Message "Created note not found after save."

    $orderActivity = @($afterActivities | Where-Object {
        $_.Type -eq [Orderly.Core.Models.ActivityType]::OrderCreated -and
        $_.OrderId -eq $createdOrder.Id -and
        $_.Description -eq $orderTitle
    }) | Select-Object -First 1
    Assert-True -Condition ($null -ne $orderActivity) -Message "OrderCreated activity not found."

    $noteActivity = @($afterActivities | Where-Object {
        $_.Type -eq [Orderly.Core.Models.ActivityType]::NoteCreated -and
        $_.OrderId -eq $createdOrder.Id -and
        $_.Description -eq $noteContent -and
        $_.MetadataJson -like '*"source":"runtime"*'
    }) | Select-Object -First 1
    Assert-True -Condition ($null -ne $noteActivity) -Message "NoteCreated activity (runtime metadata) not found."

    Assert-True -Condition ($afterActivities.Count -ge ($beforeActivities.Count + 2)) -Message "Activity log did not increase as expected."

    Write-Step "Step 5/5: reset QA baseline after write verification"
    if (-not $SkipFinalReset) {
        Invoke-QaScript -Path $resetScript
    } else {
        Write-Step "Skip final reset (requested)"
    }

    Write-Host ""
    Write-Host "P1 WRITE SMOKE: PASS"
    Write-Host "Created order id: $($createdOrder.Id)"
    Write-Host "Created note id: $($createdNote.Id)"
}
finally {
    $env:ORDERLY_QA_DB_PATH = $previousQaDbPath
}
