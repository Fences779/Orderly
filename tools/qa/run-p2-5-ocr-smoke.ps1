param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'
$localFallbackText = '【本地OCR占位】请人工确认截图内容后转为沟通记录。'

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

function New-OcrServiceContext {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory, $fieldEncryptionService)
    $ocrRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $conversationService = [Orderly.Data.Services.ConversationService]::new($messageRepository, $activityRepository)
    $ocrService = [Orderly.Data.Services.LocalOcrService]::new($ocrRepository, $activityRepository, $conversationService, $messageRepository)

    return [pscustomobject]@{
        CustomerRepository = $customerRepository
        OrderRepository = $orderRepository
        ConversationRepository = $messageRepository
        OcrRepository = $ocrRepository
        ActivityRepository = $activityRepository
        ConversationService = $conversationService
        OcrService = $ocrService
    }
}

function Get-QaOcrContext {
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

function Get-MetadataValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$MetadataJson,
        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    $metadata = $MetadataJson | ConvertFrom-Json -AsHashtable
    return $metadata[$Key]
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.5 OCR smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

$tempImagePath = Join-Path ([System.IO.Path]::GetTempPath()) ("[P2_QA]-p2.5-ocr-" + [guid]::NewGuid().ToString('N') + ".png")

try {
    if (-not $SkipReset) {
        Write-Step "Step 1/8: reset QA data"
        Invoke-QaScript -Path $resetScript
    } else {
        Write-Step "Step 1/8: skip QA data reset"
    }

    Write-Step "Step 2/8: baseline QA status"
    $baselineStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
    $baselineOcrCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA OcrResults count:'
    $baselineConversationCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA ConversationMessages count:'
    $baselineActivityCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA ActivityLogs count:'
    Write-Step "Baseline OcrResults count: $baselineOcrCount"
    Write-Step "Baseline ConversationMessages count: $baselineConversationCount"
    Write-Step "Baseline ActivityLogs count: $baselineActivityCount"

    Write-Step "Step 3/8: create a temporary local image placeholder and import assemblies"
    Set-Content -LiteralPath $tempImagePath -Value 'p2.5 qa local image placeholder' -Encoding utf8
    Import-OrderlyAssemblies
    $serviceContext = New-OcrServiceContext
    $qaContext = Get-QaOcrContext -Context $serviceContext

    Write-Step "Step 4/8: create OCR task and complete it with local fallback text"
    $ocrMetadata = @{
        source = 'manual-image'
        createdBy = 'p2.5'
        provider = 'local'
        usedFallback = $true
        fileName = [System.IO.Path]::GetFileName($tempImagePath)
        fileExists = $true
    } | ConvertTo-Json -Compress

    $ocrResult = [Orderly.Core.Models.OcrResult]::new()
    $ocrResult.CustomerId = $qaContext.Customer.Id
    $ocrResult.OrderId = $qaContext.Order.Id
    $ocrResult.SourcePath = $tempImagePath
    $ocrResult.SourceName = "[P2_QA] $([System.IO.Path]::GetFileName($tempImagePath))"
    $ocrResult.MetadataJson = $ocrMetadata

    $created = $serviceContext.OcrService.CreateOcrTaskAsync($ocrResult).GetAwaiter().GetResult()
    if ($created.Id -le 0 -or $created.Status -ne [Orderly.Core.Models.OcrStatus]::Pending) {
        throw 'CreateOcrTaskAsync did not persist a Pending OCR task.'
    }

    $completed = $serviceContext.OcrService.CompleteOcrTaskAsync($created.Id, $localFallbackText).GetAwaiter().GetResult()
    if ($null -eq $completed -or $completed.Status -ne [Orderly.Core.Models.OcrStatus]::Completed) {
        throw 'CompleteOcrTaskAsync did not transition OCR task to Completed.'
    }

    if ($completed.ExtractedText -ne $localFallbackText) {
        throw 'Completed OCR task did not keep the expected local fallback text.'
    }

    Write-Step "Completed OCR task Id: $($completed.Id)"

    Write-Step "Step 5/8: verify OCR result readback and metadata"
    $readBackOcr = $serviceContext.OcrRepository.GetByIdAsync($completed.Id).GetAwaiter().GetResult()
    if ($null -eq $readBackOcr) {
        throw 'Persisted OCR result could not be read back.'
    }

    if ((Get-MetadataValue -MetadataJson $readBackOcr.MetadataJson -Key 'provider') -ne 'local') {
        throw 'OCR metadata missing provider=local.'
    }

    if (-not [bool](Get-MetadataValue -MetadataJson $readBackOcr.MetadataJson -Key 'usedFallback')) {
        throw 'OCR metadata missing usedFallback=true.'
    }

    if ((Get-MetadataValue -MetadataJson $readBackOcr.MetadataJson -Key 'createdBy') -ne 'p2.5') {
        throw 'OCR metadata missing createdBy=p2.5.'
    }

    if ((Get-MetadataValue -MetadataJson $readBackOcr.MetadataJson -Key 'source') -ne 'manual-image') {
        throw 'OCR metadata missing source=manual-image.'
    }

    Write-Step "Step 6/8: convert OCR text into ConversationMessage and verify idempotent readback"
    $message = $serviceContext.OcrService.ConvertToConversationMessageAsync(
        $completed.Id,
        $qaContext.Customer.Name,
        $qaContext.Order.DealId).GetAwaiter().GetResult()

    if ($message.Id -le 0) {
        throw 'ConvertToConversationMessageAsync did not persist a ConversationMessage.'
    }

    $readBackMessage = $serviceContext.ConversationRepository.GetBySourceMessageIdAsync("ocr-result:$($completed.Id)").GetAwaiter().GetResult()
    if ($null -eq $readBackMessage -or $readBackMessage.Id -ne $message.Id) {
        throw 'ConversationMessage could not be read back by OCR source message id.'
    }

    $readBackCompleted = $serviceContext.OcrRepository.GetByIdAsync($completed.Id).GetAwaiter().GetResult()
    $convertedMessageId = [int](Get-MetadataValue -MetadataJson $readBackCompleted.MetadataJson -Key 'convertedToMessageId')
    if ($convertedMessageId -ne $message.Id) {
        throw "OCR metadata missing convertedToMessageId=$($message.Id)."
    }

    Write-Step "Converted ConversationMessage Id: $($message.Id)"

    Write-Step "Step 7/8: verify ActivityLog and QA status deltas"
    $activities = $serviceContext.ActivityRepository.ListByCustomerIdAsync($qaContext.Customer.Id).GetAwaiter().GetResult()
    $ocrCreatedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::OcrTaskCreated -and $_.Description.Contains($readBackCompleted.SourceName) })
    $ocrCompletedActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::OcrTaskCompleted -and $_.Description.Contains($readBackCompleted.SourceName) })
    $messageActivities = @($activities | Where-Object { $_.Type -eq [Orderly.Core.Models.ActivityType]::ConversationMessageAdded -and $_.Description.Contains('Incoming / Manual') -and $_.MetadataJson.Contains('"qa"') })

    if ($ocrCreatedActivities.Count -lt 1) {
        throw 'Missing OcrTaskCreated activity log entry.'
    }

    if ($ocrCompletedActivities.Count -lt 1) {
        throw 'Missing OcrTaskCompleted activity log entry.'
    }

    if ($messageActivities.Count -lt 1) {
        throw 'Missing ConversationMessageAdded activity log entry for OCR conversion.'
    }

    $afterWriteStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
    $afterWriteOcrCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA OcrResults count:'
    $afterWriteConversationCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA ConversationMessages count:'
    $afterWriteActivityCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA ActivityLogs count:'
    if ($afterWriteOcrCount -lt ($baselineOcrCount + 1)) {
        throw "OcrResults count did not increase as expected. Baseline=$baselineOcrCount, AfterWrite=$afterWriteOcrCount"
    }

    if ($afterWriteConversationCount -lt ($baselineConversationCount + 1)) {
        throw "ConversationMessages count did not increase as expected. Baseline=$baselineConversationCount, AfterWrite=$afterWriteConversationCount"
    }

    Write-Step "After-write OcrResults count: $afterWriteOcrCount"
    Write-Step "After-write ConversationMessages count: $afterWriteConversationCount"
    Write-Step "After-write ActivityLogs count: $afterWriteActivityCount"

    Write-Step "Step 8/8: reset QA data again and verify baseline is restored"
    Invoke-QaScript -Path $resetScript
    $finalStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
    $finalOcrCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA OcrResults count:'
    $finalConversationCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA ConversationMessages count:'
    $finalActivityCount = Get-CountFromStatus -Output $finalStatus.StdOut -Label 'QA ActivityLogs count:'
    if ($finalOcrCount -ne $baselineOcrCount) {
        throw "OcrResults count did not restore after reset. Baseline=$baselineOcrCount, Final=$finalOcrCount"
    }

    if ($finalConversationCount -ne $baselineConversationCount) {
        throw "ConversationMessages count did not restore after reset. Baseline=$baselineConversationCount, Final=$finalConversationCount"
    }

    if ($finalActivityCount -ne $baselineActivityCount) {
        throw "ActivityLogs count did not restore after reset. Baseline=$baselineActivityCount, Final=$finalActivityCount"
    }

    Write-Step "Final OcrResults count restored to: $finalOcrCount"
    Write-Step "Final ConversationMessages count restored to: $finalConversationCount"
    Write-Step "Final ActivityLogs count restored to: $finalActivityCount"
    Write-Step "P2.5 OCR smoke completed"
}
finally {
    Remove-Item -LiteralPath $tempImagePath -ErrorAction SilentlyContinue
}
