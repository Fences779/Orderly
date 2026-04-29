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

function New-PipelineContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory)
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
        ConnectionFactory          = $connectionFactory
        CustomerRepository         = $customerRepository
        OrderRepository            = $orderRepository
        DealRepository             = $dealRepository
        MessageRepository          = $messageRepository
        SuggestionRepository       = $suggestionRepository
        ActivityRepository         = $activityRepository
        PriceAdjustmentRepository  = $priceAdjustmentRepository
        Resolver                   = $resolver
    }
}

function New-P3Customer {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [string]$NameSuffix,
        [Orderly.Core.Models.CustomerStatus]$Status = [Orderly.Core.Models.CustomerStatus]::Active,
        [object]$LastContactAt = $null
    )

    $customer = [Orderly.Core.Models.Customer]::new()
    $customer.Name = "[P1.3_QA] P3 $NameSuffix"
    $customer.Status = $Status
    $customer.Priority = [Orderly.Core.Models.CustomerPriority]::Normal
    $customer.SourcePlatform = 'QA'
    $customer.Channel = 'P3 Smoke'
    $customer.ContactHandle = $Key
    $customer.Phone = ''
    $customer.Remark = "[P1.3_QA] pipeline smoke $NameSuffix"
    $customer.ExternalId = $Key
    $customer.RemoteId = $Key
    if ($null -ne $LastContactAt) {
        $customer.LastContactAt = [datetime]$LastContactAt
    }
    return $Context.CustomerRepository.CreateAsync($customer).GetAwaiter().GetResult()
}

function New-P3Deal {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [Orderly.Core.Models.DealStage]$Stage
    )

    $deal = [Orderly.Core.Models.Deal]::new()
    $deal.CustomerId = $Customer.Id
    $deal.Title = "[P1.3_QA] $Key"
    $deal.Stage = $Stage
    $deal.EstimatedAmount = 1000
    $deal.Requirement = "[P1.3_QA] pipeline smoke deal"
    $deal.SourcePlatform = 'QA'
    $deal.Channel = 'P3 Smoke'
    $deal.RemoteId = $Key
    return $Context.DealRepository.CreateAsync($deal).GetAwaiter().GetResult()
}

function New-P3Order {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Deal,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [Orderly.Core.Models.OrderStatus]$Status
    )

    $order = [Orderly.Core.Models.MerchantOrder]::new()
    $order.CustomerId = $Customer.Id
    $order.DealId = if ($null -eq $Deal) { $null } else { $Deal.Id }
    $order.Title = "[P1.3_QA] $Key"
    $order.Status = $Status
    $order.Amount = 1200
    $order.Requirement = "[P1.3_QA] pipeline smoke order"
    $order.SourcePlatform = 'QA'
    $order.Channel = 'P3 Smoke'
    $order.ExternalId = $Key
    $order.RemoteId = $Key
    return $Context.OrderRepository.CreateAsync($order).GetAwaiter().GetResult()
}

function New-P3Message {
    param(
        [Parameter(Mandatory = $true)]
        $Context,
        [Parameter(Mandatory = $true)]
        $Customer,
        $Order,
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $message = [Orderly.Core.Models.ConversationMessage]::new()
    $message.CustomerId = $Customer.Id
    $message.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $message.Direction = [Orderly.Core.Models.MessageDirection]::Incoming
    $message.Channel = [Orderly.Core.Models.MessageChannel]::Manual
    $message.SenderName = '[P1.3_QA] pipeline customer'
    $message.Content = '[P1.3_QA] pipeline contacted'
    $message.MessageTime = [DateTime]::Now
    $message.SourceMessageId = $Key
    $message.MetadataJson = New-QaMetadataJson -Key $Key
    $message.RemoteId = $Key
    return $Context.MessageRepository.CreateAsync($message).GetAwaiter().GetResult()
}

function New-P3Suggestion {
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

function Assert-Stage {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot,
        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Snapshot.Stage.ToString() -ne $Expected) {
        throw "Expected PipelineStage $Expected, actual: $($Snapshot.Stage)"
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3.2 pipeline smoke'
Write-Step 'Scope: local-only resolver, no schema mutation, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step 'Step 1/9: reset QA data'
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step 'Step 1/9: skip QA data reset'
}

Write-Step 'Step 2/9: import assemblies and prepare resolver context'
Import-OrderlyAssemblies
$context = New-PipelineContext

Write-Step 'Step 3/9: create pipeline stage scenarios'
$newCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-new-001' -NameSuffix 'New'
$fallbackCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-fallback-001' -NameSuffix 'Fallback'
$null = New-P3Order -Context $context -Customer $fallbackCustomer -Deal $null -Key 'p13qa-p3-fallback-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingCommunication)

$contactCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-contacted-001' -NameSuffix 'Contacted'
$contactOrder = New-P3Order -Context $context -Customer $contactCustomer -Deal $null -Key 'p13qa-p3-contacted-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingCommunication)
$null = New-P3Message -Context $context -Customer $contactCustomer -Order $contactOrder -Key 'p13qa-p3-contacted-message-001'

$interestCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-interested-001' -NameSuffix 'Interested'
$interestOrder = New-P3Order -Context $context -Customer $interestCustomer -Deal $null -Key 'p13qa-p3-interested-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingCommunication)
$interestMessage = New-P3Message -Context $context -Customer $interestCustomer -Order $interestOrder -Key 'p13qa-p3-interested-message-001'
$null = New-P3Suggestion -Context $context -Customer $interestCustomer -Order $interestOrder -Message $interestMessage -Key 'p13qa-p3-interested-suggestion-001' -Status ([Orderly.Core.Models.AiSuggestionStatus]::Draft)

$quoteCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-quoted-001' -NameSuffix 'Quoted'
$quoteDeal = New-P3Deal -Context $context -Customer $quoteCustomer -Key 'p13qa-p3-quoted-deal-001' -Stage ([Orderly.Core.Models.DealStage]::Quoting)
$quoteOrder = New-P3Order -Context $context -Customer $quoteCustomer -Deal $quoteDeal -Key 'p13qa-p3-quoted-order-001' -Status ([Orderly.Core.Models.OrderStatus]::Quoted)

$draftCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-draft-001' -NameSuffix 'DraftPrepared'
$draftOrder = New-P3Order -Context $context -Customer $draftCustomer -Deal $null -Key 'p13qa-p3-draft-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingQuote)
$draftMessage = New-P3Message -Context $context -Customer $draftCustomer -Order $draftOrder -Key 'p13qa-p3-draft-message-001'
$null = New-P3Suggestion -Context $context -Customer $draftCustomer -Order $draftOrder -Message $draftMessage -Key 'p13qa-p3-draft-suggestion-001' -Status ([Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared) -AutoReplyState 'prepared'

$waitingCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-waiting-001' -NameSuffix 'WaitingPayment'
$waitingOrder = New-P3Order -Context $context -Customer $waitingCustomer -Deal $null -Key 'p13qa-p3-waiting-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingFollowUp)
$waitingMessage = New-P3Message -Context $context -Customer $waitingCustomer -Order $waitingOrder -Key 'p13qa-p3-waiting-message-001'
$null = New-P3Suggestion -Context $context -Customer $waitingCustomer -Order $waitingOrder -Message $waitingMessage -Key 'p13qa-p3-waiting-suggestion-001' -Status ([Orderly.Core.Models.AiSuggestionStatus]::Sent) -AutoReplyState 'sent'

$paidCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-paid-001' -NameSuffix 'Paid'
$paidDeal = New-P3Deal -Context $context -Customer $paidCustomer -Key 'p13qa-p3-paid-deal-001' -Stage ([Orderly.Core.Models.DealStage]::Won)
$paidOrder = New-P3Order -Context $context -Customer $paidCustomer -Deal $paidDeal -Key 'p13qa-p3-paid-order-001' -Status ([Orderly.Core.Models.OrderStatus]::Won)

$fulfilledCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-fulfilled-001' -NameSuffix 'Fulfilled'
$fulfilledDeal = New-P3Deal -Context $context -Customer $fulfilledCustomer -Key 'p13qa-p3-fulfilled-deal-001' -Stage ([Orderly.Core.Models.DealStage]::Won)
$fulfilledOrder = New-P3Order -Context $context -Customer $fulfilledCustomer -Deal $fulfilledDeal -Key 'p13qa-p3-fulfilled-order-001' -Status ([Orderly.Core.Models.OrderStatus]::Closed)

$lostCustomer = New-P3Customer -Context $context -Key 'p13qa-p3-lost-001' -NameSuffix 'Lost'
$lostDeal = New-P3Deal -Context $context -Customer $lostCustomer -Key 'p13qa-p3-lost-deal-001' -Stage ([Orderly.Core.Models.DealStage]::Lost)
$lostOrder = New-P3Order -Context $context -Customer $lostCustomer -Deal $lostDeal -Key 'p13qa-p3-lost-order-001' -Status ([Orderly.Core.Models.OrderStatus]::PendingCommunication)

Write-Step 'Step 4/9: resolve pipeline stages'
$snapshotNew = $context.Resolver.ResolveAsync($newCustomer.Id).GetAwaiter().GetResult()
$snapshotFallback = $context.Resolver.ResolveAsync($fallbackCustomer.Id).GetAwaiter().GetResult()
$snapshotContact = $context.Resolver.ResolveAsync($contactCustomer.Id, $contactOrder.Id).GetAwaiter().GetResult()
$snapshotInterested = $context.Resolver.ResolveAsync($interestCustomer.Id, $interestOrder.Id).GetAwaiter().GetResult()
$snapshotQuoted = $context.Resolver.ResolveAsync($quoteCustomer.Id, $quoteOrder.Id).GetAwaiter().GetResult()
$snapshotDraft = $context.Resolver.ResolveAsync($draftCustomer.Id, $draftOrder.Id).GetAwaiter().GetResult()
$snapshotWaiting = $context.Resolver.ResolveAsync($waitingCustomer.Id, $waitingOrder.Id).GetAwaiter().GetResult()
$snapshotPaid = $context.Resolver.ResolveAsync($paidCustomer.Id, $paidOrder.Id).GetAwaiter().GetResult()
$snapshotFulfilled = $context.Resolver.ResolveAsync($fulfilledCustomer.Id, $fulfilledOrder.Id).GetAwaiter().GetResult()
$snapshotLost = $context.Resolver.ResolveAsync($lostCustomer.Id, $lostOrder.Id).GetAwaiter().GetResult()

Write-Step 'Step 5/9: assert expected stages and fallback'
Assert-Stage -Snapshot $snapshotNew -Expected 'New'
Assert-Stage -Snapshot $snapshotFallback -Expected 'New'
if (-not $snapshotFallback.UsedFallback) {
    throw 'Expected fallback snapshot to mark UsedFallback = true.'
}
Assert-Stage -Snapshot $snapshotContact -Expected 'Contacted'
Assert-Stage -Snapshot $snapshotInterested -Expected 'Interested'
Assert-Stage -Snapshot $snapshotQuoted -Expected 'Quoted'
Assert-Stage -Snapshot $snapshotDraft -Expected 'DraftPrepared'
Assert-Stage -Snapshot $snapshotWaiting -Expected 'WaitingPayment'
Assert-Stage -Snapshot $snapshotPaid -Expected 'Paid'
Assert-Stage -Snapshot $snapshotFulfilled -Expected 'Fulfilled'
Assert-Stage -Snapshot $snapshotLost -Expected 'Lost'

Write-Step 'Step 6/9: assert pipeline stage is not persisted to schema'
$connection = $context.ConnectionFactory.CreateConnection()
$connection.Open()
try {
    foreach ($tableName in @('Customers', 'Deals', 'Orders')) {
        $command = $connection.CreateCommand()
        $command.CommandText = "PRAGMA table_info($tableName);"
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                if ($reader.GetString(1) -eq 'PipelineStage') {
                    throw "Unexpected PipelineStage column detected in table: $tableName"
                }
            }
        }
        finally {
            $reader.Dispose()
            $command.Dispose()
        }
    }
}
finally {
    $connection.Dispose()
}

Write-Step 'Step 7/9: assert resolver does not mutate OrderStatus / DealStage'
$paidOrderAfter = $context.OrderRepository.GetByIdAsync($paidOrder.Id).GetAwaiter().GetResult()
$paidDealAfter = $context.DealRepository.GetByIdAsync($paidDeal.Id).GetAwaiter().GetResult()
$fulfilledOrderAfter = $context.OrderRepository.GetByIdAsync($fulfilledOrder.Id).GetAwaiter().GetResult()
$lostDealAfter = $context.DealRepository.GetByIdAsync($lostDeal.Id).GetAwaiter().GetResult()

if ($paidOrderAfter.Status -ne [Orderly.Core.Models.OrderStatus]::Won) {
    throw 'OrderStatus changed unexpectedly after pipeline resolution.'
}
if ($paidDealAfter.Stage -ne [Orderly.Core.Models.DealStage]::Won) {
    throw 'DealStage changed unexpectedly for paid scenario.'
}
if ($fulfilledOrderAfter.Status -ne [Orderly.Core.Models.OrderStatus]::Closed) {
    throw 'Fulfilled scenario order status changed unexpectedly.'
}
if ($lostDealAfter.Stage -ne [Orderly.Core.Models.DealStage]::Lost) {
    throw 'Lost scenario deal stage changed unexpectedly.'
}

Write-Step 'Step 8/9: reset QA data'
Invoke-QaScript -Path $resetScript

Write-Step 'Step 9/9: final pass'
Write-Host ''
Write-Host 'P3.2 PIPELINE SMOKE: PASS'
Write-Host ('Resolved stages: ' + (@(
            $snapshotNew.Stage,
            $snapshotFallback.Stage,
            $snapshotContact.Stage,
            $snapshotInterested.Stage,
            $snapshotQuoted.Stage,
            $snapshotDraft.Stage,
            $snapshotWaiting.Stage,
            $snapshotPaid.Stage,
            $snapshotFulfilled.Stage,
            $snapshotLost.Stage) -join ', '))
