param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ArgumentList = @()
    )

    & $Path @ArgumentList

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

function New-QaMetadataJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [hashtable]$Extra = @{}
    )

    $payload = @{
        qa = @{
            tag     = 'p13qa'
            source  = 'runtime'
            key     = $Key
            markers = @('[P1.3_QA]', '[P2_QA]', '[P1.4.1_QA]', '[P1_QA_RUNTIME]', '【P。3——QA')
        }
    }

    foreach ($entry in $Extra.GetEnumerator()) {
        $payload[$entry.Key] = $entry.Value
    }

    return ($payload | ConvertTo-Json -Depth 8 -Compress)
}

function New-P34Context {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory, $fieldEncryptionService)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory, $fieldEncryptionService)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory, $fieldEncryptionService)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory, $fieldEncryptionService)
    $ocrResultRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory, $fieldEncryptionService)
    $workbenchTaskService = [Orderly.Data.Services.LocalWorkbenchTaskService]::new(
        $customerRepository,
        $orderRepository,
        $dealRepository,
        $followUpRepository,
        $messageRepository,
        $suggestionRepository,
        $ocrResultRepository,
        $activityRepository,
        $priceAdjustmentRepository)
    $resolver = [Orderly.Data.Services.PipelineStageResolver]::new(
        $customerRepository,
        $orderRepository,
        $dealRepository,
        $messageRepository,
        $suggestionRepository,
        $followUpRepository,
        $activityRepository,
        $priceAdjustmentRepository)

    return [pscustomobject]@{
        CustomerRepository        = $customerRepository
        OrderRepository           = $orderRepository
        DealRepository            = $dealRepository
        FollowUpRepository        = $followUpRepository
        MessageRepository         = $messageRepository
        SuggestionRepository      = $suggestionRepository
        OcrResultRepository       = $ocrResultRepository
        ActivityRepository        = $activityRepository
        PriceAdjustmentRepository = $priceAdjustmentRepository
        WorkbenchTaskService      = $workbenchTaskService
        Resolver                  = $resolver
    }
}

function Get-BaselineStatusText {
    return (Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')).StdOut
}

function Get-QaWorkbenchTarget {
    param(
        [Parameter(Mandatory = $true)]
        $Context
    )

    $customer = $Context.CustomerRepository.GetAllAsync().GetAwaiter().GetResult() |
        Where-Object { $_.Name -like '*[P1.3_QA]*客户-A*' } |
        Select-Object -First 1
    if ($null -eq $customer) {
        throw 'QA customer A not found.'
    }

    $orders = $Context.OrderRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult()
    $order = $orders |
        Where-Object { $_.Title -like '*[P1.3_QA]*订单-待处理*' } |
        Select-Object -First 1
    if ($null -eq $order) {
        throw 'QA order 待处理 not found.'
    }

    $messages = $Context.MessageRepository.ListByOrderIdAsync($order.Id).GetAwaiter().GetResult()
    $message = $messages | Select-Object -First 1
    if ($null -eq $message) {
        throw 'QA message not found.'
    }

    return [pscustomobject]@{
        Customer = $customer
        Order    = $order
        Message  = $message
    }
}

function New-P34Customer {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [string]$NameSuffix
    )

    $customer = [Orderly.Core.Models.Customer]::new()
    $customer.Name = "[P1.3_QA] P3.4 $NameSuffix"
    $customer.Status = [Orderly.Core.Models.CustomerStatus]::Active
    $customer.Priority = [Orderly.Core.Models.CustomerPriority]::Normal
    $customer.SourcePlatform = 'QA'
    $customer.Channel = 'P3.4 Smoke'
    $customer.ContactHandle = $Key
    $customer.Phone = ''
    $customer.Remark = "[P1.3_QA] P3.4 $NameSuffix"
    $customer.ExternalId = $Key
    $customer.RemoteId = $Key
    return $Context.CustomerRepository.CreateAsync($customer).GetAwaiter().GetResult()
}

function New-P34Order {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        [string]$Key,
        [Orderly.Core.Models.OrderStatus]$Status = [Orderly.Core.Models.OrderStatus]::PendingCommunication
    )

    $order = [Orderly.Core.Models.MerchantOrder]::new()
    $order.CustomerId = $Customer.Id
    $order.Title = "[P1.3_QA] $Key"
    $order.Status = $Status
    $order.Amount = 800
    $order.Requirement = "[P1.3_QA] P3.4 order"
    $order.SourcePlatform = 'QA'
    $order.Channel = 'P3.4 Smoke'
    $order.ExternalId = $Key
    $order.RemoteId = $Key
    return $Context.OrderRepository.CreateAsync($order).GetAwaiter().GetResult()
}

function New-P34Message {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [datetime]$OccurredAt = [DateTime]::Now
    )

    $message = [Orderly.Core.Models.ConversationMessage]::new()
    $message.CustomerId = $Customer.Id
    $message.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $message.Direction = [Orderly.Core.Models.MessageDirection]::Incoming
    $message.Channel = [Orderly.Core.Models.MessageChannel]::Manual
    $message.SenderName = '[P1.3_QA] customer'
    $message.Content = "[P1.3_QA] $Key"
    $message.MessageTime = $OccurredAt
    $message.SourceMessageId = $Key
    $message.MetadataJson = New-QaMetadataJson -Key $Key
    $message.RemoteId = $Key
    return $Context.MessageRepository.CreateAsync($message).GetAwaiter().GetResult()
}

function New-P34Activity {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [datetime]$OccurredAt,
        [Orderly.Core.Models.ActivityType]$Type = [Orderly.Core.Models.ActivityType]::CustomerUpdated,
        [string]$Title = '[P1.3_QA] recent activity'
    )

    $activity = [Orderly.Core.Models.ActivityLog]::new()
    $activity.Type = $Type
    $activity.CustomerId = $Customer.Id
    $activity.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $activity.Title = $Title
    $activity.Description = $Title
    $activity.Operator = 'qa'
    $activity.MetadataJson = New-QaMetadataJson -Key $Key
    $activity.CreatedAt = $OccurredAt
    $activity.UpdatedAt = $OccurredAt
    $activity.RemoteId = $Key
    return $Context.ActivityRepository.CreateAsync($activity).GetAwaiter().GetResult()
}

function New-P34FollowUp {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [datetime]$ScheduledAt,
        [Orderly.Core.Models.FollowUpStatus]$Status = [Orderly.Core.Models.FollowUpStatus]::Pending
    )

    $followUp = [Orderly.Core.Models.FollowUp]::new()
    $followUp.CustomerId = $Customer.Id
    $followUp.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $followUp.Title = "[P1.3_QA] $Key"
    $followUp.Content = "[P1.3_QA] $Key"
    $followUp.Status = $Status
    $followUp.ScheduledAt = $ScheduledAt
    $followUp.RemoteId = $Key
    return $Context.FollowUpRepository.CreateAsync($followUp).GetAwaiter().GetResult()
}

function New-P34Suggestion {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        $Message,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [Orderly.Core.Models.AiSuggestionStatus]$Status,
        [string]$AutoReplyState = ''
    )

    $extra = @{}
    if ($AutoReplyState) {
        $extra.autoReply = @{
            mode      = 'local-draft'
            state     = $AutoReplyState
            localOnly = $true
        }
    }

    $suggestion = [Orderly.Core.Models.AiSuggestion]::new()
    $suggestion.CustomerId = $Customer.Id
    $suggestion.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $suggestion.MessageId = if ($null -eq $Message) { $null } else { $Message.Id }
    $suggestion.SuggestionText = "[P1.3_QA] $Key"
    $suggestion.Reason = "[P1.3_QA] $Key"
    $suggestion.Status = $Status
    $suggestion.MetadataJson = New-QaMetadataJson -Key $Key -Extra $extra
    $suggestion.RemoteId = $Key
    return $Context.SuggestionRepository.CreateAsync($suggestion).GetAwaiter().GetResult()
}

function New-P34OcrResult {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $ocrResult = [Orderly.Core.Models.OcrResult]::new()
    $ocrResult.CustomerId = $Customer.Id
    $ocrResult.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $ocrResult.SourcePath = "D:\\qa\\$Key.png"
    $ocrResult.SourceName = "$Key.png"
    $ocrResult.ExtractedText = "[P1.3_QA] $Key OCR"
    $ocrResult.Status = [Orderly.Core.Models.OcrStatus]::Completed
    $ocrResult.MetadataJson = New-QaMetadataJson -Key $Key -Extra @{ provider = 'local' }
    $ocrResult.RemoteId = $Key
    return $Context.OcrResultRepository.CreateAsync($ocrResult).GetAwaiter().GetResult()
}

function Assert-TaskField {
    param(
        [Parameter(Mandatory = $true)]
        $Task,
        [Parameter(Mandatory = $true)]
        [string]$FieldName,
        [Parameter(Mandatory = $true)]
        $Expected
    )

    $actual = $Task.$FieldName
    if ($actual -ne $Expected) {
        throw "Unexpected task field: $FieldName. Expected=$Expected, Actual=$actual"
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3.4 workbench logic smoke'
Write-Step 'Scope: logic/service/resolver only, no UI, no public network, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step 'Step 1/10: reset QA data'
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step 'Step 1/10: skip QA data reset'
}

Write-Step 'Step 2/10: capture baseline QA status'
$baselineStatus = Get-BaselineStatusText

Write-Step 'Step 3/10: import assemblies and prepare service context'
Import-OrderlyAssemblies
$context = New-P34Context
$target = Get-QaWorkbenchTarget -Context $context

Write-Step 'Step 4/10: create deep-link, recent-activity, dedupe, and fallback scenarios'
$draftSuggestion = New-P34Suggestion -Context $context -Customer $target.Customer -Order $target.Order -Message $target.Message -Key 'p13qa-p34-draft-001' -Status ([Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared) -AutoReplyState 'copied'
$ocrResult = New-P34OcrResult -Context $context -Customer $target.Customer -Order $target.Order -Key 'p13qa-p34-ocr-001'

$recentCustomers = @()
for ($index = 1; $index -le 6; $index++) {
    $customer = New-P34Customer -Context $context -Key ("p13qa-p34-recent-{0:000}" -f $index) -NameSuffix ("Recent-{0}" -f $index)
    $activityTime = [DateTime]::Now.AddMinutes(-$index)
    $null = New-P34Activity -Context $context -Customer $customer -Order $null -Key ("p13qa-p34-recent-activity-{0:000}" -f $index) -OccurredAt $activityTime -Title ("[P1.3_QA] recent {0}" -f $index)
    $recentCustomers += $customer
}

$blockedRecentCustomer = New-P34Customer -Context $context -Key 'p13qa-p34-blocked-recent-001' -NameSuffix 'BlockedRecent'
$null = New-P34Activity -Context $context -Customer $blockedRecentCustomer -Order $null -Key 'p13qa-p34-blocked-activity-001' -OccurredAt ([DateTime]::Now.AddMinutes(-0.5)) -Title '[P1.3_QA] blocked recent'
$null = New-P34FollowUp -Context $context -Customer $blockedRecentCustomer -Order $null -Key 'p13qa-p34-blocked-followup-001' -ScheduledAt ([DateTime]::Today.AddHours(-2)) -Status ([Orderly.Core.Models.FollowUpStatus]::Pending)

$replyCustomer = New-P34Customer -Context $context -Key 'p13qa-p34-reply-001' -NameSuffix 'ReplyDedupe'
$replyOrder = New-P34Order -Context $context -Customer $replyCustomer -Key 'p13qa-p34-reply-order-001'
$replyMessage1 = New-P34Message -Context $context -Customer $replyCustomer -Order $replyOrder -Key 'p13qa-p34-reply-message-001' -OccurredAt ([DateTime]::Now.AddMinutes(-20))
$replyMessage2 = New-P34Message -Context $context -Customer $replyCustomer -Order $replyOrder -Key 'p13qa-p34-reply-message-002' -OccurredAt ([DateTime]::Now.AddMinutes(-10))

$closedFallbackCustomer = New-P34Customer -Context $context -Key 'p13qa-p34-closed-fallback-001' -NameSuffix 'ClosedFallback'
$closedFallbackOrder = New-P34Order -Context $context -Customer $closedFallbackCustomer -Key 'p13qa-p34-closed-order-001' -Status ([Orderly.Core.Models.OrderStatus]::Closed)

Write-Step 'Step 5/10: generate workbench tasks and validate deep-link fields'
Invoke-QaCiphertextBackfill -DatabasePath (Get-DefaultDatabasePath)
$tasks = $context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult()
if ($tasks.Count -lt 8) {
    throw "Expected at least 8 workbench tasks after projection, actual: $($tasks.Count)"
}

$draftTask = $tasks | Where-Object { $_.Type.ToString() -eq 'DraftNotSent' -and $_.AiSuggestionId -eq $draftSuggestion.Id } | Select-Object -First 1
if ($null -eq $draftTask) {
    throw 'DraftNotSent task for copied draft was not projected.'
}
Assert-TaskField -Task $draftTask -FieldName 'TargetSection' -Expected 'AiSuggestion'
Assert-TaskField -Task $draftTask -FieldName 'ActionHint' -Expected 'ReviewDraft'
Assert-TaskField -Task $draftTask -FieldName 'AiSuggestionId' -Expected $draftSuggestion.Id

$ocrTask = $tasks | Where-Object { $_.Type.ToString() -eq 'OcrNotConverted' -and $_.OcrResultId -eq $ocrResult.Id } | Select-Object -First 1
if ($null -eq $ocrTask) {
    throw 'OcrNotConverted task for completed OCR result was not projected.'
}
Assert-TaskField -Task $ocrTask -FieldName 'TargetSection' -Expected 'Ocr'
Assert-TaskField -Task $ocrTask -FieldName 'ActionHint' -Expected 'ConvertOcrToMessage'
Assert-TaskField -Task $ocrTask -FieldName 'OcrResultId' -Expected $ocrResult.Id

$followUpTodayTasks = @($tasks | Where-Object { $_.Type.ToString() -eq 'FollowUpToday' })
$followUpOverdueTasks = @($tasks | Where-Object { $_.Type.ToString() -eq 'FollowUpOverdue' })
if ($followUpTodayTasks.Count -eq 0 -or $followUpOverdueTasks.Count -eq 0) {
    throw 'Expected FollowUpToday and FollowUpOverdue tasks.'
}
if (@($followUpTodayTasks + $followUpOverdueTasks | Where-Object { -not ($_.FollowUpId -gt 0) }).Count -gt 0) {
    throw 'FollowUp task deep-link fields are incomplete.'
}

Write-Step 'Step 6/10: validate RecentlyActive noise reduction and task dedupe'
$recentTasks = @($tasks | Where-Object { $_.Type.ToString() -eq 'RecentlyActiveCustomer' })
if ($recentTasks.Count -gt 5) {
    throw "RecentlyActiveCustomer should be capped at 5, actual: $($recentTasks.Count)"
}

$recentCustomerIds = @($recentTasks | Select-Object -ExpandProperty CustomerId)
if ($recentCustomerIds -contains $blockedRecentCustomer.Id) {
    throw 'RecentlyActiveCustomer should be suppressed when a blocking task exists for the same customer.'
}
if (@($recentCustomerIds | Select-Object -Unique).Count -ne $recentTasks.Count) {
    throw 'RecentlyActiveCustomer should keep at most one task per customer.'
}

$recentInjectedCustomerIds = @($recentCustomers | Select-Object -ExpandProperty Id)
if (@($recentCustomerIds | Where-Object { $recentInjectedCustomerIds -contains $_ }).Count -eq 0) {
    throw 'Expected at least one injected recent-only customer to survive the projection.'
}

for ($index = 0; $index -lt $recentTasks.Count - 1; $index++) {
    if ($recentTasks[$index].OccurredAt -lt $recentTasks[$index + 1].OccurredAt) {
        throw 'RecentlyActiveCustomer tasks are not sorted by latest activity descending.'
    }
}

$replyTasks = @($tasks | Where-Object { $_.Type.ToString() -eq 'ReplyNeeded' -and $_.CustomerId -eq $replyCustomer.Id -and $_.OrderId -eq $replyOrder.Id })
if ($replyTasks.Count -ne 1) {
    throw "ReplyNeeded should dedupe to one task per customer/order scope. Actual: $($replyTasks.Count)"
}
Assert-TaskField -Task $replyTasks[0] -FieldName 'MessageId' -Expected $replyMessage2.Id

$duplicateTaskKeys = @($tasks | Group-Object DedupeKey | Where-Object { $_.Count -gt 1 })
if ($duplicateTaskKeys.Count -gt 0) {
    throw "Duplicate dedupe keys detected after projection: $($duplicateTaskKeys.Name -join ', ')"
}

$taskTypeOrder = @($tasks | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
$expectedRelativeOrder = @('FollowUpOverdue', 'DraftNotSent', 'ReplyNeeded', 'AiSuggestionPending', 'OcrNotConverted', 'FollowUpToday')
for ($index = 0; $index -lt $expectedRelativeOrder.Count - 1; $index++) {
    $left = $taskTypeOrder.IndexOf($expectedRelativeOrder[$index])
    $right = $taskTypeOrder.IndexOf($expectedRelativeOrder[$index + 1])
    if ($left -lt 0 -or $right -lt 0 -or $left -gt $right) {
        throw "Unexpected workbench task sort order around $($expectedRelativeOrder[$index]) -> $($expectedRelativeOrder[$index + 1])"
    }
}

Write-Step 'Step 7/10: validate sort stability across repeated reads'
$secondPass = $context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult()
$firstIds = @($tasks | Select-Object -ExpandProperty Id)
$secondIds = @($secondPass | Select-Object -ExpandProperty Id)
if (($firstIds -join '|') -ne ($secondIds -join '|')) {
    throw 'Workbench task order is not stable between repeated reads.'
}

Write-Step 'Step 8/10: validate pipeline fallback safety'
$missingCustomerSnapshot = $context.Resolver.ResolveAsync(999999).GetAwaiter().GetResult()
if ($missingCustomerSnapshot.Stage.ToString() -ne 'New' -or -not $missingCustomerSnapshot.UsedFallback) {
    throw 'Missing-customer pipeline fallback should resolve to New with UsedFallback = true.'
}

$closedFallbackSnapshot = $context.Resolver.ResolveAsync($closedFallbackCustomer.Id, $closedFallbackOrder.Id).GetAwaiter().GetResult()
if ($closedFallbackSnapshot.Stage.ToString() -ne 'Lost' -or -not $closedFallbackSnapshot.UsedFallback) {
    throw 'Closed order without success signals should safely resolve to Lost with UsedFallback = true.'
}

Write-Step 'Step 9/10: reset QA data and ensure baseline is restored'
Invoke-QaScript -Path $resetScript
$restoredStatus = Get-BaselineStatusText
if ($baselineStatus -ne $restoredStatus) {
    throw 'QA baseline status changed after reset-qa-data.'
}

Write-Step 'Step 10/10: final pass'
Write-Host ''
Write-Host 'P3.4 WORKBENCH LOGIC SMOKE: PASS'
Write-Host ('Task count after projection: ' + $tasks.Count)
Write-Host ('Task order: ' + ($taskTypeOrder -join ', '))
