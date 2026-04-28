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

function New-AutoReplyServiceContext {
    param(
        [Parameter(Mandatory = $true)]
        [Orderly.Data.Services.AiProviderOptions]$ProviderOptions
    )

    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $clipboardService = [Orderly.Infrastructure.Services.InMemoryClipboardService]::new()
    $localProvider = [Orderly.Data.Services.LocalAiSuggestionProvider]::new()
    $primaryProvider = [Orderly.Data.Services.AiSuggestionProviderFactory]::CreatePrimaryProvider($ProviderOptions, $localProvider)
    $aiAssistantService = [Orderly.Data.Services.LocalAiAssistantService]::new(
        $customerRepository,
        $orderRepository,
        $messageRepository,
        $suggestionRepository,
        $activityRepository,
        $primaryProvider,
        $localProvider,
        $ProviderOptions)
    $autoReplyService = [Orderly.Data.Services.LocalAutoReplyService]::new($suggestionRepository, $orderRepository, $activityRepository, $clipboardService)

    return [pscustomobject]@{
        CustomerRepository = $customerRepository
        OrderRepository = $orderRepository
        MessageRepository = $messageRepository
        SuggestionRepository = $suggestionRepository
        ActivityRepository = $activityRepository
        AiAssistantService = $aiAssistantService
        AutoReplyService = $autoReplyService
        ClipboardService = $clipboardService
    }
}

function Get-QaAutoReplyContext {
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
Write-Step "Starting P2.3 auto reply smoke"
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

Write-Step "Step 3/7: generate and prepare a local auto reply draft"
Import-OrderlyAssemblies
$providerOptions = [Orderly.Data.Services.AiProviderOptions]::new('local', $null, $null, $null, 15)
$serviceContext = New-AutoReplyServiceContext -ProviderOptions $providerOptions
$qaContext = Get-QaAutoReplyContext -Context $serviceContext
$draftSource = $serviceContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
    $qaContext.Customer.Id,
    $qaContext.Order.Id,
    $qaContext.Order.DealId,
    $qaContext.Message.Id).GetAwaiter().GetResult()

$prepared = $serviceContext.AutoReplyService.PrepareReplyAsync($draftSource.Id).GetAwaiter().GetResult()
if ($null -eq $prepared) {
    throw 'PrepareReplyAsync did not return a persisted draft.'
}

if ($prepared.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared) {
    throw 'Prepared suggestion did not transition to DraftPrepared.'
}

if (-not $prepared.SuggestionText.Contains('本地草稿')) {
    throw 'Prepared draft did not include the expected local draft marker.'
}

if (-not $prepared.MetadataJson.Contains('"state":"prepared"')) {
    throw 'Prepared draft metadata did not include the expected prepared state.'
}

$preparedReadBack = $serviceContext.SuggestionRepository.GetByIdAsync($prepared.Id).GetAwaiter().GetResult()
if ($null -eq $preparedReadBack -or $preparedReadBack.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared) {
    throw 'Prepared draft could not be read back from repository.'
}

Write-Step "Prepared draft suggestion Id: $($prepared.Id)"

Write-Step "Step 4/7: copy the prepared local draft and then mark it as sent"
[void]$serviceContext.AutoReplyService.CopyReplyDraftAsync($prepared.Id).GetAwaiter().GetResult()
$copied = $serviceContext.SuggestionRepository.GetByIdAsync($prepared.Id).GetAwaiter().GetResult()
if (-not $copied.MetadataJson.Contains('"state":"copied"')) {
    throw 'Copied draft metadata did not include the expected copied state.'
}

$clipboardText = $serviceContext.ClipboardService.LastText
if ([string]::IsNullOrWhiteSpace($clipboardText) -or $clipboardText.Contains('本地草稿 / 未发送')) {
    throw 'Copied draft text was not written to the in-memory clipboard as expected.'
}

[void]$serviceContext.AutoReplyService.MarkReplySentAsync($prepared.Id).GetAwaiter().GetResult()
$sent = $serviceContext.SuggestionRepository.GetByIdAsync($prepared.Id).GetAwaiter().GetResult()
if ($null -eq $sent -or $sent.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::Sent) {
    throw 'Prepared draft did not transition to Sent.'
}

if (-not $sent.MetadataJson.Contains('"state":"sent"')) {
    throw 'Sent draft metadata did not include the expected sent state.'
}

Write-Step "Sent draft suggestion Id: $($prepared.Id)"

Write-Step "Step 5/7: prepare another draft and reject it"
$rejectedSource = $serviceContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
    $qaContext.Customer.Id,
    $qaContext.Order.Id,
    $qaContext.Order.DealId,
    $qaContext.Message.Id).GetAwaiter().GetResult()

$rejectedPrepared = $serviceContext.AutoReplyService.PrepareReplyAsync($rejectedSource.Id).GetAwaiter().GetResult()
[void]$serviceContext.AutoReplyService.MarkReplyRejectedAsync($rejectedPrepared.Id).GetAwaiter().GetResult()
$rejected = $serviceContext.SuggestionRepository.GetByIdAsync($rejectedPrepared.Id).GetAwaiter().GetResult()
if ($null -eq $rejected -or $rejected.Status -ne [Orderly.Core.Models.AiSuggestionStatus]::Rejected) {
    throw 'Prepared draft did not transition to Rejected.'
}

if (-not $rejected.MetadataJson.Contains('"state":"rejected"')) {
    throw 'Rejected draft metadata did not include the expected rejected state.'
}

Write-Step "Rejected draft suggestion Id: $($rejected.Id)"

Write-Step "Step 6/7: verify ActivityLog records and QA status deltas"
$activities = $serviceContext.ActivityRepository.ListByCustomerIdAsync($qaContext.Customer.Id).GetAwaiter().GetResult()
$sentNeedle = '"suggestionId":' + $prepared.Id
$rejectedNeedle = '"suggestionId":' + $rejected.Id
$preparedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AutoReplyDraftPrepared -and $_.MetadataJson.Contains($sentNeedle) })
$copiedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AutoReplyDraftCopied -and $_.MetadataJson.Contains($sentNeedle) })
$sentActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AutoReplySent -and $_.MetadataJson.Contains($sentNeedle) })
$rejectedPreparedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AutoReplyDraftPrepared -and $_.MetadataJson.Contains($rejectedNeedle) })
$rejectedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::AutoReplyDraftRejected -and $_.MetadataJson.Contains($rejectedNeedle) })

if ($preparedActivities.Count -lt 1 -or $rejectedPreparedActivities.Count -lt 1) {
    throw 'Missing prepared draft activity log entry.'
}

if ($sentActivities.Count -lt 1) {
    throw 'Missing sent draft activity log entry.'
}

if ($copiedActivities.Count -lt 1) {
    throw 'Missing copied draft activity log entry.'
}

if ($rejectedActivities.Count -lt 1) {
    throw 'Missing rejected draft activity log entry.'
}

$afterWriteStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$afterWriteSuggestionCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA AiSuggestions count:'
$afterWriteActivityCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA ActivityLogs count:'
if ($afterWriteSuggestionCount -lt ($baselineSuggestionCount + 2)) {
    throw "AiSuggestions count did not increase as expected. Baseline=$baselineSuggestionCount, AfterWrite=$afterWriteSuggestionCount"
}

if ($afterWriteActivityCount -lt ($baselineActivityCount + 7)) {
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
Write-Step "P2.3 auto reply smoke completed"
