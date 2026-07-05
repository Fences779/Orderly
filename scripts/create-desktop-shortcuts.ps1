$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopPath = [Environment]::GetFolderPath('Desktop')
$wshShell = New-Object -ComObject WScript.Shell
$devShortcutName = 'Orderly' + [string][char]0x5F00 + [string][char]0x53D1 + [string][char]0x6A21 + [string][char]0x5F0F + '.lnk'
$startShortcutName = 'Orderly' + [string][char]0x542F + [string][char]0x52A8 + '.lnk'
$devDescription = 'Orderly Dev Mode'
$startDescription = 'Orderly Start'

$shortcuts = @(
  @{
    Name = $devShortcutName
    TargetPath = Join-Path $repoRoot 'dev-watch-qa.bat'
    Description = $devDescription
  },
  @{
    Name = $startShortcutName
    TargetPath = Join-Path $repoRoot 'start-orderly.bat'
    Description = $startDescription
  }
)

foreach ($shortcutConfig in $shortcuts) {
  if (-not (Test-Path -LiteralPath $shortcutConfig.TargetPath)) {
    throw "Target file not found: $($shortcutConfig.TargetPath)"
  }

  $shortcutPath = Join-Path $desktopPath $shortcutConfig.Name
  $shortcut = $wshShell.CreateShortcut($shortcutPath)
  $shortcut.TargetPath = $shortcutConfig.TargetPath
  $shortcut.WorkingDirectory = $repoRoot
  $shortcut.Description = $shortcutConfig.Description
  $shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,220"
  $shortcut.Save()
}

Write-Host "Desktop shortcuts created:"
foreach ($shortcutConfig in $shortcuts) {
  Write-Host ("- " + (Join-Path $desktopPath $shortcutConfig.Name))
}
