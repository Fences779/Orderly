Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$script:QaEncryptionKeyBytes = $null

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
    if (-not [string]::IsNullOrWhiteSpace($env:ORDERLY_QA_ARTIFACT_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:ORDERLY_QA_ARTIFACT_ROOT)
    }

    return Join-Path ([System.IO.Path]::GetTempPath()) 'Orderly\artifacts'
}

function Get-QaDatabaseRoot {
    return Join-Path (Get-ArtifactRoot) 'qa-db'
}

function Get-QaSmokeRoot {
    return Join-Path (Get-ArtifactRoot) 'qa-smoke'
}

function New-QaSmokeRunDirectory {
    $timestamp = (Get-Date -Format 'yyyyMMdd_HHmmss_fff') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $root = Get-QaSmokeRoot
    $path = Join-Path $root $timestamp
    New-Item -ItemType Directory -Path $path -Force | Out-Null

    return [pscustomobject]@{
        Timestamp = $timestamp
        Path      = $path
    }
}

function Get-DefaultDatabasePath {
    if (-not [string]::IsNullOrWhiteSpace($env:ORDERLY_QA_DB_PATH)) {
        $overridePath = [System.IO.Path]::GetFullPath($env:ORDERLY_QA_DB_PATH)
        $overrideDir = [System.IO.Path]::GetDirectoryName($overridePath)
        if (-not [string]::IsNullOrWhiteSpace($overrideDir)) {
            [System.IO.Directory]::CreateDirectory($overrideDir) | Out-Null
        }

        return $overridePath
    }

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

function Import-OrderlyAssembliesForQa {
    param(
        [switch]$IncludeAppAssembly
    )

    $binRoot = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows')
    $nativeRuntimePath = Join-Path $binRoot 'runtimes\\win-x64\\native'
    if (Test-Path -LiteralPath $nativeRuntimePath) {
        $env:PATH = "$nativeRuntimePath;$binRoot;$env:PATH"
        if (-not ('QaNativeLoader' -as [type])) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class QaNativeLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);
}
"@
        }

        [QaNativeLoader]::SetDllDirectory($nativeRuntimePath) | Out-Null
        $nativeLibrary = Join-Path $nativeRuntimePath 'e_sqlcipher.dll'
        if ([QaNativeLoader]::LoadLibrary($nativeLibrary) -eq [IntPtr]::Zero) {
            throw "Failed to preload native SQLite library: $nativeLibrary"
        }
    }

    $assemblyNames = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlcipher.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll',
        'System.Security.Cryptography.ProtectedData.dll',
        'Orderly.Core.dll',
        'Orderly.Data.dll',
        'Orderly.Infrastructure.dll'
    )

    if ($IncludeAppAssembly) {
        $assemblyNames += 'Orderly.App.dll'
    }

    foreach ($assemblyName in $assemblyNames) {
        $assemblyPath = Join-Path $binRoot $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath)) {
            # ProtectedData ships as a transitive dependency; tolerate its absence and fall back to
            # the framework-provided type when present.
            if ($assemblyName -eq 'System.Security.Cryptography.ProtectedData.dll') {
                continue
            }

            throw "Missing QA dependency assembly: $assemblyPath"
        }

        [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
    }

    [SQLitePCL.Batteries_V2]::Init()
}

function New-PassthroughFieldEncryptionService {
    return [Orderly.Data.Services.PassthroughFieldEncryptionService]::Instance
}

function Get-QaEncryptionKeyBytes {
    if (-not $script:QaEncryptionKeyBytes) {
        $seedBytes = [System.Text.Encoding]::UTF8.GetBytes('Orderly-QA-Encryption-v1')
        $script:QaEncryptionKeyBytes = [System.Security.Cryptography.SHA256]::HashData($seedBytes)
    }

    return $script:QaEncryptionKeyBytes
}

# --- SQLCipher-compatible QA database access ---------------------------------------------------
#
# QA mode runs the product with full SQLCipher whole-database encryption: Orderly.App generates a
# random per-database "QA session data key", stores it DPAPI-protected, and uses it as the SQLCipher
# key when it creates/opens the QA database. QA scripts must therefore open that same encrypted
# database with the same key — a keyless Microsoft.Data.Sqlite connection raises
# "SQLite Error 26: file is not a database". The helpers below load the product's QA session data
# key exactly the way the app does (same file-path derivation, same DPAPI entropy) and build a keyed
# SqliteConnectionFactory, so the harness stays SQLCipher-compatible without weakening encryption and
# without ever printing the key. The key itself is never written to logs.

function Get-QaIdentityDirectory {
    $root = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Orderly'
    return Join-Path $root 'identity'
}

function Get-QaSessionDataKeyFilePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    # Mirrors App.GetQaSessionDataKeyPath: SHA-256 over the upper-cased full database path, hex-lower.
    $normalized = [System.IO.Path]::GetFullPath($DatabasePath).ToUpperInvariant()
    $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($normalized)
    $hash = [System.Security.Cryptography.SHA256]::HashData($pathBytes)
    $hex = [System.Convert]::ToHexString($hash).ToLowerInvariant()
    return Join-Path (Get-QaIdentityDirectory) ("qa-session-data-key-$hex.dpapi")
}

function Get-QaSessionDataKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $keyPath = Get-QaSessionDataKeyFilePath -DatabasePath $DatabasePath
    if (-not (Test-Path -LiteralPath $keyPath)) {
        return $null
    }

    $protected = [System.IO.File]::ReadAllBytes($keyPath)
    $entropy = [System.Text.Encoding]::UTF8.GetBytes('Orderly.QaSessionDataKey.v1')
    return [System.Security.Cryptography.ProtectedData]::Unprotect(
        $protected,
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
}

function New-QaConnectionFactory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $key = Get-QaSessionDataKey -DatabasePath $DatabasePath
    if ($null -eq $key -or $key.Length -eq 0) {
        # No QA SQLCipher key present: the database is unencrypted (e.g. created directly by a
        # script that does not run the app). Fall back to a keyless factory.
        return [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath)
    }

    # The SqliteConnectionFactory invokes the key provider lazily on connection open, which can occur
    # on a background (thread-pool) thread with no PowerShell runspace. A PowerShell scriptblock cast
    # to a delegate would fail there ("no Runspace available"), so use a tiny compiled .NET type whose
    # method we bind as a real delegate. It returns a fresh copy each call (the factory zeroes it).
    if (-not ('QaSqlCipherKeyProvider' -as [type])) {
        Add-Type @"
public sealed class QaSqlCipherKeyProvider
{
    private readonly byte[] _key;
    public QaSqlCipherKeyProvider(byte[] key)
    {
        _key = (byte[])key.Clone();
    }

    public byte[] Provide()
    {
        return (byte[])_key.Clone();
    }
}
"@
    }

    $provider = [QaSqlCipherKeyProvider]::new($key)
    $keyProvider = [System.Delegate]::CreateDelegate([Func[byte[]]], $provider, 'Provide')
    return [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($DatabasePath, $keyProvider)
}

function New-QaFieldEncryptionContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath,
        [string]$AccountId = 'qa-local-account',
        [string]$Username = 'qa-local-user',
        [string]$DisplayName = 'QA Local User'
    )

    $sessionContextService = [Orderly.Data.Services.SessionContextService]::new()

    # Field encryption in QA mode uses the same key the app's QA session uses (the SQLCipher QA
    # session data key) so script-side decryption matches app-written ciphertext. When no QA session
    # key exists yet (unencrypted DB created directly by a script), fall back to the deterministic
    # seed key for backward compatibility.
    $dataKey = Get-QaSessionDataKey -DatabasePath $DatabasePath
    if ($null -eq $dataKey -or $dataKey.Length -eq 0) {
        $dataKey = Get-QaEncryptionKeyBytes
    }

    $sessionContext = [Orderly.Core.Models.LocalSessionContext]@{
        AccountId   = $AccountId
        Username    = $Username
        DisplayName = $DisplayName
        Role        = [Orderly.Core.Models.LocalAccountRole]::Owner
        DatabasePath = $DatabasePath
        DataKey     = $dataKey
        SignedInAt  = [DateTime]::Now
    }
    $sessionContextService.SetCurrent($sessionContext)

    return [pscustomobject]@{
        SessionContextService = $sessionContextService
        FieldEncryptionService = [Orderly.Data.Services.FieldEncryptionService]::new($sessionContextService)
    }
}

function Invoke-QaCiphertextBackfill {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $DatabasePath
    $connectionFactory = New-QaConnectionFactory -DatabasePath $DatabasePath
    $migrationService = [Orderly.Data.Services.SensitiveFieldMigrationService]::new(
        $connectionFactory,
        $fieldContext.FieldEncryptionService)
    [void]$migrationService.BackfillAsync().GetAwaiter().GetResult()
}

function Remove-LegacyInvalidEncryptedActivityLogs {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DatabasePath
    )

    $commandText = @'
DELETE FROM ActivityLogs
WHERE
    (ifnull(TitleCiphertext, '') <> '' AND TitleCiphertext NOT LIKE 'v1:%')
 OR (ifnull(DescriptionCiphertext, '') <> '' AND DescriptionCiphertext NOT LIKE 'v1:%')
 OR (ifnull(OperatorCiphertext, '') <> '' AND OperatorCiphertext NOT LIKE 'v1:%')
 OR (ifnull(MetadataJsonCiphertext, '') <> '' AND MetadataJsonCiphertext NOT LIKE 'v1:%');
'@

    $connectionFactory = New-QaConnectionFactory -DatabasePath $DatabasePath
    $connection = $connectionFactory.CreateConnection()
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $commandText
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
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
