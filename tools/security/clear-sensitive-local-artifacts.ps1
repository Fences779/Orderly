param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$repoRootWithSeparator = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
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

    $resolved = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
    if ($resolved -ne $repoRoot -and -not $resolved.StartsWith($repoRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside repository: $resolved"
    }

    $item = Get-Item -LiteralPath $resolved -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove reparse point: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Host "Removed $relativePath"
}
