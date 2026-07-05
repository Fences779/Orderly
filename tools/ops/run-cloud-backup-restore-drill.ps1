[CmdletBinding()]
param(
    [string]$DumpPath,
    [string]$TempDatabase,
    [string]$PostgresHost = $(if ($env:ORDERLY_POSTGRES_HOST) { $env:ORDERLY_POSTGRES_HOST } else { "localhost" }),
    [int]$PostgresPort = $(if ($env:ORDERLY_POSTGRES_PORT) { [int]$env:ORDERLY_POSTGRES_PORT } else { 5432 }),
    [string]$PostgresUser = $(if ($env:ORDERLY_POSTGRES_USER) { $env:ORDERLY_POSTGRES_USER } elseif ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "orderly" }),
    [string]$PostgresPassword = $(if ($env:ORDERLY_POSTGRES_PASSWORD) { $env:ORDERLY_POSTGRES_PASSWORD } elseif ($env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD } else { "" }),
    [switch]$KeepDatabase
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. Install PostgreSQL client tools and retry."
    }
}

Require-Command "createdb"
Require-Command "pg_restore"
Require-Command "psql"
Require-Command "dropdb"

if ([string]::IsNullOrWhiteSpace($DumpPath)) {
    $backupDir = if ($env:ORDERLY_LOCAL_BACKUP_DIR) { $env:ORDERLY_LOCAL_BACKUP_DIR } else { Join-Path (Get-Location) "backups" }
    if (-not (Test-Path -LiteralPath $backupDir)) {
        throw "DumpPath was not provided and backup directory does not exist: $backupDir"
    }

    $latestDump = Get-ChildItem -LiteralPath $backupDir -Filter "*.dump" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestDump) {
        throw "No .dump backup found in $backupDir"
    }

    $DumpPath = $latestDump.FullName
}

$resolvedDump = Resolve-Path -LiteralPath $DumpPath
if ([string]::IsNullOrWhiteSpace($TempDatabase)) {
    $TempDatabase = "orderly_restore_drill_{0}" -f (Get-Date -Format "yyyyMMddHHmmss")
}

if ($TempDatabase -notmatch '^orderly_restore_drill_[A-Za-z0-9_]+$') {
    throw "TempDatabase must start with orderly_restore_drill_ to avoid touching a real database."
}

if ([string]::IsNullOrWhiteSpace($PostgresPassword)) {
    Write-Warning "PostgresPassword is empty. Relying on pgpass or local trust authentication."
}
else {
    $env:PGPASSWORD = $PostgresPassword
}

Write-Host "Creating temporary database $TempDatabase ..."
& createdb -h $PostgresHost -p $PostgresPort -U $PostgresUser $TempDatabase

try {
    Write-Host "Restoring dump $resolvedDump ..."
    & pg_restore -h $PostgresHost -p $PostgresPort -U $PostgresUser --no-owner --no-privileges -d $TempDatabase $resolvedDump

    Write-Host "Running restore sanity queries ..."
    & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $TempDatabase -v ON_ERROR_STOP=1 -c 'SELECT COUNT(*) AS cloud_users FROM "CloudUsers";'
    & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $TempDatabase -v ON_ERROR_STOP=1 -c 'SELECT COUNT(*) AS workspaces FROM "CloudWorkspaces";'

    Write-Host "Restore drill passed."

    $healthDir = if ($env:ORDERLY_LOCAL_BACKUP_DIR) { $env:ORDERLY_LOCAL_BACKUP_DIR } else { Split-Path -Parent $resolvedDump }
    if (-not [string]::IsNullOrWhiteSpace($healthDir)) {
        New-Item -ItemType Directory -Force -Path $healthDir | Out-Null
        $healthPath = Join-Path $healthDir "restore-drill-health.json"
        [pscustomobject]@{
            lastRestoreDrillAtUtc = (Get-Date).ToUniversalTime().ToString("o")
            lastRestoreDrillStatus = "Passed"
            lastRestoreDrillDatabase = $TempDatabase
            updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $healthPath -Encoding UTF8
    }
}
finally {
    if (-not $KeepDatabase) {
        Write-Host "Dropping temporary database $TempDatabase ..."
        & dropdb -h $PostgresHost -p $PostgresPort -U $PostgresUser --if-exists $TempDatabase
    }
    else {
        Write-Host "Temporary database kept: $TempDatabase"
    }
}
