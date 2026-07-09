param(
    [switch]$SkipBuild,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$emptyEnvPath = Join-Path $repoRoot ".env.t8-empty"
$smokeEnvPath = Join-Path $repoRoot ".env.t8-smoke"
$projectName = "orderly-cloud-t8"
$apiPort = 18082
$postgresPort = 15442
$baseUrl = "http://127.0.0.1:$apiPort"

function New-Secret([int]$Bytes = 32) {
    $buffer = [byte[]]::new($Bytes)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($buffer)
    }
    finally {
        $rng.Dispose()
    }
    return ([BitConverter]::ToString($buffer) -replace '-', '')
}

function Write-EnvFile(
    [string]$Path,
    [string]$PostgresPassword,
    [string]$JwtKey,
    [string]$BootstrapToken,
    [string]$BootstrapPassword
) {
    $content = @"
ORDERLY_COMPOSE_PROJECT_NAME=$projectName
TZ=Asia/Shanghai
ORDERLY_API_HTTP_PORT=$apiPort
ORDERLY_POSTGRES_HOST_PORT=$postgresPort
ORDERLY_PUBLIC_URL=$baseUrl
ORDERLY_ALLOWED_ORIGINS=$baseUrl
ORDERLY_DOMAIN=localhost
POSTGRES_DB=orderly
POSTGRES_USER=orderly
POSTGRES_PASSWORD=$PostgresPassword
ORDERLY_JWT_SIGNING_KEY=$JwtKey
ORDERLY_BOOTSTRAP_ADMIN_TOKEN=$BootstrapToken
ORDERLY_BOOTSTRAP_ADMIN_PASSWORD=$BootstrapPassword
ORDERLY_BACKUP_RETENTION_DAYS=30
ORDERLY_REQUIRE_PRE_MIGRATION_BACKUP=false
ORDERLY_REQUIRE_PRE_IMPORT_BACKUP=false
ORDERLY_RESTORE_DRILL_ENABLED=false
ORDERLY_RESTORE_DRILL_INTERVAL_HOURS=24
ORDERLY_LOCAL_EXPORT_DIR=/opt/orderly/exports
ORDERLY_LOCAL_BLOB_DIR=/opt/orderly/blobs
ORDERLY_EXPORT_RETENTION_HOURS=24
ORDERLY_EXPORT_MAX_RETRY_COUNT=2
ORDERLY_EXPORT_MAX_LOCAL_BYTES=2147483648
ORDERLY_OSS_ENDPOINT=
ORDERLY_OSS_BUCKET=
ORDERLY_OSS_ACCESS_KEY_ID=
ORDERLY_OSS_ACCESS_KEY_SECRET=
ORDERLY_OSS_BACKUP_PREFIX=backups/
ORDERLY_OSS_EXPORT_PREFIX=exports/
"@
    Set-Content -Path $Path -Value $content -Encoding UTF8
}

function Invoke-Compose([string]$EnvFile, [string[]]$Arguments) {
    & docker compose --env-file $EnvFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Test-ImageExists([string]$ImageName) {
    & docker image inspect $ImageName *> $null
    return $LASTEXITCODE -eq 0
}

function Invoke-ComposeBuildOrUseCached([string]$EnvFile) {
    try {
        Invoke-Compose $EnvFile @("build", "orderly-server")
    }
    catch {
        if (Test-ImageExists "orderly-server:latest") {
            Write-Warning "docker compose build failed, using existing local orderly-server:latest for compose validation. Original error: $($_.Exception.Message)"
            return
        }

        throw
    }
}

function Wait-ApiHealth([string]$Url, [int]$TimeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-RestMethod -Uri "$Url/health" -TimeoutSec 5
            if ($response.Status -eq "Healthy") {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)

    throw "API health did not become healthy within $TimeoutSeconds seconds."
}

function Invoke-PostgresScalar([string]$EnvFile, [string]$Sql) {
    $result = $Sql | & docker compose --env-file $EnvFile exec -T postgres psql -U orderly -d orderly -tA
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL scalar query failed."
    }

    return ($result | Select-Object -First 1).Trim()
}

$dockerVersion = docker info --format "{{.ServerVersion}}" 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dockerVersion)) {
    throw "Docker daemon is required for Docker Compose validation."
}

if (-not $SkipBuild) {
    dotnet build Orderly.sln --nologo
}

$emptyPostgresPassword = New-Secret
$emptyJwtKey = New-Secret
Write-EnvFile -Path $emptyEnvPath `
    -PostgresPassword $emptyPostgresPassword `
    -JwtKey $emptyJwtKey `
    -BootstrapToken "" `
    -BootstrapPassword ""

Write-Host "T8 compose config check"
Invoke-Compose $emptyEnvPath @("config", "--quiet")
Invoke-ComposeBuildOrUseCached $emptyEnvPath

Write-Host "T8 empty database migration check"
Invoke-Compose $emptyEnvPath @("down", "--volumes", "--remove-orphans")
Invoke-Compose $emptyEnvPath @("up", "-d", "--no-build", "postgres", "orderly-server")
Wait-ApiHealth $baseUrl

$emptyUsers = Invoke-PostgresScalar $emptyEnvPath 'SELECT COUNT(*) FROM "CloudUsers";'
$emptyWorkspaces = Invoke-PostgresScalar $emptyEnvPath 'SELECT COUNT(*) FROM "CloudWorkspaces";'
if ($emptyUsers -ne "0" -or $emptyWorkspaces -ne "0") {
    throw "Empty migration must not create real users/workspaces. Users=$emptyUsers Workspaces=$emptyWorkspaces"
}

Invoke-Compose $emptyEnvPath @("down", "--volumes", "--remove-orphans")

$smokePostgresPassword = New-Secret
$smokeJwtKey = New-Secret
$bootstrapToken = New-Secret
$bootstrapPassword = "$(New-Secret -Bytes 18)Aa1!"
Write-EnvFile -Path $smokeEnvPath `
    -PostgresPassword $smokePostgresPassword `
    -JwtKey $smokeJwtKey `
    -BootstrapToken $bootstrapToken `
    -BootstrapPassword $bootstrapPassword

Write-Host "T8 compose smoke environment startup"
Invoke-Compose $smokeEnvPath @("up", "-d", "--no-build", "postgres", "orderly-server")
Wait-ApiHealth $baseUrl

$env:ORDERLY_COMPOSE_SMOKE_BASE_URL = $baseUrl
$env:ORDERLY_COMPOSE_SMOKE_ADMIN_PASSWORD = $bootstrapPassword
$env:ORDERLY_COMPOSE_SMOKE_ADMIN_DEVICE_ID = "t8-admin-device"
$env:ORDERLY_COMPOSE_SMOKE_PG_CONNECTION = "Host=127.0.0.1;Port=$postgresPort;Database=orderly;Username=orderly;Password=$smokePostgresPassword"

Write-Host "T8 compose API and sync smoke test"
dotnet test tests/Orderly.Tests/Orderly.Tests.csproj `
    --no-build `
    --nologo `
    --filter "FullyQualifiedName~ComposeSmokeTests"
if ($LASTEXITCODE -ne 0) {
    throw "ComposeSmokeTests failed."
}

Write-Host "T8 API container restart recovery"
Invoke-Compose $smokeEnvPath @("restart", "orderly-server")
Wait-ApiHealth $baseUrl

$userCountBeforeDown = Invoke-PostgresScalar $smokeEnvPath 'SELECT COUNT(*) FROM "CloudUsers";'
Invoke-Compose $smokeEnvPath @("down", "--remove-orphans")
Invoke-Compose $smokeEnvPath @("up", "-d", "postgres", "orderly-server")
Wait-ApiHealth $baseUrl
$userCountAfterUp = Invoke-PostgresScalar $smokeEnvPath 'SELECT COUNT(*) FROM "CloudUsers";'
if ([int]$userCountAfterUp -lt [int]$userCountBeforeDown -or [int]$userCountAfterUp -le 0) {
    throw "PostgreSQL volume was not retained across down/up. Before=$userCountBeforeDown After=$userCountAfterUp"
}

Write-Host "T8 repeatable smoke test after down/up"
dotnet test tests/Orderly.Tests/Orderly.Tests.csproj `
    --no-build `
    --nologo `
    --filter "FullyQualifiedName~ComposeSmokeTests"
if ($LASTEXITCODE -ne 0) {
    throw "Repeated ComposeSmokeTests failed."
}

Invoke-Compose $smokeEnvPath @("ps")

if (-not $KeepRunning) {
    Write-Host "T8 compose services remain running for local preview. Run 'docker compose --env-file .env.t8-smoke down' when done."
}

Write-Host "T8 Docker Compose validation completed."
