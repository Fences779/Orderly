param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$dockerVersion = docker info --format "{{.ServerVersion}}" 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dockerVersion)) {
    throw "Docker daemon is required for local integration smoke tests."
}

if (-not $SkipBuild) {
    dotnet build Orderly.sln --nologo
}

dotnet test tests/Orderly.Tests/Orderly.Tests.csproj `
    --no-build `
    --nologo `
    --filter "FullyQualifiedName~LocalIntegrationSmokeTests"
