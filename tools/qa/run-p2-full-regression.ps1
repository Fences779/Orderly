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

$scripts = @(
    'run-p2-1-message-smoke.ps1',
    'run-p2-2-ai-suggestion-smoke.ps1',
    'run-p2-3-auto-reply-smoke.ps1',
    'run-p2-4-ai-provider-smoke.ps1',
    'run-p2-5-ocr-smoke.ps1',
    'run-p2-6-manual-send-smoke.ps1',
    'run-p2-7-backup-smoke.ps1',
    'run-p2-8-restore-smoke.ps1',
    'run-p2-9-restore-preview-smoke.ps1'
)

$passed = New-Object System.Collections.Generic.List[string]

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P2 full regression'
Write-Step 'Scope: local-only smoke suite, no public network, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

try {
    for ($i = 0; $i -lt $scripts.Count; $i++) {
        $scriptName = $scripts[$i]
        $scriptPath = Join-Path $PSScriptRoot $scriptName
        Write-Step ("Step {0}/{1}: {2}" -f ($i + 1), $scripts.Count, $scriptName)
        Invoke-QaScript -Path $scriptPath
        $passed.Add($scriptName)
        Write-Step ("PASS {0}" -f $scriptName)
    }

    Write-Host ''
    Write-Host 'P2 FULL REGRESSION: PASS'
    Write-Host ('Executed: ' + ($passed -join ', '))
}
catch {
    Write-Host ''
    Write-Host 'P2 FULL REGRESSION: FAILED'
    Write-Host ('Passed before failure: ' + ($(if ($passed.Count -gt 0) { $passed -join ', ' } else { 'none' })))
    Write-Host ('Failed at: ' + $scriptName)
    Write-Host ('Reason: ' + $_.Exception.Message)
    throw
}
