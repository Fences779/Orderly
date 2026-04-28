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

function Get-PreviousAiEnv {
    return [pscustomobject]@{
        Provider = $env:ORDERLY_AI_PROVIDER
        BaseUrl = $env:ORDERLY_AI_BASE_URL
        ApiKey = $env:ORDERLY_AI_API_KEY
        DeepSeekApiKey = $env:DEEPSEEK_API_KEY
        Model = $env:ORDERLY_AI_MODEL
        TimeoutSeconds = $env:ORDERLY_AI_TIMEOUT_SECONDS
    }
}

function Set-AiEnv {
    param(
        [string]$Provider,
        [string]$BaseUrl,
        [string]$ApiKey,
        [string]$DeepSeekApiKey,
        [string]$Model,
        [string]$TimeoutSeconds
    )

    $env:ORDERLY_AI_PROVIDER = $Provider
    $env:ORDERLY_AI_BASE_URL = $BaseUrl
    $env:ORDERLY_AI_API_KEY = $ApiKey
    $env:DEEPSEEK_API_KEY = $DeepSeekApiKey
    $env:ORDERLY_AI_MODEL = $Model
    $env:ORDERLY_AI_TIMEOUT_SECONDS = $TimeoutSeconds
}

function Restore-AiEnv {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot
    )

    $env:ORDERLY_AI_PROVIDER = $Snapshot.Provider
    $env:ORDERLY_AI_BASE_URL = $Snapshot.BaseUrl
    $env:ORDERLY_AI_API_KEY = $Snapshot.ApiKey
    $env:DEEPSEEK_API_KEY = $Snapshot.DeepSeekApiKey
    $env:ORDERLY_AI_MODEL = $Snapshot.Model
    $env:ORDERLY_AI_TIMEOUT_SECONDS = $Snapshot.TimeoutSeconds
}

Assert-NoRunningOrderlyProcess
Write-Step "Starting P2.4 AI provider smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

$previousEnv = Get-PreviousAiEnv

try {
    if (-not $SkipReset) {
        Write-Step "Step 1/8: reset QA data"
        Invoke-QaScript -Path $resetScript
    } else {
        Write-Step "Step 1/8: skip QA data reset"
    }

    Write-Step "Step 2/8: baseline QA status"
    $baselineStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
    $baselineSuggestionCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA AiSuggestions count:'
    $baselineActivityCount = Get-CountFromStatus -Output $baselineStatus.StdOut -Label 'QA ActivityLogs count:'
    Write-Step "Baseline AiSuggestions count: $baselineSuggestionCount"
    Write-Step "Baseline ActivityLogs count: $baselineActivityCount"

    Write-Step "Step 3/8: no environment variables should use Local Stub"
    Import-OrderlyAssemblies
    Set-AiEnv -Provider '' -BaseUrl '' -ApiKey '' -DeepSeekApiKey '' -Model '' -TimeoutSeconds ''
    $localOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $localContext = New-AiSuggestionServiceContext -ProviderOptions $localOptions
    $qaContext = Get-QaAiContext -Context $localContext
    $localSuggestion = $localContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
        $qaContext.Customer.Id,
        $qaContext.Order.Id,
        $qaContext.Order.DealId,
        $qaContext.Message.Id).GetAwaiter().GetResult()

    if ((Get-MetadataValue -MetadataJson $localSuggestion.MetadataJson -Key 'provider') -ne 'local-stub') {
        throw 'Local environment did not use local-stub provider.'
    }

    if ([bool](Get-MetadataValue -MetadataJson $localSuggestion.MetadataJson -Key 'usedFallback')) {
        throw 'Local environment should not mark usedFallback=true.'
    }

    Write-Step "Local Stub suggestion Id: $($localSuggestion.Id)"

    Write-Step "Step 4/8: missing OpenAI-compatible configuration should fallback without crashing"
    Set-AiEnv -Provider 'openai-compatible' -BaseUrl '' -ApiKey '' -DeepSeekApiKey '' -Model '' -TimeoutSeconds '15'
    $missingConfigOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $missingConfigContext = New-AiSuggestionServiceContext -ProviderOptions $missingConfigOptions
    $missingConfigSuggestion = $missingConfigContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
        $qaContext.Customer.Id,
        $qaContext.Order.Id,
        $qaContext.Order.DealId,
        $qaContext.Message.Id).GetAwaiter().GetResult()

    if ((Get-MetadataValue -MetadataJson $missingConfigSuggestion.MetadataJson -Key 'provider') -ne 'local-stub') {
        throw 'Missing provider configuration did not fallback to local-stub.'
    }

    if (-not [bool](Get-MetadataValue -MetadataJson $missingConfigSuggestion.MetadataJson -Key 'usedFallback')) {
        throw 'Missing provider configuration should mark usedFallback=true.'
    }

    if ([string]::IsNullOrWhiteSpace([string](Get-MetadataValue -MetadataJson $missingConfigSuggestion.MetadataJson -Key 'errorSummary'))) {
        throw 'Missing provider configuration did not capture errorSummary.'
    }

    Write-Step "Missing-config fallback suggestion Id: $($missingConfigSuggestion.Id)"

    Write-Step "Step 5/8: missing DeepSeek key should fallback without crashing"
    Set-AiEnv -Provider 'deepseek' -BaseUrl '' -ApiKey '' -DeepSeekApiKey '' -Model '' -TimeoutSeconds '15'
    $missingDeepSeekOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $missingDeepSeekContext = New-AiSuggestionServiceContext -ProviderOptions $missingDeepSeekOptions
    $missingDeepSeekSuggestion = $missingDeepSeekContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
        $qaContext.Customer.Id,
        $qaContext.Order.Id,
        $qaContext.Order.DealId,
        $qaContext.Message.Id).GetAwaiter().GetResult()

    if ((Get-MetadataValue -MetadataJson $missingDeepSeekSuggestion.MetadataJson -Key 'provider') -ne 'local-stub') {
        throw 'Missing DeepSeek key did not fallback to local-stub.'
    }

    if (-not [bool](Get-MetadataValue -MetadataJson $missingDeepSeekSuggestion.MetadataJson -Key 'usedFallback')) {
        throw 'Missing DeepSeek key should mark usedFallback=true.'
    }

    $deepSeekErrorSummary = [string](Get-MetadataValue -MetadataJson $missingDeepSeekSuggestion.MetadataJson -Key 'errorSummary')
    if ([string]::IsNullOrWhiteSpace($deepSeekErrorSummary) -or -not $deepSeekErrorSummary.Contains('DEEPSEEK_API_KEY')) {
        throw 'Missing DeepSeek key did not capture DEEPSEEK_API_KEY errorSummary.'
    }

    Write-Step "DeepSeek missing-key fallback suggestion Id: $($missingDeepSeekSuggestion.Id)"

    Write-Step "Step 6/8: simulated provider failure should fallback to Local Stub"
    Set-AiEnv -Provider 'openai-compatible' -BaseUrl 'http://127.0.0.1:9/v1' -ApiKey 'placeholder-key' -DeepSeekApiKey '' -Model 'placeholder-model' -TimeoutSeconds '1'
    $failingOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $failingContext = New-AiSuggestionServiceContext -ProviderOptions $failingOptions
    $failingSuggestion = $failingContext.AiAssistantService.GenerateAndSaveReplySuggestionAsync(
        $qaContext.Customer.Id,
        $qaContext.Order.Id,
        $qaContext.Order.DealId,
        $qaContext.Message.Id).GetAwaiter().GetResult()

    if ((Get-MetadataValue -MetadataJson $failingSuggestion.MetadataJson -Key 'provider') -ne 'local-stub') {
        throw 'Simulated provider failure did not fallback to local-stub.'
    }

    if (-not [bool](Get-MetadataValue -MetadataJson $failingSuggestion.MetadataJson -Key 'usedFallback')) {
        throw 'Simulated provider failure should mark usedFallback=true.'
    }

    if ((Get-MetadataValue -MetadataJson $failingSuggestion.MetadataJson -Key 'createdBy') -ne 'p2.4') {
        throw 'MetadataJson missing createdBy=p2.4.'
    }

    if ([int](Get-MetadataValue -MetadataJson $failingSuggestion.MetadataJson -Key 'contextMessageCount') -lt 1) {
        throw 'MetadataJson missing valid contextMessageCount.'
    }

    Write-Step "Failure-fallback suggestion Id: $($failingSuggestion.Id)"

    Write-Step "Step 7/8: verify AiSuggestion readback and ActivityLog records"
    $readBackSuggestions = $failingContext.AiAssistantService.ListSuggestionsAsync($qaContext.Customer.Id, $qaContext.Order.Id).GetAwaiter().GetResult()
    if (@($readBackSuggestions | Where-Object { $_.Id -eq $failingSuggestion.Id }).Count -lt 1) {
        throw 'Fallback suggestion could not be read back from ListSuggestionsAsync.'
    }

    $activities = $failingContext.ActivityRepository.ListByCustomerIdAsync($qaContext.Customer.Id).GetAwaiter().GetResult()
    $localNeedle = '"suggestionId":' + $localSuggestion.Id
    $missingConfigNeedle = '"suggestionId":' + $missingConfigSuggestion.Id
    $missingDeepSeekNeedle = '"suggestionId":' + $missingDeepSeekSuggestion.Id
    $failingNeedle = '"suggestionId":' + $failingSuggestion.Id
    $generatedActivities = @(
        $activities | Where-Object {
            $_.Type -eq [Orderly.Core.Models.ActivityType]::AiSuggestionGenerated `
                -and ($_.MetadataJson.Contains($localNeedle) -or $_.MetadataJson.Contains($missingConfigNeedle) -or $_.MetadataJson.Contains($missingDeepSeekNeedle) -or $_.MetadataJson.Contains($failingNeedle))
        }
    )

    if ($generatedActivities.Count -lt 4) {
        throw 'Missing AiSuggestionGenerated activity log entries for provider smoke scenarios.'
    }

    $afterWriteStatus = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')
    $afterWriteSuggestionCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA AiSuggestions count:'
    $afterWriteActivityCount = Get-CountFromStatus -Output $afterWriteStatus.StdOut -Label 'QA ActivityLogs count:'
    if ($afterWriteSuggestionCount -lt ($baselineSuggestionCount + 4)) {
        throw "AiSuggestions count did not increase as expected. Baseline=$baselineSuggestionCount, AfterWrite=$afterWriteSuggestionCount"
    }

    if ($afterWriteActivityCount -lt ($baselineActivityCount + 4)) {
        throw "ActivityLogs count did not increase as expected. Baseline=$baselineActivityCount, AfterWrite=$afterWriteActivityCount"
    }

    Write-Step "After-write AiSuggestions count: $afterWriteSuggestionCount"
    Write-Step "After-write ActivityLogs count: $afterWriteActivityCount"

    Write-Step "Step 8/8: reset QA data again and verify baseline is restored"
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
    Write-Step "P2.4 AI provider smoke completed"
}
finally {
    Restore-AiEnv -Snapshot $previousEnv
}
