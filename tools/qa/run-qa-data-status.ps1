. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

Write-Step "查询 QA 数据状态"
Write-Step "仓库根目录：$(Get-RepoRoot)"
Write-Step "默认数据库路径：$(Get-DefaultDatabasePath)"
Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status') | Out-Null

