[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedTestUserName,

    [switch]$UninstallVerify,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PackageId = 'Orderly'
$Channel = 'stable'
$SourceVersion = '0.1.1'
$TargetVersion = '0.1.2'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$DesktopPath = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($DesktopPath)) {
    $DesktopPath = $env:USERPROFILE
}

$ReportPath = Join-Path $DesktopPath 'Orderly-E2E-Report.txt'
$InstallRoot = Join-Path $env:LOCALAPPDATA 'Orderly'
$InstallCurrentDir = Join-Path $InstallRoot 'current'
$DataRoot = Join-Path $env:LOCALAPPDATA 'OrderlyData'
$ExpectedUpdateSource = Join-Path $RepoRoot 'artifacts\release\0.1.2\packages'
$Release011Root = Join-Path $RepoRoot 'artifacts\release\0.1.1'
$Release012Root = Join-Path $RepoRoot 'artifacts\release\0.1.2'
$Release011Packages = Join-Path $Release011Root 'packages'
$Release012Packages = Join-Path $Release012Root 'packages'
$InstallLogPath = Join-Path $DesktopPath 'Orderly-E2E-Install.log'
$UpdateLaunchLogPath = Join-Path $DesktopPath 'Orderly-E2E-AppLaunch.log'

$script:CurrentStep = '初始化'
$script:FailureSuggestion = '查看报告中的失败步骤、原始异常和对象字段清单后重跑。'
$script:FinalResult = 'FAIL'

function Get-CurrentUserLeaf {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    if ($identity.Contains('\')) {
        return $identity.Split('\')[-1]
    }

    return $identity
}

function ConvertTo-ObjectArray {
    param([object]$InputObject)

    if ($null -eq $InputObject) {
        return @()
    }

    if ($InputObject -is [string]) {
        return @($InputObject)
    }

    if ($InputObject -is [System.Array]) {
        return @($InputObject)
    }

    if ($InputObject -is [System.Collections.IEnumerable]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $InputObject) {
            [void]$items.Add($item)
        }

        return @($items.ToArray())
    }

    return @($InputObject)
}

function Get-SafePropertyValue {
    param(
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [object]$DefaultValue = $null
    )

    if ($null -eq $InputObject) {
        return $DefaultValue
    }

    if ($InputObject -is [Microsoft.Win32.RegistryKey]) {
        $value = $InputObject.GetValue($Name, $null)
        if ($null -eq $value) {
            return $DefaultValue
        }

        return $value
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) {
            $value = $InputObject[$Name]
            if ($null -eq $value) {
                return $DefaultValue
            }

            return $value
        }

        return $DefaultValue
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }

    if ($null -eq $property.Value) {
        return $DefaultValue
    }

    return $property.Value
}

function Get-ObjectPropertyNames {
    param([object]$InputObject)

    if ($null -eq $InputObject) {
        return @()
    }

    if ($InputObject -is [Microsoft.Win32.RegistryKey]) {
        return @($InputObject.GetValueNames())
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        return @($InputObject.Keys)
    }

    return @($InputObject.PSObject.Properties | ForEach-Object { $_.Name })
}

function Format-SafeValue {
    param([object]$Value)

    if ($null -eq $Value) {
        return 'N/A'
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return 'N/A'
    }

    return Protect-ReportText -Text $text
}

function Protect-ReportText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $Text
    }

    $protected = $Text
    $pathReplacements = @{}

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $pathReplacements[$env:USERPROFILE] = '<USERPROFILE>'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $pathReplacements[$env:LOCALAPPDATA] = '<LOCALAPPDATA>'
    }

    if (-not [string]::IsNullOrWhiteSpace($DesktopPath)) {
        $pathReplacements[$DesktopPath] = '<DESKTOP>'
    }

    if (-not [string]::IsNullOrWhiteSpace($RepoRoot)) {
        $pathReplacements[$RepoRoot] = '<REPO_ROOT>'
    }

    foreach ($entry in $pathReplacements.GetEnumerator()) {
        $escaped = [regex]::Escape([string]$entry.Key)
        $protected = [regex]::Replace($protected, $escaped, [string]$entry.Value, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTestUserName)) {
        $escapedUserName = [regex]::Escape($ExpectedTestUserName)
        $protected = [regex]::Replace($protected, $escapedUserName, '<EXPECTED_TEST_USER>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }

    return $protected
}

function Write-ReportLine {
    param([string]$Message)

    $safeMessage = Protect-ReportText -Text $Message
    Write-Host $safeMessage
    Add-Content -LiteralPath $ReportPath -Encoding UTF8 -Value $safeMessage
}

function Write-ObjectDiagnostic {
    param(
        [object]$InputObject,
        [string]$Title
    )

    Write-ReportLine $Title
    if ($null -eq $InputObject) {
        Write-ReportLine '  <null>'
        return
    }

    Write-ReportLine ("  Type={0}" -f $InputObject.GetType().FullName)
    $names = @(Get-ObjectPropertyNames -InputObject $InputObject | Sort-Object -Unique)
    if ($names.Count -eq 0) {
        Write-ReportLine '  Fields=<none>'
        return
    }

    foreach ($name in $names) {
        $value = Get-SafePropertyValue -InputObject $InputObject -Name ([string]$name) -DefaultValue 'N/A'
        Write-ReportLine ("  {0}={1}" -f $name, (Format-SafeValue $value))
    }
}

function Assert-RequiredProperty {
    param(
        [object]$InputObject,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $value = Get-SafePropertyValue -InputObject $InputObject -Name $Name -DefaultValue $null
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        Write-ObjectDiagnostic -InputObject $InputObject -Title "对象字段诊断：$Context"
        throw "$Context 缺少关键字段：$Name"
    }

    return $value
}

function Fail-Step {
    param(
        [string]$Message,
        [string]$Suggestion = $null
    )

    if (-not [string]::IsNullOrWhiteSpace($Suggestion)) {
        $script:FailureSuggestion = $Suggestion
    }

    Write-ReportLine "FAIL: $Message"
    throw $Message
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    $script:CurrentStep = $Name
    Write-ReportLine ''
    Write-ReportLine "STEP: $Name"
    & $Action
}

function Initialize-Report {
    $mode = 'InstallAndUpgrade'
    if ($UninstallVerify) {
        $mode = 'UninstallVerify'
    }
    elseif ($ValidateOnly) {
        $mode = 'ValidateOnly'
    }

    $reportDir = Split-Path -Path $ReportPath -Parent
    if (-not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir | Out-Null
    }

    $header = @(
        'Orderly Local E2E Report'
        "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Mode: $mode"
        ''
    )

    $header | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

function Write-BasicDiagnostics {
    $scriptHash = Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath
    Write-ReportLine 'CurrentUser=<CURRENT_USER>'
    Write-ReportLine 'whoami=<CURRENT_IDENTITY>'
    Write-ReportLine 'USERPROFILE=<USERPROFILE>'
    Write-ReportLine 'LOCALAPPDATA=<LOCALAPPDATA>'
    Write-ReportLine "ScriptPath=$PSCommandPath"
    Write-ReportLine "ScriptSHA256=$($scriptHash.Hash)"
    Write-ReportLine "SourceVersion=$SourceVersion"
    Write-ReportLine "TargetVersion=$TargetVersion"
    Write-ReportLine "ExpectedTestUserName=$ExpectedTestUserName"
    Write-ReportLine "InstallRoot=$InstallRoot"
    Write-ReportLine "InstallCurrentDir=$InstallCurrentDir"
    Write-ReportLine "DataRoot=$DataRoot"
    Write-ReportLine "InstallLogPath=$InstallLogPath"
    Write-ReportLine "UpdateLaunchLogPath=$UpdateLaunchLogPath"
    Write-ReportLine "ExpectedUpdateSource=$ExpectedUpdateSource"
}

function Assert-TestAccountContext {
    $whoamiValue = (whoami).Trim()
    $userLeaf = Get-CurrentUserLeaf
    $profileLeaf = Split-Path -Path $env:USERPROFILE -Leaf
    $expectedLocalAppData = Join-Path $env:USERPROFILE 'AppData\Local'

    Write-ReportLine "whoami=$whoamiValue"
    Write-ReportLine "USERPROFILE=$env:USERPROFILE"
    Write-ReportLine "LOCALAPPDATA=$env:LOCALAPPDATA"

    if (-not [string]::Equals($userLeaf, $ExpectedTestUserName, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Step '当前用户不是指定的临时测试用户，禁止真实 E2E。' `
            "请切换到参数 -ExpectedTestUserName 指定的临时测试账号后运行真实安装/更新/卸载验收；其他账号只能运行 -ValidateOnly。"
    }

    if (-not [string]::Equals($profileLeaf, $ExpectedTestUserName, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Step 'USERPROFILE 与指定的临时测试用户不一致。'
    }

    if (-not [string]::Equals(
        [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\'),
        [System.IO.Path]::GetFullPath($expectedLocalAppData).TrimEnd('\'),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Step "LOCALAPPDATA 与当前测试账号不匹配：$env:LOCALAPPDATA"
    }

    Write-ReportLine 'OK: 测试账号上下文校验通过'
}

function Assert-ExecutionModeAllowed {
    if ($ValidateOnly) {
        Write-ReportLine 'OK: ValidateOnly 模式，不会安装、启动、卸载或删除 Orderly。'
        return
    }

    Assert-TestAccountContext
}

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Fail-Step "$Description 不存在：$Path"
    }

    Write-ReportLine "OK: $Description -> $Path"
}

function Assert-FileContains {
    param(
        [string]$Path,
        [string]$Needle,
        [string]$Description
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if (-not $content.Contains($Needle)) {
        Fail-Step "$Description 未包含 '$Needle'：$Path"
    }

    Write-ReportLine "OK: $Description 包含 '$Needle'"
}

function Assert-HashManifestEntry {
    param(
        [string]$ManifestPath,
        [string]$ReleaseRoot,
        [string]$RelativePath
    )

    $targetPath = Join-Path $ReleaseRoot $RelativePath
    Assert-PathExists -Path $targetPath -Description "SHA256 文件 $RelativePath"
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash.ToLowerInvariant()
    $normalizedRelativePath = $RelativePath -replace '\\', '/'
    $content = Get-Content -LiteralPath $ManifestPath -Raw
    $expectedLine = "$hash  $normalizedRelativePath"
    if (-not $content.Contains($expectedLine)) {
        Fail-Step "SHA256SUMS 未匹配 $RelativePath"
    }

    Write-ReportLine "OK: SHA256 $RelativePath -> $hash"
}

function Assert-ReleaseArtifacts {
    param(
        [string]$Version,
        [string]$ReleaseRoot,
        [string]$PackageRoot
    )

    Write-ReportLine "检查发布资产：$Version"
    $setupPath = Join-Path $ReleaseRoot 'Setup.exe'
    $manifestPath = Join-Path $ReleaseRoot 'SHA256SUMS.txt'
    $nupkgName = "Orderly-$Version-stable-full.nupkg"

    Assert-PathExists -Path $setupPath -Description "$Version Setup.exe"
    Assert-PathExists -Path $manifestPath -Description "$Version SHA256SUMS.txt"
    Assert-PathExists -Path (Join-Path $PackageRoot $nupkgName) -Description "$Version full nupkg"
    Assert-PathExists -Path (Join-Path $PackageRoot 'Orderly-stable-Setup.exe') -Description "$Version packages Setup.exe"
    Assert-PathExists -Path (Join-Path $PackageRoot 'RELEASES-stable') -Description "$Version RELEASES-stable"
    Assert-PathExists -Path (Join-Path $PackageRoot 'releases.stable.json') -Description "$Version releases.stable.json"
    Assert-PathExists -Path (Join-Path $PackageRoot 'assets.stable.json') -Description "$Version assets.stable.json"
    Assert-FileContains -Path $manifestPath -Needle 'Setup.exe' -Description "$Version SHA256SUMS"
    Assert-FileContains -Path $manifestPath -Needle $nupkgName -Description "$Version SHA256SUMS"
    Assert-HashManifestEntry -ManifestPath $manifestPath -ReleaseRoot $ReleaseRoot -RelativePath 'Setup.exe'
    Assert-HashManifestEntry -ManifestPath $manifestPath -ReleaseRoot $ReleaseRoot -RelativePath "packages\$nupkgName"
}

function Resolve-UpdateSourceInput {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw '更新源为空。'
    }

    $trimmed = $Value.Trim()
    $absoluteUri = $null
    if ([System.Uri]::TryCreate($trimmed, [System.UriKind]::Absolute, [ref]$absoluteUri)) {
        if ([string]::Equals($absoluteUri.Scheme, [System.Uri]::UriSchemeHttp, [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($absoluteUri.Scheme, [System.Uri]::UriSchemeHttps, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{
                Kind = 'Web'
                Original = $trimmed
                ResolvedPath = $trimmed
            }
        }

        if ($absoluteUri.IsFile) {
            return [pscustomobject]@{
                Kind = 'Local'
                Original = $trimmed
                ResolvedPath = [System.IO.Path]::GetFullPath($absoluteUri.LocalPath)
            }
        }
    }

    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        return [pscustomobject]@{
            Kind = 'Local'
            Original = $trimmed
            ResolvedPath = [System.IO.Path]::GetFullPath($trimmed)
        }
    }

    throw "更新源无效，仅支持 http/https、本地绝对路径或 file URI：$Value"
}

function Assert-LocalUpdateSource {
    param([string]$SourcePath)

    $resolved = Resolve-UpdateSourceInput -Value $SourcePath
    if ($resolved.Kind -ne 'Local') {
        Fail-Step "E2E 更新源必须是本地目录：$SourcePath"
    }

    Assert-PathExists -Path $resolved.ResolvedPath -Description '本地更新源目录'
    Assert-PathExists -Path (Join-Path $resolved.ResolvedPath 'releases.stable.json') -Description '本地更新源 releases.stable.json'
    Assert-PathExists -Path (Join-Path $resolved.ResolvedPath 'RELEASES-stable') -Description '本地更新源 RELEASES-stable'
    Write-ReportLine "OK: 本地更新源解析 -> $($resolved.ResolvedPath)"
}

function Stop-OrderlyProcessesUnderInstallRoot {
    $targets = @(Get-Process | Where-Object {
        try {
            $processPath = Get-SafePropertyValue -InputObject $_ -Name 'Path' -DefaultValue $null
            if ($null -eq $processPath) {
                return $false
            }

            return ([string]$processPath).StartsWith($InstallRoot, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            return $false
        }
    })

    foreach ($process in $targets) {
        Write-ReportLine "停止进程：$($process.ProcessName) PID=$($process.Id)"
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
    }
}

function Remove-InstallRootOnly {
    Assert-TestAccountContext

    $expectedInstallRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Orderly')).TrimEnd('\')
    $actualInstallRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
    if (-not [string]::Equals($actualInstallRoot, $expectedInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Step "安装目录保护失败：$actualInstallRoot"
    }

    Stop-OrderlyProcessesUnderInstallRoot

    if (Test-Path -LiteralPath $InstallRoot) {
        Write-ReportLine "清理测试账号旧安装残留：$InstallRoot"
        Remove-Item -LiteralPath $InstallRoot -Recurse -Force
    }
    else {
        Write-ReportLine "安装目录不存在，无需清理：$InstallRoot"
    }

    if (Test-Path -LiteralPath $DataRoot) {
        Write-ReportLine "保留业务数据目录：$DataRoot"
    }
    else {
        Write-ReportLine "业务数据目录当前不存在：$DataRoot"
    }
}

function Get-StartMenuShortcutPaths {
    $startMenuPrograms = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    if (-not (Test-Path -LiteralPath $startMenuPrograms)) {
        return @()
    }

    $items = @(Get-ChildItem -LiteralPath $startMenuPrograms -Filter 'Orderly*.lnk' -Recurse -ErrorAction SilentlyContinue)
    return @($items | ForEach-Object { $_.FullName })
}

function Get-ShortcutInfo {
    param([string]$Path)

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    return [pscustomobject]@{
        Path = $Path
        TargetPath = Get-SafePropertyValue -InputObject $shortcut -Name 'TargetPath' -DefaultValue 'N/A'
        Arguments = Get-SafePropertyValue -InputObject $shortcut -Name 'Arguments' -DefaultValue 'N/A'
        WorkingDirectory = Get-SafePropertyValue -InputObject $shortcut -Name 'WorkingDirectory' -DefaultValue 'N/A'
    }
}

function Get-UninstallEntries {
    $roots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($root in $roots) {
        $rawEntries = @(Get-ItemProperty -Path $root -ErrorAction SilentlyContinue)
        foreach ($entry in $rawEntries) {
            $displayName = [string](Get-SafePropertyValue -InputObject $entry -Name 'DisplayName' -DefaultValue '')
            $installLocation = [string](Get-SafePropertyValue -InputObject $entry -Name 'InstallLocation' -DefaultValue '')
            $uninstallString = [string](Get-SafePropertyValue -InputObject $entry -Name 'UninstallString' -DefaultValue '')
            $quietUninstallString = [string](Get-SafePropertyValue -InputObject $entry -Name 'QuietUninstallString' -DefaultValue '')
            $psPath = [string](Get-SafePropertyValue -InputObject $entry -Name 'PSPath' -DefaultValue $root)

            $matchesOrderly = [string]::Equals($displayName, $PackageId, [System.StringComparison]::OrdinalIgnoreCase)
            if (-not $matchesOrderly -and -not [string]::IsNullOrWhiteSpace($installLocation)) {
                $matchesOrderly = $installLocation.StartsWith($InstallRoot, [System.StringComparison]::OrdinalIgnoreCase)
            }
            if (-not $matchesOrderly -and -not [string]::IsNullOrWhiteSpace($uninstallString)) {
                $matchesOrderly = $uninstallString.IndexOf('Orderly', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }
            if (-not $matchesOrderly -and -not [string]::IsNullOrWhiteSpace($quietUninstallString)) {
                $matchesOrderly = $quietUninstallString.IndexOf('Orderly', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            }

            if ($matchesOrderly) {
                $entry | Add-Member -NotePropertyName 'OrderlyRegistrySource' -NotePropertyValue $psPath -Force
                [void]$entries.Add($entry)
            }
        }
    }

    return @($entries.ToArray())
}

function Write-UninstallEntryReport {
    param([object[]]$Entries)

    if ($null -eq $Entries -or $Entries.Count -eq 0) {
        Write-ReportLine '卸载注册表项：<none>'
        return
    }

    $index = 0
    foreach ($entry in $Entries) {
        $index++
        Write-ObjectDiagnostic -InputObject $entry -Title "卸载注册表项 #$index"
    }
}

function Assert-UninstallEntriesUsable {
    param([object[]]$Entries)

    if ($null -eq $Entries -or $Entries.Count -eq 0) {
        Fail-Step '未找到卸载注册表项。' '确认 0.1.1 Setup.exe 已在测试账号下安装成功，并检查 HKCU/HKLM 卸载项。'
    }

    $usableCount = 0
    foreach ($entry in $Entries) {
        [void](Assert-RequiredProperty -InputObject $entry -Name 'DisplayName' -Context '卸载注册表项')
        $uninstallString = Get-SafePropertyValue -InputObject $entry -Name 'UninstallString' -DefaultValue $null
        $quietUninstallString = Get-SafePropertyValue -InputObject $entry -Name 'QuietUninstallString' -DefaultValue $null
        if (-not [string]::IsNullOrWhiteSpace([string]$uninstallString) -or -not [string]::IsNullOrWhiteSpace([string]$quietUninstallString)) {
            $usableCount++
        }

        Write-ReportLine ("OK: 卸载项摘要 -> DisplayName={0}; DisplayVersion={1}; Publisher={2}; InstallLocation={3}; UninstallString={4}" -f `
            (Format-SafeValue (Get-SafePropertyValue -InputObject $entry -Name 'DisplayName' -DefaultValue 'N/A')),
            (Format-SafeValue (Get-SafePropertyValue -InputObject $entry -Name 'DisplayVersion' -DefaultValue 'N/A')),
            (Format-SafeValue (Get-SafePropertyValue -InputObject $entry -Name 'Publisher' -DefaultValue 'N/A')),
            (Format-SafeValue (Get-SafePropertyValue -InputObject $entry -Name 'InstallLocation' -DefaultValue 'N/A')),
            (Format-SafeValue (Get-SafePropertyValue -InputObject $entry -Name 'UninstallString' -DefaultValue 'N/A')))
    }

    if ($usableCount -eq 0) {
        Fail-Step '卸载注册表项缺少可执行卸载命令。'
    }
}

function Get-InstalledProductVersion {
    param([string]$ExePath = (Join-Path $InstallCurrentDir 'Orderly.App.exe'))

    $exePath = $ExePath
    Assert-PathExists -Path $exePath -Description '版本检查主程序'
    $version = [string]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion)
    if ([string]::IsNullOrWhiteSpace($version)) {
        Fail-Step "无法从主程序读取 ProductVersion：$exePath"
    }

    return [string]$version
}

function Test-InstalledVersionEquals {
    param([string]$ExpectedVersion)

    $exePath = Join-Path $InstallCurrentDir 'Orderly.App.exe'
    if (-not (Test-Path -LiteralPath $exePath)) {
        return $false
    }

    $version = [string]([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion)
    if ([string]::IsNullOrWhiteSpace($version)) {
        return $false
    }

    return [bool]([string]::Equals($version, $ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase))
}

function Get-InstallState {
    param(
        [string]$RootPath,
        [string]$CurrentPath
    )

    $updateExe = Join-Path $RootPath 'Update.exe'
    $mainExe = Join-Path $CurrentPath 'Orderly.App.exe'
    return [pscustomobject]@{
        InstallRootExists = Test-Path -LiteralPath $RootPath
        CurrentDirExists = Test-Path -LiteralPath $CurrentPath
        UpdateExeExists = Test-Path -LiteralPath $updateExe
        MainExeExists = Test-Path -LiteralPath $mainExe
    }
}

function Assert-InstalledLayout {
    param([string]$ExpectedVersion)

    Assert-PathExists -Path $InstallRoot -Description '安装根目录'
    Assert-PathExists -Path $InstallCurrentDir -Description 'current 目录'
    Assert-PathExists -Path (Join-Path $InstallCurrentDir 'Orderly.App.exe') -Description '已安装主程序'
    Assert-PathExists -Path (Join-Path $InstallRoot 'Update.exe') -Description 'Velopack Update.exe'

    $shortcuts = @(Get-StartMenuShortcutPaths)
    if ($shortcuts.Count -eq 0) {
        Fail-Step '未找到开始菜单快捷方式。'
    }

    foreach ($shortcut in $shortcuts) {
        $info = Get-ShortcutInfo -Path $shortcut
        Write-ObjectDiagnostic -InputObject $info -Title '开始菜单快捷方式'
    }

    $entries = @(Get-UninstallEntries)
    Write-UninstallEntryReport -Entries $entries
    Assert-UninstallEntriesUsable -Entries $entries

    $productVersion = Get-InstalledProductVersion
    if (-not [string]::Equals($productVersion, $ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Step "当前安装版本不匹配。期望：$ExpectedVersion，实际：$productVersion"
    }

    Write-ReportLine "OK: 当前安装版本 -> $productVersion"
}

function Invoke-SetupInstall {
    param([string]$SetupPath)

    Write-ReportLine "开始静默安装：$SetupPath"
    $process = Start-Process -FilePath $SetupPath -ArgumentList @('--silent', '--log', $InstallLogPath) -Wait -PassThru
    Write-ReportLine "安装进程退出码：$($process.ExitCode)"
    if ($process.ExitCode -ne 0) {
        Fail-Step "静默安装失败，退出码：$($process.ExitCode)。日志：$InstallLogPath"
    }

    Assert-PathExists -Path $InstallLogPath -Description '安装日志'
}

function Start-InstalledAppWithLocalUpdateSource {
    $exePath = Join-Path $InstallCurrentDir 'Orderly.App.exe'
    Assert-PathExists -Path $exePath -Description '启动用已安装主程序'
    Assert-LocalUpdateSource -SourcePath $ExpectedUpdateSource

    Write-ReportLine "以进程级环境变量启动 $SourceVersion，更新源=$ExpectedUpdateSource"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exePath
    $psi.WorkingDirectory = $InstallCurrentDir
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['ORDERLY_UPDATE_SOURCE_URL'] = $ExpectedUpdateSource
    $psi.EnvironmentVariables['ORDERLY_E2E_LAUNCH_LOG'] = $UpdateLaunchLogPath
    $process = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $process) {
        Fail-Step '启动已安装应用失败。'
    }

    Write-ReportLine "OK: 已启动应用 PID=$($process.Id)"
}

function Assert-NoBusinessDataInsideInstallRoot {
    $forbiddenHits = New-Object System.Collections.Generic.List[string]
    $forbiddenRelativePaths = @(
        'identity',
        'accounts',
        'avatars',
        'current\identity',
        'current\accounts',
        'current\avatars',
        'launcher.db',
        'orderly.db',
        'current\launcher.db',
        'current\orderly.db'
    )

    foreach ($relativePath in $forbiddenRelativePaths) {
        $targetPath = Join-Path $InstallRoot $relativePath
        if (Test-Path -LiteralPath $targetPath) {
            [void]$forbiddenHits.Add($targetPath)
        }
    }

    $keyFiles = @(Get-ChildItem -LiteralPath $InstallRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.key', '.keys', '.dpapi') })
    foreach ($file in $keyFiles) {
        [void]$forbiddenHits.Add($file.FullName)
    }

    if ($forbiddenHits.Count -gt 0) {
        Fail-Step ("安装目录内发现业务数据/头像/密钥残留：" + [Environment]::NewLine + ($forbiddenHits -join [Environment]::NewLine))
    }

    Write-ReportLine 'OK: 安装目录未发现业务数据库、头像、密钥或用户设置残留'
}

function Start-And-VerifyRelaunch {
    $exePath = Join-Path $InstallCurrentDir 'Orderly.App.exe'
    Assert-PathExists -Path $exePath -Description '复启动作主程序'

    $process = Start-Process -FilePath $exePath -WorkingDirectory $InstallCurrentDir -PassThru
    Start-Sleep -Seconds 8
    if ($process.HasExited) {
        Fail-Step "升级后应用未能保持启动，退出码：$($process.ExitCode)"
    }

    Write-ReportLine "OK: 升级后应用可再次启动，PID=$($process.Id)"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

function Parse-CommandLine {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        throw '命令行为空。'
    }

    $trimmed = $CommandLine.Trim()
    if ($trimmed.StartsWith('"')) {
        $closingQuote = $trimmed.IndexOf('"', 1)
        if ($closingQuote -lt 1) {
            throw "无法解析命令行：$CommandLine"
        }

        return [pscustomobject]@{
            FilePath = $trimmed.Substring(1, $closingQuote - 1)
            Arguments = $trimmed.Substring($closingQuote + 1).Trim()
        }
    }

    $firstSpace = $trimmed.IndexOf(' ')
    if ($firstSpace -lt 0) {
        return [pscustomobject]@{
            FilePath = $trimmed
            Arguments = ''
        }
    }

    return [pscustomobject]@{
        FilePath = $trimmed.Substring(0, $firstSpace)
        Arguments = $trimmed.Substring($firstSpace + 1).Trim()
    }
}

function Select-UninstallCommand {
    param([object[]]$Entries)

    foreach ($entry in $Entries) {
        $quiet = Get-SafePropertyValue -InputObject $entry -Name 'QuietUninstallString' -DefaultValue $null
        if (-not [string]::IsNullOrWhiteSpace([string]$quiet)) {
            return [string]$quiet
        }
    }

    foreach ($entry in $Entries) {
        $normal = Get-SafePropertyValue -InputObject $entry -Name 'UninstallString' -DefaultValue $null
        if (-not [string]::IsNullOrWhiteSpace([string]$normal)) {
            return [string]$normal
        }
    }

    $fallbackUpdateExe = Join-Path $InstallRoot 'Update.exe'
    Assert-PathExists -Path $fallbackUpdateExe -Description '卸载回退 Update.exe'
    return '"' + $fallbackUpdateExe + '" uninstall --silent'
}

function Invoke-UninstallVerification {
    Assert-TestAccountContext
    Stop-OrderlyProcessesUnderInstallRoot

    $entries = @(Get-UninstallEntries)
    Write-UninstallEntryReport -Entries $entries
    Assert-UninstallEntriesUsable -Entries $entries

    $commandText = Select-UninstallCommand -Entries $entries
    $parsed = Parse-CommandLine -CommandLine $commandText
    Write-ReportLine "执行卸载命令：$($parsed.FilePath) $($parsed.Arguments)"
    $process = Start-Process -FilePath $parsed.FilePath -ArgumentList $parsed.Arguments -Wait -PassThru
    Write-ReportLine "卸载进程退出码：$($process.ExitCode)"
    if ($process.ExitCode -ne 0) {
        Fail-Step "卸载失败，退出码：$($process.ExitCode)"
    }

    Start-Sleep -Seconds 3

    $shortcuts = @(Get-StartMenuShortcutPaths)
    if ($shortcuts.Count -gt 0) {
        Fail-Step ("卸载后开始菜单快捷方式仍存在：" + ($shortcuts -join ', '))
    }

    $remainingEntries = @(Get-UninstallEntries)
    if ($remainingEntries.Count -gt 0) {
        Write-UninstallEntryReport -Entries $remainingEntries
        Fail-Step '卸载后注册表卸载项仍存在。'
    }

    if (Test-Path -LiteralPath $InstallRoot) {
        Fail-Step "卸载后程序目录仍存在：$InstallRoot"
    }

    if (-not (Test-Path -LiteralPath $DataRoot)) {
        Fail-Step "卸载后业务数据目录不应丢失：$DataRoot"
    }

    Write-ReportLine 'OK: 卸载后开始菜单、卸载项、程序目录均已清除'
    Write-ReportLine "OK: 卸载后业务数据目录仍保留 -> $DataRoot"
}

function Test-StrictModeEnabled {
    try {
        $sample = [pscustomobject]@{}
        [void]$sample.DoesNotExist
        return $false
    }
    catch {
        return $true
    }
}

function Invoke-ObjectShapeSelfTest {
    $zero = @(ConvertTo-ObjectArray -InputObject $null)
    if ($zero.Count -ne 0) {
        throw 'ConvertTo-ObjectArray 未正确处理 0 个对象。'
    }

    $one = @(ConvertTo-ObjectArray -InputObject ([pscustomobject]@{ DisplayName = 'Orderly' }))
    if ($one.Count -ne 1) {
        throw 'ConvertTo-ObjectArray 未正确处理 1 个对象。'
    }

    $many = @(ConvertTo-ObjectArray -InputObject @(
        [pscustomobject]@{ DisplayName = 'Orderly' },
        [pscustomobject]@{ DisplayName = 'Orderly 2' }
    ))
    if ($many.Count -ne 2) {
        throw 'ConvertTo-ObjectArray 未正确处理多个对象。'
    }

    $missingInstallLocation = [pscustomobject]@{
        DisplayName = 'Orderly'
        UninstallString = '"<LOCALAPPDATA>\Orderly\Update.exe" uninstall --silent'
    }
    $installLocation = Get-SafePropertyValue -InputObject $missingInstallLocation -Name 'InstallLocation' -DefaultValue 'N/A'
    if ($installLocation -ne 'N/A') {
        throw '缺 InstallLocation 对象未返回 N/A。'
    }

    $displayVersion = Get-SafePropertyValue -InputObject $missingInstallLocation -Name 'DisplayVersion' -DefaultValue 'N/A'
    if ($displayVersion -ne 'N/A') {
        throw '缺 DisplayVersion 对象未返回 N/A。'
    }

    $registryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software')
    try {
        $registryValue = Get-SafePropertyValue -InputObject $registryKey -Name 'InstallLocation' -DefaultValue 'N/A'
        if ($registryValue -ne 'N/A') {
            throw 'RegistryKey 缺失字段未返回 N/A。'
        }
    }
    finally {
        if ($null -ne $registryKey) {
            $registryKey.Close()
        }
    }

    $shortcutLike = [pscustomobject]@{
        TargetPath = '<LOCALAPPDATA>\Orderly\current\Orderly.App.exe'
    }
    $arguments = Get-SafePropertyValue -InputObject $shortcutLike -Name 'Arguments' -DefaultValue 'N/A'
    if ($arguments -ne 'N/A') {
        throw '快捷方式缺 Arguments 对象未返回 N/A。'
    }

    Write-ReportLine 'OK: 对象形状自检覆盖 0/1/多对象、缺字段、RegistryKey、PSCustomObject、快捷方式字段。'
}

function Invoke-InstallStateSelfTest {
    $syntheticRoot = Join-Path $env:TEMP 'OrderlyE2E-ValidateOnly-SyntheticInstall'
    $syntheticCurrent = Join-Path $syntheticRoot 'current'
    $state = Get-InstallState -RootPath $syntheticRoot -CurrentPath $syntheticCurrent
    Write-ObjectDiagnostic -InputObject $state -Title '安装状态识别自检（合成路径）'

    $rootExists = Get-SafePropertyValue -InputObject $state -Name 'InstallRootExists' -DefaultValue $true
    if ($rootExists -ne $false) {
        throw '安装状态识别自检失败。'
    }

    Write-ReportLine 'OK: ValidateOnly 自检仅访问合成路径，不读取当前用户真实 Orderly 安装或数据目录。'
}

function Invoke-UpdateSourceSelfTest {
    $absolute = Resolve-UpdateSourceInput -Value $ExpectedUpdateSource
    if ($absolute.Kind -ne 'Local') {
        throw '本地绝对路径更新源解析失败。'
    }

    $fileUri = New-Object System.Uri($ExpectedUpdateSource)
    $fileResolved = Resolve-UpdateSourceInput -Value $fileUri.AbsoluteUri
    if ($fileResolved.Kind -ne 'Local') {
        throw 'file URI 更新源解析失败。'
    }

    $webResolved = Resolve-UpdateSourceInput -Value 'https://example.com/releases'
    if ($webResolved.Kind -ne 'Web') {
        throw 'https 更新源解析失败。'
    }

    Assert-LocalUpdateSource -SourcePath $ExpectedUpdateSource
    Write-ReportLine 'OK: 更新源自检覆盖本地绝对路径、file URI、https。'
}

function Invoke-ReturnContractSelfTest {
    $logOutput = @(& { Write-ReportLine 'OK: success pipeline 日志隔离探针' })
    if ($logOutput.Count -ne 0) {
        throw 'Write-ReportLine 污染 success pipeline。'
    }

    $versionOutput = @(& { Get-InstalledProductVersion -ExePath (Join-Path $Release011Root 'Setup.exe') })
    if ($versionOutput.Count -ne 1) {
        throw "Get-InstalledProductVersion 返回数量不正确：$($versionOutput.Count)"
    }

    if (-not ($versionOutput[0] -is [string])) {
        throw 'Get-InstalledProductVersion 未返回纯字符串。'
    }

    $versionText = [string]$versionOutput[0]
    if ($versionText -ne $SourceVersion) {
        throw "Get-InstalledProductVersion 返回值不等于 $SourceVersion：$versionText"
    }

    if ($versionText.Contains('OK:') -or $versionText.Contains('\') -or $versionText.Contains("`r") -or $versionText.Contains("`n")) {
        throw "Get-InstalledProductVersion 返回值包含日志、路径或换行：$versionText"
    }

    $names = Get-ObjectPropertyNames -InputObject ([pscustomobject]@{ A = 1; B = 2 })
    if (-not ($names -is [System.Array]) -or $names.Count -ne 2) {
        throw '集合函数未返回数组契约。'
    }

    $resolved = Resolve-UpdateSourceInput -Value $ExpectedUpdateSource
    if (-not [string]::Equals([string]$resolved.ResolvedPath, [System.IO.Path]::GetFullPath($ExpectedUpdateSource), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '路径比较自检失败。'
    }

    $state = Get-InstallState -RootPath (Join-Path $env:TEMP 'OrderlyE2E-NoSuchRoot') -CurrentPath (Join-Path $env:TEMP 'OrderlyE2E-NoSuchRoot\current')
    if ([bool](Get-SafePropertyValue -InputObject $state -Name 'InstallRootExists' -DefaultValue $true)) {
        throw '布尔判断自检失败。'
    }

    Write-ReportLine 'OK: 返回契约自检覆盖单值字符串、数组、日志隔离、版本/路径/布尔比较。'
}

function Invoke-ValidateOnly {
    Write-ReportLine 'ValidateOnly: 不安装、不启动、不卸载、不删除 Orderly。'
    if (Test-StrictModeEnabled) {
        Write-ReportLine 'OK: StrictMode 已开启。'
    }
    else {
        Fail-Step 'StrictMode 未开启。'
    }

    Assert-ReleaseArtifacts -Version $SourceVersion -ReleaseRoot $Release011Root -PackageRoot $Release011Packages
    Assert-ReleaseArtifacts -Version $TargetVersion -ReleaseRoot $Release012Root -PackageRoot $Release012Packages
    Invoke-ObjectShapeSelfTest
    Invoke-InstallStateSelfTest
    Invoke-UpdateSourceSelfTest
    Invoke-ReturnContractSelfTest
    Write-ReportLine "OK: 报告写入成功 -> $ReportPath"
}

Initialize-Report

try {
    Write-BasicDiagnostics
    Invoke-Step -Name '运行模式保护' -Action { Assert-ExecutionModeAllowed }

    if ($ValidateOnly) {
        Invoke-Step -Name '无副作用自检' -Action { Invoke-ValidateOnly }
    }
    elseif ($UninstallVerify) {
        Invoke-Step -Name '卸载验证' -Action { Invoke-UninstallVerification }
    }
    else {
        Invoke-Step -Name '发布资产检查' -Action {
            Assert-ReleaseArtifacts -Version $SourceVersion -ReleaseRoot $Release011Root -PackageRoot $Release011Packages
            Assert-ReleaseArtifacts -Version $TargetVersion -ReleaseRoot $Release012Root -PackageRoot $Release012Packages
            Assert-LocalUpdateSource -SourcePath $ExpectedUpdateSource
        }

        if (Test-InstalledVersionEquals -ExpectedVersion $SourceVersion) {
            Write-ReportLine "OK: 已检测到安装版本 $SourceVersion，跳过清理和重新安装。"
        }
        else {
            Invoke-Step -Name '重跑前清理安装目录' -Action { Remove-InstallRootOnly }
            Invoke-Step -Name '安装 0.1.1' -Action { Invoke-SetupInstall -SetupPath (Join-Path $Release011Root 'Setup.exe') }
        }

        Invoke-Step -Name '验证 0.1.1 安装状态' -Action { Assert-InstalledLayout -ExpectedVersion $SourceVersion }
        Invoke-Step -Name '启动应用并注入本地更新源' -Action { Start-InstalledAppWithLocalUpdateSource }

        Write-ReportLine ''
        Write-ReportLine '人工操作提示：'
        Write-ReportLine '1. 在程序内创建最小测试账号。'
        Write-ReportLine '2. 创建一条测试数据。'
        Write-ReportLine '3. 设置头像。'
        Write-ReportLine '4. 修改两项设置。'
        Write-ReportLine '5. 在程序内点击检查更新并确认更新。'
        Write-ReportLine ''
        [void](Read-Host '完成上述操作后按 Enter 继续验收')

        Invoke-Step -Name '验证升级到 0.1.2' -Action { Assert-InstalledLayout -ExpectedVersion $TargetVersion }
        Invoke-Step -Name '验证升级后可启动' -Action { Start-And-VerifyRelaunch }
        Invoke-Step -Name '验证数据保留和安装目录隔离' -Action {
            if (-not (Test-Path -LiteralPath $DataRoot)) {
                Fail-Step "升级后业务数据目录不存在：$DataRoot"
            }

            Write-ReportLine "OK: 升级后业务数据目录仍存在 -> $DataRoot"
            Assert-NoBusinessDataInsideInstallRoot
        }
    }

    $script:FinalResult = 'PASS'
    Write-ReportLine ''
    Write-ReportLine 'RESULT: PASS'
}
catch {
    Write-ReportLine ''
    Write-ReportLine 'RESULT: FAIL'
    Write-ReportLine "FailedStep=$script:CurrentStep"
    Write-ReportLine "Exception=$($_.Exception.Message)"
    Write-ReportLine 'RawException:'
    Write-ReportLine ($_ | Out-String)
    Write-ReportLine "Suggestion=$script:FailureSuggestion"
    throw
}
finally {
    Write-ReportLine "FinalResult=$script:FinalResult"
    Write-ReportLine "Finished: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ReportLine "ReportPath=$ReportPath"
}
