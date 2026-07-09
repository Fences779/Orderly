[CmdletBinding()]
param(
    [string]$ComposeFile,
    [string]$EnvFile,
    [string]$EcsDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path $scriptRoot '..\deploy\ecs\docker-compose.prod.yml'
}

if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = Join-Path $scriptRoot '..\deploy\ecs\.env.prod.example'
}

if ([string]::IsNullOrWhiteSpace($EcsDir)) {
    $EcsDir = Join-Path $scriptRoot '..\deploy\ecs'
}

$script:failures = [System.Collections.Generic.List[string]]::new()
$script:warnings = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string]$Message)
    [void]$script:failures.Add($Message)
}

function Add-Warning {
    param([string]$Message)
    [void]$script:warnings.Add($Message)
}

function Resolve-FullPath {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Read-EnvFile {
    param([string]$Path)

    $values = @{}
    $lineNumber = 0
    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $lineNumber++
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) {
            continue
        }

        $equalsIndex = $line.IndexOf('=')
        if ($equalsIndex -le 0) {
            Add-Failure "Invalid env line $lineNumber in $Path"
            continue
        }

        $key = $line.Substring(0, $equalsIndex).Trim()
        $value = $line.Substring($equalsIndex + 1).Trim().Trim('"')
        $values[$key] = $value
    }

    return $values
}

function Get-ServiceBlock {
    param(
        [string]$Text,
        [string]$Name
    )

    $escapedName = [regex]::Escape($Name)
    $pattern = "(?ms)^\s{2}${escapedName}:\s*\r?\n(?<body>.*?)(?=^\s{2}[A-Za-z0-9_.-]+:\s*\r?\n|\z)"
    $match = [regex]::Match($Text, $pattern)
    if ($match.Success) {
        return $match.Value
    }

    return $null
}

function Test-PlaceholderValue {
    param([string]$Value)

    return $Value -match '__SET_' `
        -or $Value -match 'example\.invalid' `
        -or $Value -match 'replace-with' `
        -or $Value -match 'CHANGE_ME' `
        -or $Value -match 'placeholder'
}

$composePath = Resolve-FullPath $ComposeFile
$envPath = Resolve-FullPath $EnvFile
$ecsPath = Resolve-FullPath $EcsDir
$scriptPath = Resolve-FullPath $PSCommandPath

if (-not (Test-Path -LiteralPath $composePath)) { Add-Failure "Compose file not found: $composePath" }
if (-not (Test-Path -LiteralPath $envPath)) { Add-Failure "Env file not found: $envPath" }
if (-not (Test-Path -LiteralPath $ecsPath)) { Add-Failure "ECS dir not found: $ecsPath" }

if ($script:failures.Count -eq 0) {
    $envValues = Read-EnvFile $envPath
    $requiredEnv = @(
        'ASPNETCORE_ENVIRONMENT',
        'ORDERLY_SERVER_IMAGE',
        'ORDERLY_DOMAIN',
        'ORDERLY_PUBLIC_URL',
        'ORDERLY_ALLOWED_ORIGINS',
        'POSTGRES_DB',
        'POSTGRES_USER',
        'POSTGRES_PASSWORD',
        'ORDERLY_POSTGRES_HOST',
        'ORDERLY_POSTGRES_PORT',
        'ORDERLY_POSTGRES_DB',
        'ORDERLY_POSTGRES_USER',
        'ORDERLY_POSTGRES_PASSWORD',
        'ORDERLY_JWT_SIGNING_KEY',
        'ORDERLY_BOOTSTRAP_ADMIN_TOKEN',
        'ORDERLY_BOOTSTRAP_ADMIN_PASSWORD',
        'ORDERLY_BACKUP_RETENTION_DAYS',
        'ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP',
        'ORDERLY_REQUIRE_PRE_IMPORT_BACKUP',
        'ORDERLY_RESTORE_DRILL_ENABLED',
        'ORDERLY_LOCAL_BACKUP_DIR',
        'ORDERLY_LOCAL_EXPORT_DIR',
        'ORDERLY_BACKUP_ENCRYPTION_KEY',
        'ORDERLY_OSS_ENDPOINT',
        'ORDERLY_OSS_BUCKET',
        'ORDERLY_OSS_ACCESS_KEY_ID',
        'ORDERLY_OSS_ACCESS_KEY_SECRET'
    )

    foreach ($key in $requiredEnv) {
        if (-not $envValues.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$envValues[$key])) {
            Add-Failure "Required env missing or empty: $key"
        }
    }

    if ($envValues.ContainsKey('ASPNETCORE_ENVIRONMENT') -and $envValues['ASPNETCORE_ENVIRONMENT'] -ne 'Production') {
        Add-Failure 'ASPNETCORE_ENVIRONMENT must be Production.'
    }

    if ($envValues.ContainsKey('ORDERLY_ALLOWED_ORIGINS') -and $envValues['ORDERLY_ALLOWED_ORIGINS'] -eq '*') {
        Add-Failure 'ORDERLY_ALLOWED_ORIGINS must not be wildcard in production.'
    }

    if ($envValues.ContainsKey('ORDERLY_BACKUP_RETENTION_DAYS')) {
        $retentionDays = 0
        if (-not [int]::TryParse([string]$envValues['ORDERLY_BACKUP_RETENTION_DAYS'], [ref]$retentionDays) -or $retentionDays -lt 30) {
            Add-Failure 'ORDERLY_BACKUP_RETENTION_DAYS must be at least 30.'
        }
    }

    $templateMode = [System.IO.Path]::GetFileName($envPath).EndsWith('.example', [StringComparison]::OrdinalIgnoreCase)
    if (-not $templateMode) {
        foreach ($secretKey in $envValues.Keys | Where-Object { $_ -match '(PASSWORD|TOKEN|SECRET|KEY)' }) {
            if (Test-PlaceholderValue ([string]$envValues[$secretKey])) {
                Add-Failure "Production env still contains placeholder for $secretKey"
            }
        }
    }

    $composeText = Get-Content -LiteralPath $composePath -Raw
    $postgresBlock = Get-ServiceBlock $composeText 'postgres'
    $apiBlock = Get-ServiceBlock $composeText 'orderly-server'
    $caddyBlock = Get-ServiceBlock $composeText 'caddy'

    if ($null -eq $postgresBlock) { Add-Failure 'postgres service is missing.' }
    if ($null -eq $apiBlock) { Add-Failure 'orderly-server service is missing.' }
    if ($null -eq $caddyBlock) { Add-Failure 'caddy service is missing.' }

    if ($postgresBlock -and $postgresBlock -match '(?m)^\s+ports\s*:') {
        Add-Failure 'PostgreSQL must not expose public ports.'
    }

    $redisBlock = Get-ServiceBlock $composeText 'redis'
    if ($redisBlock -and $redisBlock -match '(?m)^\s+ports\s*:') {
        Add-Failure 'Redis must not expose public ports.'
    }

    if ($apiBlock -and $apiBlock -match '(?m)^\s+ports\s*:') {
        Add-Failure 'API must not expose public ports; expose it only to reverse proxy.'
    }

    foreach ($service in @('postgres', 'orderly-server', 'caddy')) {
        $block = Get-ServiceBlock $composeText $service
        if ($block -and $block -notmatch '(?m)^\s+healthcheck\s*:') {
            Add-Failure "$service is missing healthcheck."
        }
    }

    foreach ($requiredPath in @('/opt/orderly/data/postgres', '/opt/orderly/backups', '/opt/orderly/logs', '/opt/orderly/data/object-storage')) {
        if ($composeText -notmatch [regex]::Escape($requiredPath)) {
            Add-Failure "Compose is missing required ECS path: $requiredPath"
        }
    }

    if ($composeText -notmatch 'max-size' -or $composeText -notmatch 'max-file') {
        Add-Failure 'Compose is missing Docker log size policy.'
    }

    $caddyPath = Join-Path $ecsPath 'Caddyfile.example'
    if (-not (Test-Path -LiteralPath $caddyPath)) {
        Add-Failure "Reverse proxy template missing: $caddyPath"
    }
    else {
        $caddyText = Get-Content -LiteralPath $caddyPath -Raw
        if ($caddyText -notmatch 'reverse_proxy\s+orderly-server:8080') {
            Add-Failure 'Caddyfile must reverse proxy to orderly-server:8080.'
        }
        if ($caddyText -notmatch 'SignalR' -or $caddyText -notmatch '/hubs/workspace') {
            Add-Failure 'Caddyfile must document SignalR websocket forwarding.'
        }
        if ($caddyText -notmatch 'blockedPublicAttachmentPaths') {
            Add-Failure 'Caddyfile must block public-looking attachment/static blob paths.'
        }
    }

    foreach ($doc in @('SECRETS_CHECKLIST.md', 'MIGRATION_RUNBOOK.md', 'BACKUP_RESTORE_RUNBOOK.md', 'T10_DEPLOYMENT_RUNBOOK.md', 'README.md')) {
        $docPath = Join-Path $ecsPath $doc
        if (-not (Test-Path -LiteralPath $docPath)) {
            Add-Failure "Required ECS doc missing: $doc"
        }
    }

    $migrationPath = Join-Path $ecsPath 'MIGRATION_RUNBOOK.md'
    if (Test-Path -LiteralPath $migrationPath) {
        $migrationText = Get-Content -LiteralPath $migrationPath -Raw
        foreach ($term in @('DbUp', '首次空库', '后续升级', '备份', '不重写历史 migration', '不清空生产库')) {
            if ($migrationText -notmatch [regex]::Escape($term)) {
                Add-Failure "Migration runbook missing term: $term"
            }
        }
    }

    $backupPath = Join-Path $ecsPath 'BACKUP_RESTORE_RUNBOOK.md'
    if (Test-Path -LiteralPath $backupPath) {
        $backupText = Get-Content -LiteralPath $backupPath -Raw
        foreach ($term in @('30', '异地备份', '附件', '恢复演练', '告警')) {
            if ($backupText -notmatch [regex]::Escape($term)) {
                Add-Failure "Backup runbook missing term: $term"
            }
        }
    }

    $scanFiles = @()
    $scanFiles += Get-ChildItem -LiteralPath $ecsPath -File -Recurse
    $scanFiles += Get-Item -LiteralPath $scriptPath
    $assignmentPattern = '^\s*[A-Z0-9_]*(PASSWORD|TOKEN|SECRET|KEY)[A-Z0-9_]*\s*[:=]\s*([^#\r\n]+)'
    foreach ($file in $scanFiles) {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $lineNumber++
            if ($line -match $assignmentPattern) {
                $value = $Matches[2].Trim().Trim('"').Trim("'")
                if ([string]::IsNullOrWhiteSpace($value) -or (Test-PlaceholderValue $value) -or $value.StartsWith('${')) {
                    continue
                }

                Add-Failure "Possible committed real secret in $($file.FullName):$lineNumber"
            }
        }
    }

    $dangerPatterns = @(
        'down\s+--volumes',
        'docker\s+system\s+prune',
        'rm\s+-rf\s+/opt/orderly',
        'DROP\s+DATABASE\s+orderly',
        'TRUNCATE\s+TABLE',
        'DELETE\s+FROM\s+"?Cloud',
        'git\s+reset\s+--hard'
    )
    foreach ($file in $scanFiles) {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName) {
            $lineNumber++
            foreach ($pattern in $dangerPatterns) {
                if ($line -match $pattern -and $line -notmatch '(禁止|不允许|不要|Never|Do not)') {
                    Add-Failure "Possible production-dangerous command in $($file.FullName):$lineNumber"
                }
            }
        }
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        Add-Failure 'docker command not found; cannot validate compose syntax.'
    }
    else {
        $composeOutput = & docker compose --env-file $envPath -f $composePath config --quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            Add-Failure "docker compose config failed: $composeOutput"
        }
    }
}

if ($script:warnings.Count -gt 0) {
    Write-Host 'WARNINGS:'
    foreach ($warning in $script:warnings) {
        Write-Host " - $warning"
    }
}

if ($script:failures.Count -gt 0) {
    Write-Host 'ECS preflight: FAIL'
    foreach ($failure in $script:failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'ECS preflight: PASS'
Write-Host "Compose file: $composePath"
Write-Host "Env file: $envPath"
Write-Host 'Checks: compose syntax, env keys, secret placeholders, port exposure, healthchecks, log policy, runbooks, backup notes.'
