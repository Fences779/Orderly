Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $script:Utf8NoBom
[Console]::OutputEncoding = $script:Utf8NoBom
$OutputEncoding = $script:Utf8NoBom

function Ensure-PowerShellCore {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,
        [string[]]$ScriptArguments = @()
    )

    if ($PSVersionTable.PSEdition -eq 'Core') {
        return
    }

    $pwsh = Get-Command -Name 'pwsh' -ErrorAction SilentlyContinue
    if ($null -eq $pwsh) {
        throw "This QA tool requires PowerShell 7 (pwsh) for UTF-8 and .NET 8 compatibility. Please install pwsh and retry."
    }

    & $pwsh.Source -NoLogo -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @ScriptArguments
    exit $LASTEXITCODE
}

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Join-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Segments
    )

    $path = Get-RepoRoot
    foreach ($segment in $Segments) {
        $path = Join-Path $path $segment
    }

    return $path
}

function Get-OrderlyAppProjectPath {
    return Join-RepoPath @('src', 'Orderly.App', 'Orderly.App.csproj')
}

function Get-OrderlyAppExePath {
    $exePath = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows', 'Orderly.App.exe')
    if (-not (Test-Path -LiteralPath $exePath)) {
        $projectPath = Get-OrderlyAppProjectPath
        throw "Debug executable not found: $exePath`nBuild first from repo root: dotnet build `"$projectPath`" -c Debug"
    }

    return $exePath
}

function Get-ArtifactRoot {
    return Join-RepoPath @('artifacts')
}

function Get-QaSmokeRoot {
    return Join-RepoPath @('artifacts', 'qa-smoke')
}

function New-QaSmokeRunDirectory {
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $root = Get-QaSmokeRoot
    $path = Join-Path $root $timestamp
    New-Item -ItemType Directory -Path $path -Force | Out-Null

    return [pscustomobject]@{
        Timestamp = $timestamp
        Path      = $path
    }
}

function Get-DefaultDatabasePath {
    $root = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Orderly'
    return Join-Path $root 'orderly.db'
}

function Get-MicrosoftDataSqliteAssemblyPath {
    $assemblyPath = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows', 'Microsoft.Data.Sqlite.dll')
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "未找到 SQLite 驱动程序集：$assemblyPath"
    }

    return $assemblyPath
}

function Get-RunningOrderlyProcesses {
    return Get-Process -Name 'Orderly.App' -ErrorAction SilentlyContinue
}

function Assert-NoRunningOrderlyProcess {
    $running = @(Get-RunningOrderlyProcesses)
    if ($running.Count -gt 0) {
        $processIds = ($running | Select-Object -ExpandProperty Id) -join ', '
        throw "Detected running Orderly.App process(es): $processIds. Close them before running QA tools."
    }
}

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Save-Utf8Text {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    Set-Content -LiteralPath $Path -Value $Content -Encoding utf8
}

function Save-Utf8Json {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        $Value,
        [int]$Depth = 8
    )

    $json = $Value | ConvertTo-Json -Depth $Depth
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
}

function Invoke-OrderlyAppCommand {
    param(
        [string[]]$ArgumentList = @()
    )

    $exePath = Get-OrderlyAppExePath
    $stdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) ("orderly-qa-" + [guid]::NewGuid().ToString('N') + ".stdout.txt")
    $stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ("orderly-qa-" + [guid]::NewGuid().ToString('N') + ".stderr.txt")

    try {
        $process = Start-Process `
            -FilePath $exePath `
            -ArgumentList $ArgumentList `
            -Wait `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        $stdout = if (Test-Path -LiteralPath $stdoutPath) {
            $stdoutText = Get-Content -LiteralPath $stdoutPath -Raw -Encoding utf8
            if ([string]::IsNullOrEmpty($stdoutText)) { '' } else { $stdoutText.TrimEnd() }
        } else {
            ''
        }

        $stderr = if (Test-Path -LiteralPath $stderrPath) {
            $stderrText = Get-Content -LiteralPath $stderrPath -Raw -Encoding utf8
            if ([string]::IsNullOrEmpty($stderrText)) { '' } else { $stderrText.TrimEnd() }
        } else {
            ''
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut   = $stdout
            StdErr   = $stderr
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -ErrorAction SilentlyContinue
    }
}

function Invoke-OrderlyAppCommandOrThrow {
    param(
        [string[]]$ArgumentList = @()
    )

    $result = Invoke-OrderlyAppCommand -ArgumentList $ArgumentList
    if ($result.StdOut) {
        Write-Host $result.StdOut
    }

    if ($result.ExitCode -ne 0) {
        if ($result.StdErr) {
            Write-Error $result.StdErr
        }

        throw "Orderly.App 执行失败，退出码：$($result.ExitCode)"
    }

    if ($result.StdErr) {
        Write-Warning $result.StdErr
    }

    return $result
}
