[CmdletBinding()]
param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

$requiredFiles = @(
    "Dockerfile",
    "docker-compose.yml",
    "deploy\Caddyfile",
    "deploy\orderly.env.example"
)

foreach ($relative in $requiredFiles) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing deployment file: $relative"
    }
}

$composeText = Get-Content -LiteralPath (Join-Path $repoRoot "docker-compose.yml") -Raw
foreach ($service in @("postgres:", "orderly-server:", "caddy:")) {
    if ($composeText -notmatch [regex]::Escape($service)) {
        throw "docker-compose.yml is missing service marker: $service"
    }
}

$caddyText = Get-Content -LiteralPath (Join-Path $repoRoot "deploy\Caddyfile") -Raw
if ($caddyText -notmatch "reverse_proxy orderly-server:8080") {
    throw "deploy/Caddyfile must reverse proxy to orderly-server:8080"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found. Install Docker Desktop or Docker Engine, then run this script again."
}

$tempEnv = New-TemporaryFile
try {
    @"
TZ=Asia/Shanghai
ORDERLY_DOMAIN=orderly.example.com
ORDERLY_PUBLIC_URL=https://orderly.example.com
ORDERLY_ALLOWED_ORIGINS=https://orderly.example.com
POSTGRES_DB=orderly
POSTGRES_USER=orderly
POSTGRES_PASSWORD=local-compose-check-password
ORDERLY_JWT_SIGNING_KEY=local-compose-check-jwt-key-32-characters-min
ORDERLY_BOOTSTRAP_ADMIN_TOKEN=local-compose-check-bootstrap-token
ORDERLY_BACKUP_RETENTION_DAYS=30
ORDERLY_OSS_ENDPOINT=
ORDERLY_OSS_BUCKET=
ORDERLY_OSS_ACCESS_KEY_ID=
ORDERLY_OSS_ACCESS_KEY_SECRET=
ORDERLY_OSS_BACKUP_PREFIX=backups/
ORDERLY_OSS_EXPORT_PREFIX=exports/
"@ | Set-Content -LiteralPath $tempEnv -Encoding UTF8

    Push-Location $repoRoot
    try {
        Write-Host "Validating docker compose config ..."
        & docker compose --env-file $tempEnv -f docker-compose.yml config --quiet

        if ($Build) {
            Write-Host "Building orderly-server image ..."
            & docker compose --env-file $tempEnv -f docker-compose.yml build orderly-server
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item -LiteralPath $tempEnv -Force -ErrorAction SilentlyContinue
}

Write-Host "Deployment package check passed."
