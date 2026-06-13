<#
.SYNOPSIS
    Universal regression entry point (Requirement 12.4-12.6).

.DESCRIPTION
    Runs the full pre-release universal regression in the required order, failing fast:

        1. dotnet build            (dotnet build Orderly.sln -c Debug)
        2. dotnet test             (full suite, excluding the forbidden-terms scan)
        3. forbidden-terms scan    (ForbiddenTermsRegressionTests only)
        4. security smoke          (existing P0 local-account encryption/restore smoke, unchanged)
        5. backup smoke            (existing P2.7 local backup smoke)
        6. commerce smoke          (run-commerce-smoke.ps1)

    On full success the script emits a pass result and exits with code 0. On the first
    failing step it stops, identifies the step, and exits non-zero (Req 12.5, 12.6).

    The existing P0 security smoke script and the backup smoke script are invoked as-is and
    are never modified by this script (Req 12.7). This script and the commerce smoke it calls
    contain no forbidden term.
#>
param()

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

# The existing P0 security smoke script. Retained unchanged (Req 12.7); only invoked here.
$securitySmokeScript = Join-Path $PSScriptRoot 'run-p4-local-account-encryption-restore-smoke.ps1'
# The existing local backup smoke script.
$backupSmokeScript = Join-Path $PSScriptRoot 'run-p2-7-backup-smoke.ps1'
# The universal commerce smoke added by this task.
$commerceSmokeScript = Join-Path $PSScriptRoot 'run-commerce-smoke.ps1'

$testsProject = Join-RepoPath @('tests', 'Orderly.Tests', 'Orderly.Tests.csproj')
$forbiddenTermsFilter = 'FullyQualifiedName~ForbiddenTermsRegressionTests'

$script:CurrentStep = 'startup'
$script:TotalSteps = 6
$executed = New-Object System.Collections.Generic.List[string]

function Initialize-IsolatedQaEnvironment {
    # The regression must run entirely against a throwaway, isolated QA workspace and never touch the
    # real %LocalAppData%\Orderly user data. We point every QA path env var at a fresh temp directory
    # and enable the privileged-QA startup gate in a QA runtime, mirroring what the app's startup
    # isolation guard (EnsureQaDatabasePathIsIsolated) requires. Originals are captured for restore.
    $names = @(
        'ORDERLY_RUNTIME_ENV',
        'DOTNET_ENVIRONMENT',
        'ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP',
        'ORDERLY_QA_DATA_ROOT',
        'ORDERLY_QA_DB_PATH',
        'ORDERLY_QA_ARTIFACT_ROOT'
    )

    $originals = @{}
    foreach ($name in $names) {
        $originals[$name] = [Environment]::GetEnvironmentVariable($name)
    }

    $stamp = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $baseRoot = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'Orderly-universal-regression' $stamp)
    $dataRoot = Join-Path $baseRoot 'qa-data'
    $dbPath = Join-Path $dataRoot 'orderly.db'
    $artifactRoot = Join-Path $baseRoot 'artifacts'

    # Safety self-check: refuse to run destructive QA resets against the real user data directory.
    $realAppRoot = [System.IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Orderly'))
    $resolvedDataRoot = [System.IO.Path]::GetFullPath($dataRoot)
    if ($resolvedDataRoot.StartsWith($realAppRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to run: the isolated QA data root must not be under the real user data directory ($realAppRoot)."
    }

    [System.IO.Directory]::CreateDirectory($dataRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null

    [Environment]::SetEnvironmentVariable('ORDERLY_RUNTIME_ENV', 'QA')
    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'QA')
    [Environment]::SetEnvironmentVariable('ORDERLY_ENABLE_PRIVILEGED_QA_STARTUP', '1')
    [Environment]::SetEnvironmentVariable('ORDERLY_QA_DATA_ROOT', $resolvedDataRoot)
    [Environment]::SetEnvironmentVariable('ORDERLY_QA_DB_PATH', [System.IO.Path]::GetFullPath($dbPath))
    [Environment]::SetEnvironmentVariable('ORDERLY_QA_ARTIFACT_ROOT', [System.IO.Path]::GetFullPath($artifactRoot))

    Write-Step "Isolated QA environment: $baseRoot"
    Write-Step "  ORDERLY_QA_DB_PATH = $([System.IO.Path]::GetFullPath($dbPath))"

    return [pscustomobject]@{
        Originals = $originals
        BaseRoot  = $baseRoot
    }
}

function Restore-IsolatedQaEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        $State
    )

    foreach ($name in $State.Originals.Keys) {
        [Environment]::SetEnvironmentVariable($name, $State.Originals[$name])
    }

    if ($State.BaseRoot -and (Test-Path -LiteralPath $State.BaseRoot)) {
        try { [Microsoft.Data.Sqlite.SqliteConnection]::ClearAllPools() | Out-Null } catch { }
        Remove-Item -LiteralPath $State.BaseRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Assert-RegressionExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$What
    )

    if (-not $?) {
        throw "$What failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$What failed with exit code: $exitCode"
    }
}

function Invoke-DotnetInRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$DotnetArguments,
        [Parameter(Mandatory = $true)]
        [string]$What
    )

    Push-Location (Get-RepoRoot)
    try {
        & dotnet @DotnetArguments
        Assert-RegressionExitCode -What $What
    }
    finally {
        Pop-Location
    }
}

function Invoke-RegressionScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ScriptArguments = @()
    )

    $name = [System.IO.Path]::GetFileName($Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required QA script not found: $name ($Path)."
    }

    & $Path @ScriptArguments
    Assert-RegressionExitCode -What $name
}

function Invoke-RegressionStep {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    $script:CurrentStep = $Name
    Write-Step ("Step {0}/{1}: {2}" -f $Index, $script:TotalSteps, $Name)
    & $Action
    $executed.Add($Name)
    Write-Step ("PASS {0}" -f $Name)
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting universal regression'
Write-Step 'Order: build -> test -> forbidden-terms scan -> security smoke -> backup smoke -> commerce smoke'
Write-Step "Repo root: $(Get-RepoRoot)"

$qaEnvironment = Initialize-IsolatedQaEnvironment
$regressionExitCode = 0
try {
    try {
        Invoke-RegressionStep -Index 1 -Name 'dotnet build' -Action {
            Invoke-DotnetInRepo -DotnetArguments @('build', 'Orderly.sln', '-c', 'Debug') -What 'dotnet build'
        }

        Invoke-RegressionStep -Index 2 -Name 'dotnet test' -Action {
            Invoke-DotnetInRepo -DotnetArguments @(
                'test', $testsProject, '-c', 'Debug',
                '--filter', "FullyQualifiedName!~ForbiddenTermsRegressionTests") -What 'dotnet test'
        }

        Invoke-RegressionStep -Index 3 -Name 'forbidden-terms scan' -Action {
            Invoke-DotnetInRepo -DotnetArguments @(
                'test', $testsProject, '-c', 'Debug',
                '--filter', $forbiddenTermsFilter) -What 'forbidden-terms scan'
        }

        Invoke-RegressionStep -Index 4 -Name 'security smoke' -Action {
            Invoke-RegressionScript -Path $securitySmokeScript
        }

        Invoke-RegressionStep -Index 5 -Name 'backup smoke' -Action {
            Invoke-RegressionScript -Path $backupSmokeScript
        }

        Invoke-RegressionStep -Index 6 -Name 'commerce smoke' -Action {
            Invoke-RegressionScript -Path $commerceSmokeScript -ScriptArguments @('-SkipBuild')
        }

        Write-Host ''
        Write-Host 'UNIVERSAL REGRESSION: PASS'
        Write-Host ('Executed: ' + ($executed -join ', '))
        $regressionExitCode = 0
    }
    catch {
        Write-Host ''
        Write-Host 'UNIVERSAL REGRESSION: FAILED'
        Write-Host ('Executed before failure: ' + ($(if ($executed.Count -gt 0) { $executed -join ', ' } else { 'none' })))
        Write-Host ('Failed step: ' + $script:CurrentStep)
        Write-Host ('Reason: ' + $_.Exception.Message)
        $regressionExitCode = 1
    }
}
finally {
    Restore-IsolatedQaEnvironment -State $qaEnvironment
}

exit $regressionExitCode
