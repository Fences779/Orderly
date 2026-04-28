param()

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

function Import-OrderlyProviderAssemblies {
    $binRoot = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows')
    $assemblyNames = @(
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
}

function Use-UserEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $processValue = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($processValue)) {
        return $processValue
    }

    $userValue = [Environment]::GetEnvironmentVariable($Name, 'User')
    if (-not [string]::IsNullOrWhiteSpace($userValue)) {
        [Environment]::SetEnvironmentVariable($Name, $userValue, 'Process')
        return $userValue
    }

    return $null
}

function Get-MaskedSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value.Length -le 8) {
        return '*' * $Value.Length
    }

    return $Value.Substring(0, 3) + ('*' * ($Value.Length - 7)) + $Value.Substring($Value.Length - 4)
}

function New-MinimalDeepSeekRequest {
    $message = [Orderly.Core.Models.AiSuggestionContextMessage]::new()
    $message.RoleLabel = '客户'
    $message.SenderName = 'QA'
    $message.Content = '只回复：DeepSeek OK'
    $message.MessageTime = [DateTime]::UtcNow

    $request = [Orderly.Core.Models.AiSuggestionRequest]::new()
    $request.CustomerName = 'QA'
    $request.CustomerRemark = 'P2.5 DeepSeek live smoke'
    $request.OrderTitle = 'Smoke'
    $request.OrderStatusText = '待沟通'
    $request.FocusMessage = '只回复：DeepSeek OK'
    $request.RecentMessages = [Orderly.Core.Models.AiSuggestionContextMessage[]]@($message)

    return $request
}

Write-Step "Starting P2.5 DeepSeek live smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

$provider = Use-UserEnvironmentVariable -Name 'ORDERLY_AI_PROVIDER'
$model = Use-UserEnvironmentVariable -Name 'ORDERLY_AI_MODEL'
$apiKey = Use-UserEnvironmentVariable -Name 'DEEPSEEK_API_KEY'

if ([string]::IsNullOrWhiteSpace($provider)) {
    throw 'ORDERLY_AI_PROVIDER is missing in both process and user environment.'
}

if ([string]::IsNullOrWhiteSpace($model)) {
    throw 'ORDERLY_AI_MODEL is missing in both process and user environment.'
}

if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw 'DEEPSEEK_API_KEY is missing in both process and user environment.'
}

Import-OrderlyProviderAssemblies

$normalizedProvider = [Orderly.Data.Services.AiProviderOptions]::NormalizeProvider($provider)
if ($normalizedProvider -ne [Orderly.Data.Services.AiProviderOptions]::DeepSeekProviderName) {
    throw "ORDERLY_AI_PROVIDER must be 'deepseek', current value: $provider"
}

Write-Step "Provider: $normalizedProvider"
Write-Step "Model: $model"
Write-Step "DeepSeek key: $(Get-MaskedSecret -Value $apiKey) (length=$($apiKey.Length))"

$options = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
$localProvider = [Orderly.Data.Services.LocalAiSuggestionProvider]::new()
$primaryProvider = [Orderly.Data.Services.AiSuggestionProviderFactory]::CreatePrimaryProvider($options, $localProvider)

if ($primaryProvider.Name -ne [Orderly.Data.Services.AiProviderOptions]::DeepSeekProviderName) {
    throw "Primary provider is not deepseek. Actual: $($primaryProvider.Name)"
}

$request = New-MinimalDeepSeekRequest
$startedAt = Get-Date
$result = $primaryProvider.GenerateAsync($request).GetAwaiter().GetResult()
$finishedAt = Get-Date

if ($result.Provider -ne [Orderly.Data.Services.AiProviderOptions]::DeepSeekProviderName) {
    throw "Unexpected provider result: $($result.Provider)"
}

if ([string]::IsNullOrWhiteSpace($result.SuggestionText)) {
    throw 'DeepSeek provider returned empty suggestion text.'
}

$snippet = $result.SuggestionText.Trim()
if ($snippet.Length -gt 120) {
    $snippet = $snippet.Substring(0, 120) + '...'
}

$metadata = @{}
if (-not [string]::IsNullOrWhiteSpace($result.MetadataJson)) {
    $metadata = $result.MetadataJson | ConvertFrom-Json -AsHashtable
}

$runDirectory = New-QaSmokeRunDirectory
$reportPath = Join-Path $runDirectory.Path 'deepseek-live-smoke-report.json'
$report = [pscustomobject]@{
    startedAt = $startedAt.ToString('o')
    finishedAt = $finishedAt.ToString('o')
    provider = $result.Provider
    model = $result.Model
    requestedProvider = $options.RequestedProvider
    baseUrl = $options.BaseUrl
    endpoint = $metadata['endpoint']
    timeoutSeconds = $options.TimeoutSeconds
    responseLength = $result.SuggestionText.Length
    responseSnippet = $snippet
    maskedApiKey = Get-MaskedSecret -Value $apiKey
}
Save-Utf8Json -Path $reportPath -Value $report

Write-Step "DeepSeek live request succeeded"
Write-Step "Response length: $($result.SuggestionText.Length)"
Write-Step "Response snippet: $snippet"
Write-Step "Report: $reportPath"
