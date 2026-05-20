. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

Write-Step "重置 QA 数据"
Write-Step "仓库根目录：$(Get-RepoRoot)"
Write-Step "默认数据库路径：$(Get-DefaultDatabasePath)"
Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--reset-qa-data') | Out-Null
Import-OrderlyAssembliesForQa
Remove-LegacyInvalidEncryptedActivityLogs -DatabasePath (Get-DefaultDatabasePath)
Invoke-QaCiphertextBackfill -DatabasePath (Get-DefaultDatabasePath)
