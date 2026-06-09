[CmdletBinding()]
param(
    [string]$EnvId = '',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$Collections = @(
    'customers',
    'deals',
    'quotes',
    'sku_catalog',
    'inventory_movements',
    'cashflow_entries',
    'followup_tasks',
    'message_templates',
    'captures',
    'activity_logs'
)

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

$resolvedEnvId = Resolve-CloudBaseEnvId -ExplicitEnvId $EnvId
$tcb = Get-Command tcb -ErrorAction Stop

foreach ($collection in $Collections) {
    $arguments = @(
        'permission',
        'set',
        "collection:$collection",
        '--level',
        'adminonly',
        '--env-id',
        $resolvedEnvId
    )

    if ($Apply) {
        & $tcb.Source @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to set adminonly permission for collection: $collection"
        }
    } else {
        Write-Host "DRY RUN: tcb $($arguments -join ' ')"
    }
}

if (-not $Apply) {
    Write-Host 'Dry run only. Re-run with -Apply to change CloudBase collection permissions.'
}
