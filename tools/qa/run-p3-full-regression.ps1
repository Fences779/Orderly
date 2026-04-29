param()

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    & $Path

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3 full regression'
Write-Step 'Scope: build + P1/P2/P3 local regression, fail fast'
Write-Step "Repo root: $(Get-RepoRoot)"

$executed = New-Object System.Collections.Generic.List[string]

try {
    Write-Step 'Step 1/7: dotnet build Orderly.sln -c Debug'
    Push-Location (Get-RepoRoot)
    try {
        dotnet build Orderly.sln -c Debug
        if (-not $?) {
            throw 'dotnet build failed.'
        }

        $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
        if ($exitCode -ne 0) {
            throw "dotnet build failed with exit code: $exitCode"
        }
    }
    finally {
        Pop-Location
    }
    $executed.Add('dotnet build Orderly.sln -c Debug')

    $steps = @(
        'run-p1-smoke.ps1',
        'run-p2-full-regression.ps1',
        'run-p3-1-workbench-smoke.ps1',
        'run-p3-2-pipeline-smoke.ps1',
        'run-p3-4-workbench-logic-smoke.ps1'
    )

    for ($index = 0; $index -lt $steps.Count; $index++) {
        $scriptName = $steps[$index]
        $scriptPath = Join-Path $PSScriptRoot $scriptName
        Write-Step ("Step {0}/7: {1}" -f ($index + 2), $scriptName)
        Invoke-QaScript -Path $scriptPath
        $executed.Add($scriptName)
        Write-Step ("PASS {0}" -f $scriptName)
    }

    Write-Host ''
    Write-Host 'P3 FULL REGRESSION: PASS'
    Write-Host ('Executed: ' + ($executed -join ', '))
}
catch {
    Write-Host ''
    Write-Host 'P3 FULL REGRESSION: FAILED'
    Write-Host ('Executed before failure: ' + ($(if ($executed.Count -gt 0) { $executed -join ', ' } else { 'none' })))
    Write-Host ('Reason: ' + $_.Exception.Message)
    throw
}
