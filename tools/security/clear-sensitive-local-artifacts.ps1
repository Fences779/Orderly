param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$targets = @(
    'ordely.db',
    'orderly.db',
    'artifacts\qa-db',
    'artifacts\qa-smoke',
    'artifacts\Orderly.sqlite'
)

foreach ($relativePath in $targets) {
    $target = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $target)) {
        continue
    }

    $resolved = (Resolve-Path -LiteralPath $target).Path
    if (-not $resolved.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside repository: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Host "Removed $relativePath"
}
