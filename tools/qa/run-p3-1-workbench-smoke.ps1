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

function New-WorkbenchContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory)
    $ocrResultRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory)
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

    return [pscustomobject]@{
        CustomerRepository        = $customerRepository
        OrderRepository           = $orderRepository
        MessageRepository         = $messageRepository
        SuggestionRepository      = $suggestionRepository
        OcrResultRepository       = $ocrResultRepository
        WorkbenchTaskService      = $workbenchTaskService
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

    $ocrResult = $Context.OcrResultRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult() |
        Select-Object -First 1
    if ($null -eq $ocrResult) {
        throw 'QA OCR result not found.'
    }

    return [pscustomobject]@{
        Customer  = $customer
        Order     = $order
        Message   = $message
        OcrResult = $ocrResult
    }
}

function Assert-ContainsTaskType {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IEnumerable]$Tasks,
        [Parameter(Mandatory = $true)]
        [string]$TypeName
    )

    $matched = @($Tasks | Where-Object { $_.Type.ToString() -eq $TypeName })
    if ($matched.Count -eq 0) {
        throw "Expected workbench task type not found: $TypeName"
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3.1 workbench smoke'
Write-Step 'Scope: local-only projection, no public network, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step 'Step 1/8: reset QA data'
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step 'Step 1/8: skip QA data reset'
}

Write-Step 'Step 2/8: capture baseline QA status'
$baselineStatus = Get-BaselineStatusText

Write-Step 'Step 3/8: import assemblies and prepare service context'
Import-OrderlyAssemblies
$context = New-WorkbenchContext
$target = Get-QaWorkbenchTarget -Context $context

Write-Step 'Step 4/8: create a copied draft and a completed OCR result without conversion'
$draftSuggestion = [Orderly.Core.Models.AiSuggestion]::new()
$draftSuggestion.CustomerId = $target.Customer.Id
$draftSuggestion.OrderId = $target.Order.Id
$draftSuggestion.MessageId = $target.Message.Id
$draftSuggestion.SuggestionText = '[P1.3_QA] P3 copied draft smoke'
$draftSuggestion.Reason = '[P1.3_QA] P3 draft not sent smoke'
$draftSuggestion.Status = [Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared
$draftSuggestion.RemoteId = 'p13qa-p3-workbench-draft-001'
$draftSuggestion.MetadataJson = New-QaMetadataJson -Key 'p13qa-p3-workbench-draft-001' -Extra @{
    autoReply = @{
        mode       = 'local-draft'
        state      = 'copied'
        localOnly  = $true
        copiedBy   = 'p3.1-smoke'
    }
}
$null = $context.SuggestionRepository.CreateAsync($draftSuggestion).GetAwaiter().GetResult()

$target.OcrResult.Status = [Orderly.Core.Models.OcrStatus]::Completed
$target.OcrResult.ExtractedText = '[P1.3_QA] P3 OCR completed smoke text'
$target.OcrResult.ErrorMessage = ''
$target.OcrResult.MetadataJson = New-QaMetadataJson -Key 'p2qa-ocr-001' -Extra @{
    provider = 'local'
}
[void]$context.OcrResultRepository.UpdateAsync($target.OcrResult).GetAwaiter().GetResult()

Write-Step 'Step 5/8: generate workbench tasks and validate required task types'
$tasks = $context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult()
if ($tasks.Count -lt 5) {
    throw "Expected at least 5 workbench tasks after projection, actual: $($tasks.Count)"
}

Assert-ContainsTaskType -Tasks $tasks -TypeName 'DraftNotSent'
Assert-ContainsTaskType -Tasks $tasks -TypeName 'AiSuggestionPending'
Assert-ContainsTaskType -Tasks $tasks -TypeName 'OcrNotConverted'
Assert-ContainsTaskType -Tasks $tasks -TypeName 'FollowUpToday'
Assert-ContainsTaskType -Tasks $tasks -TypeName 'FollowUpOverdue'

$taskTypeOrder = @($tasks | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
$expectedRelativeOrder = @('FollowUpOverdue', 'DraftNotSent', 'ReplyNeeded', 'AiSuggestionPending', 'OcrNotConverted', 'FollowUpToday')
for ($index = 0; $index -lt $expectedRelativeOrder.Count - 1; $index++) {
    $left = $taskTypeOrder.IndexOf($expectedRelativeOrder[$index])
    $right = $taskTypeOrder.IndexOf($expectedRelativeOrder[$index + 1])
    if ($left -lt 0 -or $right -lt 0 -or $left -gt $right) {
        throw "Unexpected workbench task sort order around $($expectedRelativeOrder[$index]) -> $($expectedRelativeOrder[$index + 1])"
    }
}

Write-Step 'Step 6/8: validate sort stability'
$secondPass = $context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult()
$firstIds = @($tasks | Select-Object -ExpandProperty Id)
$secondIds = @($secondPass | Select-Object -ExpandProperty Id)
if (($firstIds -join '|') -ne ($secondIds -join '|')) {
    throw 'Workbench task order is not stable between repeated reads.'
}

Write-Step 'Step 7/8: reset QA data and ensure baseline is restored'
Invoke-QaScript -Path $resetScript
$restoredStatus = Get-BaselineStatusText
if ($baselineStatus -ne $restoredStatus) {
    throw 'QA baseline status changed after reset-qa-data.'
}

Write-Step 'Step 8/8: final pass'
Write-Host ''
Write-Host 'P3.1 WORKBENCH SMOKE: PASS'
Write-Host ('Task count after projection: ' + $tasks.Count)
Write-Host ('Task order: ' + ($taskTypeOrder -join ', '))
