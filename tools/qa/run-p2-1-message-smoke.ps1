param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'
$statusScript = Join-Path $PSScriptRoot 'run-qa-data-status.ps1'

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

function Get-ConversationCountFromStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Output
    )

    $match = [regex]::Match($Output, 'QA ConversationMessages count:\s*(\d+)')
    if (-not $match.Success) {
        throw "Unable to parse ConversationMessages count from status output."
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

function New-ConversationServiceContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory)
    $conversationService = [Orderly.Data.Services.ConversationService]::new($messageRepository, $activityRepository)

    return [pscustomobject]@{
        CustomerRepository = $customerRepository
        OrderRepository = $orderRepository
        ConversationService = $conversationService
    }
}

function Get-QaConversationContext {
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

    return [pscustomobject]@{
        Customer = $customer
        Order = $order
    }
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.1 message smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/6: reset QA data"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/6: skip QA data reset"
}

Write-Step "Step 2/6: baseline QA status"
$baselineStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$baselineConversationCount = Get-ConversationCountFromStatus -Output $baselineStatus.StdOut
Write-Step "Baseline ConversationMessages count: $baselineConversationCount"

Write-Step "Step 3/6: insert one manual conversation message through ConversationService"
Import-OrderlyAssemblies
$serviceContext = New-ConversationServiceContext
$qaContext = Get-QaConversationContext -Context $serviceContext
$runtimeMarker = "[P2_QA] P2.1 runtime message $(Get-Date -Format 'yyyyMMddHHmmss')"
$message = [Orderly.Core.Models.ConversationMessage]::new()
$message.CustomerId = $qaContext.Customer.Id
$message.OrderId = $qaContext.Order.Id
$message.DealId = $qaContext.Order.DealId
$message.Direction = [Orderly.Core.Models.MessageDirection]::Incoming
$message.Channel = [Orderly.Core.Models.MessageChannel]::Manual
$message.SenderName = '[P2_QA] QA 手工录入'
$message.Content = $runtimeMarker
$message.MetadataJson = '{"source":"qa-script","stage":"p2.1"}'
$created = $serviceContext.ConversationService.SaveMessageAsync($message).GetAwaiter().GetResult()

if ($created.Id -le 0) {
    throw 'ConversationService did not return a persisted message id.'
}

Write-Step "Inserted ConversationMessage Id: $($created.Id)"

Write-Step "Step 4/6: verify recent messages can read back the inserted message"
$recentMessages = $serviceContext.ConversationService.ListByOrderAsync($qaContext.Order.Id).GetAwaiter().GetResult()
$matchedMessage = $recentMessages | Where-Object { $_.Id -eq $created.Id -and $_.Content -eq $runtimeMarker } | Select-Object -First 1
if ($null -eq $matchedMessage) {
    throw 'Inserted message was not returned by ListByOrderAsync.'
}

Write-Step "Read-back verified for order Id: $($qaContext.Order.Id)"

Write-Step "Step 5/6: status command reflects the new message count"
$afterWriteStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$afterWriteConversationCount = Get-ConversationCountFromStatus -Output $afterWriteStatus.StdOut
if ($afterWriteConversationCount -lt ($baselineConversationCount + 1)) {
    throw "ConversationMessages count did not increase as expected. Baseline=$baselineConversationCount, AfterWrite=$afterWriteConversationCount"
}

Write-Step "After-write ConversationMessages count: $afterWriteConversationCount"

Write-Step "Step 6/6: reset QA data again and verify baseline is restored"
Invoke-QaScript -Path $resetScript
$finalStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
$finalConversationCount = Get-ConversationCountFromStatus -Output $finalStatus.StdOut
if ($finalConversationCount -ne $baselineConversationCount) {
    throw "ConversationMessages count did not restore after reset. Baseline=$baselineConversationCount, Final=$finalConversationCount"
}

Write-Step "Final ConversationMessages count restored to: $finalConversationCount"
Write-Step "P2.1 message smoke completed"
