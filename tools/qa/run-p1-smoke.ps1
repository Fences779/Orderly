param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$uiaScript = Join-Path $PSScriptRoot 'run-uia-smoke.ps1'
$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'
$statusScript = Join-Path $PSScriptRoot 'run-qa-data-status.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ArgumentList = @(),
        [hashtable]$NamedArguments = @{}
    )

    & $Path @ArgumentList @NamedArguments

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

Write-Step "Starting P1 smoke"
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step "Step 1/4: reset QA data"
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step "Step 1/4: skip QA data reset"
}

Write-Step "Step 2/4: pre-smoke status"
Invoke-QaScript -Path $statusScript

Write-Step "Step 3/4: run UIA smoke"
Invoke-QaScript -Path $uiaScript -NamedArguments @{ SkipReset = $true }

Write-Step "Step 4/4: post-smoke status"
Invoke-QaScript -Path $statusScript

Write-Step "P1 smoke completed"
