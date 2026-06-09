[CmdletBinding()]
param(
    [string]$EnvId = '',
    [string]$ValuesFile = '',
    [string[]]$RequiredVariable = @(
        'ORDERLY_ALLOWED_OPENIDS',
        'ORDERLY_ALLOWED_WORKSPACE_IDS',
        'ORDERLY_INVENTORY_GATEWAY_TOKEN',
        'ORDERLY_INVENTORY_WORKSPACE_ID'
    )
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

function Resolve-CloudBaseEnvId {
    param([string]$ExplicitEnvId)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitEnvId)) {
        return $ExplicitEnvId.Trim()
    }

    $configPath = Join-Path $RepoRoot 'cloudbaserc.json'
    if (Test-Path $configPath) {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($config.envId)) {
            return [string]$config.envId
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CLOUDBASE_ENV_ID)) {
        return $env:CLOUDBASE_ENV_ID.Trim()
    }

    throw 'CloudBase envId is required. Pass -EnvId or set cloudbaserc.json/env:CLOUDBASE_ENV_ID.'
}

function ConvertTo-Hashtable {
    param($Value)

    $result = @{}
    if ($null -eq $Value) {
        return $result
    }

    foreach ($property in $Value.PSObject.Properties) {
        $result[$property.Name] = [string]$property.Value
    }

    return $result
}

function Read-ValuesFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return @{}
    }

    $resolvedPath = Resolve-Path $Path
    $json = Get-Content $resolvedPath -Raw | ConvertFrom-Json
    if ($json.PSObject.Properties.Name -contains 'env') {
        return ConvertTo-Hashtable $json.env
    }

    return ConvertTo-Hashtable $json
}

function Read-CloudBaseConfiguredValues {
    $configPath = Join-Path $RepoRoot 'cloudbaserc.json'
    if (-not (Test-Path $configPath)) {
        return @{}
    }

    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $values = @{}
    if ($null -eq $config.functions) {
        return $values
    }

    foreach ($functionConfig in @($config.functions)) {
        if ($null -eq $functionConfig.envVariables) {
            continue
        }

        foreach ($property in $functionConfig.envVariables.PSObject.Properties) {
            if (-not $values.ContainsKey($property.Name)) {
                $values[$property.Name] = [string]$property.Value
            }
        }
    }

    return $values
}

function Resolve-VariableValue {
    param(
        [string]$Name,
        [hashtable]$ValuesFileValues,
        [hashtable]$CloudBaseValues
    )

    if ($ValuesFileValues.ContainsKey($Name)) {
        return $ValuesFileValues[$Name]
    }

    $processValue = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($processValue)) {
        return $processValue
    }

    if ($CloudBaseValues.ContainsKey($Name)) {
        return $CloudBaseValues[$Name]
    }

    return ''
}

function Test-WeakValue {
    param(
        [string]$Value,
        [switch]$AllowDefault
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $normalized = $Value.Trim().ToLowerInvariant()
    $weakValues = @('replace-me', 'changeme', 'change-me', 'todo', 'test', 'password', 'token')
    if (-not $AllowDefault) {
        $weakValues += 'default'
    }

    return $normalized -in $weakValues
}

function Test-EnabledFlag {
    param(
        [string]$Name,
        [hashtable]$ValuesFileValues,
        [hashtable]$CloudBaseValues
    )

    $value = Resolve-VariableValue -Name $Name -ValuesFileValues $ValuesFileValues -CloudBaseValues $CloudBaseValues
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $false
    }

    return $value.Trim().ToLowerInvariant() -in @('1', 'true', 'yes', 'on')
}

function Resolve-RuntimeEnvironment {
    param(
        [hashtable]$ValuesFileValues,
        [hashtable]$CloudBaseValues
    )

    $runtime = Resolve-VariableValue -Name 'ORDERLY_RUNTIME_ENV' -ValuesFileValues $ValuesFileValues -CloudBaseValues $CloudBaseValues
    if (-not [string]::IsNullOrWhiteSpace($runtime)) {
        return $runtime.Trim().ToLowerInvariant()
    }

    $nodeEnv = Resolve-VariableValue -Name 'NODE_ENV' -ValuesFileValues $ValuesFileValues -CloudBaseValues $CloudBaseValues
    return $nodeEnv.Trim().ToLowerInvariant()
}

$resolvedEnvId = Resolve-CloudBaseEnvId -ExplicitEnvId $EnvId
$valuesFileValues = Read-ValuesFile -Path $ValuesFile
$cloudBaseValues = Read-CloudBaseConfiguredValues
$failures = New-Object System.Collections.Generic.List[string]

foreach ($name in $RequiredVariable) {
    $value = Resolve-VariableValue -Name $name -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues
    $allowDefault = $name -in @('ORDERLY_ALLOWED_WORKSPACE_IDS', 'ORDERLY_INVENTORY_WORKSPACE_ID')
    if (Test-WeakValue -Value $value -AllowDefault:$allowDefault) {
        $failures.Add("Missing or weak required variable: $name")
    }
}

$seedEnabled = Test-EnabledFlag -Name 'ORDERLY_ENABLE_DEAL_INIT_SEED' -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues
if ($seedEnabled) {
    $seedAdmins = Resolve-VariableValue -Name 'ORDERLY_DEAL_INIT_SEED_OPENIDS' -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues
    if (Test-WeakValue -Value $seedAdmins) {
        $failures.Add('ORDERLY_ENABLE_DEAL_INIT_SEED is enabled but ORDERLY_DEAL_INIT_SEED_OPENIDS is missing or weak.')
    }
}

foreach ($name in @('ADMIN_PC_GATEWAY_SEND_TOKEN_IN_BODY', 'ORDERLY_INVENTORY_GATEWAY_SEND_TOKEN_IN_BODY')) {
    if (Test-EnabledFlag -Name $name -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues) {
        $failures.Add("Compatibility token-in-body flag must be disabled for production: $name")
    }
}

$authAllowAllDev = Test-EnabledFlag -Name 'ORDERLY_AUTH_ALLOW_ALL_DEV' -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues
if ($authAllowAllDev) {
    $runtime = Resolve-RuntimeEnvironment -ValuesFileValues $valuesFileValues -CloudBaseValues $cloudBaseValues
    if ($runtime -notin @('development', 'dev', 'test', 'local')) {
        $failures.Add('ORDERLY_AUTH_ALLOW_ALL_DEV can only be enabled when ORDERLY_RUNTIME_ENV or NODE_ENV is development/dev/test/local.')
    }
}

if ($failures.Count -gt 0) {
    Write-Error "CloudBase security env preflight failed for $resolvedEnvId. $($failures -join ' ')"
    exit 1
}

Write-Host "CloudBase security env preflight passed for $resolvedEnvId. Variable values were not printed."
