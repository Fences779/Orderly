[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [string]$RepoUrl = 'https://github.com/Fences779/Orderly',

    [string]$ReleaseNotesPath,

    [string]$GithubToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageId = 'Orderly'
$channel = 'stable'
$packageTitle = 'Orderly'
$mainExe = 'Orderly.App.exe'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$projectPath = Join-Path $repoRoot 'src\Orderly.App\Orderly.App.csproj'
$iconPath = Join-Path $repoRoot 'src\Orderly.App\Assets\Brand\Orderly Logo.ico'
$releaseRoot = Join-Path $repoRoot "artifacts\release\$Version"
$publishDir = Join-Path $releaseRoot 'publish'
$packageDir = Join-Path $releaseRoot 'packages'
$setupPath = Join-Path $releaseRoot 'Setup.exe'
$fileVersion = "$Version.0"

function New-ReleaseDirectory {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path | Out-Null
}

function Resolve-ReleaseNotesFile {
    param(
        [string]$Version,
        [string]$ReleaseRoot,
        [string]$ReleaseNotesPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $resolved = [System.IO.Path]::GetFullPath($ReleaseNotesPath)
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "Release notes file not found: $resolved"
        }

        return $resolved
    }

    $generatedPath = Join-Path $ReleaseRoot "release-notes-$Version.md"
    @(
        "# Orderly $Version"
        ""
        "- Windows installer and Velopack stable update release."
    ) | Set-Content -LiteralPath $generatedPath -Encoding UTF8

    return $generatedPath
}

function Try-DownloadPreviousReleases {
    param(
        [string]$PackageDir,
        [string]$RepoUrl,
        [string]$GithubToken
    )

    try {
        $downloadTimeoutMs = 60000
        $arguments = @(
            'tool', 'run', 'vpk', '--', 'download', 'github',
            '--outputDir', $PackageDir,
            '--channel', $channel,
            '--repoUrl', $RepoUrl
        )

        if (-not [string]::IsNullOrWhiteSpace($GithubToken)) {
            $arguments += @('--token', $GithubToken)
        }

        $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru
        if (-not $process.WaitForExit($downloadTimeoutMs)) {
            try {
                $process.Kill()
            }
            catch {
                Write-Host "Timed out while downloading previous release assets; failed to stop process cleanly. $($_.Exception.Message)"
            }

            throw ("vpk download github timed out after {0} seconds." -f [int]($downloadTimeoutMs / 1000))
        }

        if ($process.ExitCode -ne 0) {
            throw ("vpk download github exited with code {0}." -f $process.ExitCode)
        }
    }
    catch {
        Write-Host "Previous release assets were not downloaded; continuing as first/full release. $($_.Exception.Message)"
    }
}

function Assert-FileExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Test-TextFileContains {
    param(
        [string]$Path,
        [string]$Needle,
        [string]$Description
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if (-not $content.Contains($Needle)) {
        throw "$Description does not contain '$Needle': $Path"
    }
}

function Write-HashManifest {
    param(
        [string]$ReleaseRoot,
        [string[]]$Paths
    )

    $hashPath = Join-Path $ReleaseRoot 'SHA256SUMS.txt'
    $rows = foreach ($path in $Paths) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $path
        $relative = $path
        if ($path.StartsWith($ReleaseRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $path.Substring($ReleaseRoot.Length).TrimStart('\', '/')
        }

        "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), ($relative -replace '\\', '/')
    }

    $rows | Set-Content -LiteralPath $hashPath -Encoding ASCII
    return $hashPath
}

function Test-ReleaseArtifacts {
    param(
        [string]$Version,
        [string]$ReleaseRoot,
        [string]$PublishDir,
        [string]$PackageDir,
        [string]$SetupPath,
        [string]$MainExe
    )

    $exePath = Join-Path $PublishDir $MainExe
    Assert-FileExists -Path $exePath -Description 'Published main executable'

    $fileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    if ($fileVersionInfo.ProductVersion -ne $Version) {
        throw "Published exe ProductVersion mismatch. Expected $Version, actual $($fileVersionInfo.ProductVersion)."
    }

    if ($fileVersionInfo.FileVersion -ne "$Version.0") {
        throw "Published exe FileVersion mismatch. Expected $Version.0, actual $($fileVersionInfo.FileVersion)."
    }

    $nupkgPath = Join-Path $PackageDir "Orderly-$Version-stable-full.nupkg"
    $setupPackagePath = Join-Path $PackageDir 'Orderly-stable-Setup.exe'
    $releasesPath = Join-Path $PackageDir 'RELEASES-stable'
    $releasesJsonPath = Join-Path $PackageDir 'releases.stable.json'
    $assetsJsonPath = Join-Path $PackageDir 'assets.stable.json'

    Assert-FileExists -Path $nupkgPath -Description 'Velopack full package'
    Assert-FileExists -Path $setupPackagePath -Description 'Velopack setup package'
    Assert-FileExists -Path $setupPath -Description 'Copied setup package'
    Assert-FileExists -Path $releasesPath -Description 'RELEASES manifest'
    Assert-FileExists -Path $releasesJsonPath -Description 'releases JSON manifest'
    Assert-FileExists -Path $assetsJsonPath -Description 'assets JSON manifest'

    Test-TextFileContains -Path $releasesPath -Needle $Version -Description 'RELEASES manifest'
    Test-TextFileContains -Path $releasesJsonPath -Needle $Version -Description 'releases JSON manifest'
    Test-TextFileContains -Path $assetsJsonPath -Needle $Version -Description 'assets JSON manifest'

    return Write-HashManifest -ReleaseRoot $ReleaseRoot -Paths @(
        $setupPath,
        $setupPackagePath,
        $nupkgPath,
        $releasesPath,
        $releasesJsonPath,
        $assetsJsonPath
    )
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Icon file not found: $iconPath"
}

New-ReleaseDirectory -Path $releaseRoot
New-Item -ItemType Directory -Path $publishDir | Out-Null
New-Item -ItemType Directory -Path $packageDir | Out-Null

$releaseNotesFile = Resolve-ReleaseNotesFile -Version $Version -ReleaseRoot $releaseRoot -ReleaseNotesPath $ReleaseNotesPath

Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw ("dotnet tool restore failed with exit code {0}." -f $LASTEXITCODE)
    }

    & dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir `
        -p:Version=$Version `
        -p:FileVersion=$fileVersion `
        -p:AssemblyVersion=$fileVersion `
        -p:InformationalVersion=$Version `
        -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) {
        throw ("dotnet publish failed with exit code {0}." -f $LASTEXITCODE)
    }

    Try-DownloadPreviousReleases -PackageDir $packageDir -RepoUrl $RepoUrl -GithubToken $GithubToken

    & dotnet tool run vpk -- pack `
        --outputDir $packageDir `
        --channel $channel `
        --runtime $Runtime `
        --packId $packageId `
        --packVersion $Version `
        --packDir $publishDir `
        --packTitle $packageTitle `
        --mainExe $mainExe `
        --icon $iconPath `
        --releaseNotes $releaseNotesFile `
        --noPortable true
    if ($LASTEXITCODE -ne 0) {
        throw ("vpk pack failed with exit code {0}." -f $LASTEXITCODE)
    }

    $setupArtifact = Get-ChildItem -Path $packageDir -Recurse -Filter '*Setup*.exe' |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $setupArtifact) {
        throw "Velopack setup installer was not found in $packageDir."
    }

    Copy-Item -LiteralPath $setupArtifact.FullName -Destination $setupPath -Force

    $hashManifest = Test-ReleaseArtifacts -Version $Version -ReleaseRoot $releaseRoot -PublishDir $publishDir -PackageDir $packageDir -SetupPath $setupPath -MainExe $mainExe

    Write-Host "Release root: $releaseRoot"
    Write-Host "Setup artifact: $setupPath"
    Write-Host "Velopack output: $packageDir"
    Write-Host "SHA256 manifest: $hashManifest"
}
finally {
    Pop-Location
}
