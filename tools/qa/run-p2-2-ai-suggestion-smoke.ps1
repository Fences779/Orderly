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
        'Orderly.Data.dll'
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

function New-AiSuggestionServiceContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $aiAssistantService = [Orderly.Data.Services.LocalAiAssistantService]::new($messageRepository, $suggestionRepository, $activityRepository)

    return [pscustomobject]@{
        CustomerRepository = $customerRepository
        OrderRepository = $orderRepository
        MessageRepository = $messageRepository
        SuggestionRepository = $suggestionRepository
        ActivityRepository = $activityRepository
        AiAssistantService = $aiAssistantService
    }
}

function Get-QaAiContext {
    param(
        [Parameter(Mandatory = $true)]
        $Context
    )

    $customers = $Context.CustomerRepository.GetAllAsync().GetAwaiter().GetResult()
    $customer = $customers |
        Where-Object { $_.Name.Contains('[P1.3_QA]') -and $_.Name.Contains('客户-A') } |
        Select-Object -First 1

    if ($null -eq $customer) {
        throw 'QA customer context not found.'
    }

    $orders = $Context.OrderRepository.ListByCustomerIdAsync($customer.Id).GetAwaiter().GetResult()
    $order = $orders |
        Where-Object { $_.Title.Contains('[P1.3_QA]') -and $_.Title.Contains('订单-待处理') } |
        Select-Object -First 1

    if ($null -eq $order) {
        throw 'QA order context not found.'
    }

    $messages = $Context.MessageRepository.ListByOrderIdAsync($order.Id).GetAwaiter().GetResult()
    $message = $messages | Select-Object -First 1
    if ($null -eq $message) {
        throw 'QA message context not found.'
    }

    return [pscustomobject]@{
        Customer = $customer
        Order = $order
        Message = $message
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.2 AI suggestion smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/7: reset QA data"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/7: skip QA data reset"
}

Write-Step "Step 2/7: baseline QA status"
$baselineStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$baselineSuggestionCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA AiSuggestions count:'
$baselineActivityCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA ActivityLogs count:'
Write-Step "Baseline AiSuggestions count: $baselineSuggestionCount"
Write-Step "Baseline ActivityLogs count: $baselineActivityCount"

Write-Step "Step 3/7: generate a draft AI suggestion from existing conversation context"
Import-OrderlyAssemblies
$serviceContext = New-AiSuggestionServiceContext
$qaContext = Get-QaAiContext -Context $serviceContext
$generated = $serviceContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
    $qaContext.Customer.Id,
    $qaContext.Order.Id,
    $qaContext.Order.DealId,
    $qaContext.Message.Id).GetAwaiter().GetResult()

if ($generated.Id -le 0) {
    throw 'GenerateAndSaveReplySuggestionAsync did not return a persisted suggestion id.'
}

if ($generated.MessageId -ne $qaContext.Message.Id) {
    throw "Generated suggestion did not bind to the expected message. Expected=$($qaContext.Message.Id), Actual=$($generated.MessageId)"
}

if (-not $generated.SuggestionText.Contains('Local Stub')) {
    throw 'Generated suggestion did not include the expected Local Stub marker.'
}

Write-Step "Generated draft suggestion Id: $($generated.Id)"

Write-Step "Step 4/7: read back the generated suggestion and accept it"
$readBackSuggestions = $serviceContext.AiAssistantService.ListSuggestionsAsync($qaContext.Customer.Id, $qaContext.Order.Id).GetAwaiter().GetResult()
$acceptedTarget = $readBackSuggestions | Where-Object { $_.Id -eq $generated.Id } | Select-Object -First 1
if ($null -eq $acceptedTarget) {
    throw 'Generated suggestion was not returned by ListSuggestionsAsync.'
}

$accepted = $serviceContext.AiAssistantService.UpdateSuggestionStatusAsync(
    $generated.Id,
    [Orderly.Core.Models.AiSuggestionStatus]::Accepted,
    $qaContext.Order.DealId).GetAwaiter().GetResult()

if ($accepted.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::Accepted) {
    throw 'Accepted suggestion did not transition to Accepted.'
}

Write-Step "Accepted suggestion Id: $($accepted.Id)"

Write-Step "Step 5/7: generate another suggestion and reject it"
$rejectedDraft = $serviceContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
    $qaContext.Customer.Id,
    $qaContext.Order.Id,
    $qaContext.Order.DealId,
    $qaContext.Message.Id).GetAwaiter().GetResult()

$rejected = $serviceContext.AiAssistantService.UpdateSuggestionStatusAsync(
    $rejectedDraft.Id,
    [Orderly.Core.Models.AiSuggestionStatus]::Rejected,
    $qaContext.Order.DealId).GetAwaiter().GetResult()

if ($rejected.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::Rejected) {
    throw 'Rejected suggestion did not transition to Rejected.'
}

Write-Step "Rejected suggestion Id: $($rejected.Id)"

Write-Step "Step 6/7: verify ActivityLog records and QA status deltas"
$activities = $serviceContext.ActivityRepository.ListByCustomerIdAsync($qaContext.Customer.Id).GetAwaiter().GetResult()
$generatedNeedle = '"suggestionId":' + $generated.Id
$rejectedNeedle = '"suggestionId":' + $rejected.Id
$generatedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AiSuggestionGenerated -and $_.MetadataJson.Contains($generatedNeedle) })
$acceptedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AiSuggestionAccepted -and $_.MetadataJson.Contains($generatedNeedle) })
$rejectedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AiSuggestionRejected -and $_.MetadataJson.Contains($rejectedNeedle) })
$generatedSecondActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AiSuggestionGenerated -and $_.MetadataJson.Contains($rejectedNeedle) })

if ($generatedActivities.Count -lt 1 -or $generatedSecondActivities.Count -lt 1) {
    throw 'Missing generated AI suggestion activity log entry.'
}

if ($acceptedActivities.Count -lt 1) {
    throw 'Missing accepted AI suggestion activity log entry.'
}

if ($rejectedActivities.Count -lt 1) {
    throw 'Missing rejected AI suggestion activity log entry.'
}

$afterWriteStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$afterWriteSuggestionCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA AiSuggestions count:'
$afterWriteActivityCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA ActivityLogs count:'
if ($afterWriteSuggestionCount -lt ($baselineSuggestionCount + 2)) {
    throw "AiSuggestions count did not increase as expected. Baseline=$baselineSuggestionCount, AfterWrite=$afterWriteSuggestionCount"
}

if ($afterWriteActivityCount -lt ($baselineActivityCount + 4)) {
    throw "ActivityLogs count did not increase as expected. Baseline=$baselineActivityCount, AfterWrite=$afterWriteActivityCount"
}

Write-Step "After-write AiSuggestions count: $afterWriteSuggestionCount"
Write-Step "After-write ActivityLogs count: $afterWriteActivityCount"

Write-Step "Step 7/7: reset QA data again and verify baseline is restored"
Invoke-QaScript -Path $resetScript
$finalStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$finalSuggestionCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA AiSuggestions count:'
$finalActivityCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA ActivityLogs count:'
if ($finalSuggestionCount -ne $baselineSuggestionCount) {
    throw "AiSuggestions count did not restore after reset. Baseline=$baselineSuggestionCount, Final=$finalSuggestionCount"
}

if ($finalActivityCount -ne $baselineActivityCount) {
    throw "ActivityLogs count did not restore after reset. Baseline=$baselineActivityCount, Final=$finalActivityCount"
}

Write-Step "Final AiSuggestions count restored to: $finalSuggestionCount"
Write-Step "Final ActivityLogs count restored to: $finalActivityCount"
Write-Step "P2.2 AI suggestion smoke completed"
