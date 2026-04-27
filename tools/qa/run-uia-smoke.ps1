param(
    [switch]$SkipReset,
    [switch]$SkipFinalReset,
    [int]$WindowTimeoutSeconds = 30
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class QaNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);
}
"@

function Get-AutomationPropertyCondition {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationProperty]$Property,
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    return New-Object System.Windows.Automation.PropertyCondition($Property, $Value)
}

function Find-AutomationElement {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId,
        [int]$TimeoutSeconds = 5
    )

    $condition = Get-AutomationPropertyCondition `
        -Property ([System.Windows.Automation.AutomationElement]::AutomationIdProperty) `
        -Value $AutomationId

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $element = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 250
    }

    return $null
}

function Wait-AutomationElementMissing {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory = $true)]
        [string]$AutomationId,
        [int]$TimeoutSeconds = 8
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($null -eq (Find-AutomationElement -Root $Root -AutomationId $AutomationId -TimeoutSeconds 1)) {
            return
        }

        Start-Sleep -Milliseconds 200
    }

    throw "等待元素消失超时：$AutomationId"
}

function Wait-MainWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Orderly.App 在主窗口出现前已退出，退出码：$($Process.ExitCode)"
        }

        if ($Process.MainWindowHandle -ne 0) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
            if ($null -ne $window) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 300
    }

    throw "等待 WPF 主窗口超时：$TimeoutSeconds 秒内未拿到 MainWindowHandle。"
}

function Invoke-AutomationClick {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    try {
        $invoke = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($null -ne $invoke) {
            $invoke.Invoke()
            return
        }
    }
    catch {
    }

    try {
        $selection = $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($null -ne $selection) {
            $selection.Select()
            return
        }
    }
    catch {
    }

    $rect = $Element.Current.BoundingRectangle
    if ($rect.Width -le 0 -or $rect.Height -le 0) {
        throw "元素无法点击：AutomationId=$($Element.Current.AutomationId)，BoundingRectangle 无效。"
    }

    $point = New-Object System.Drawing.Point([int]($rect.X + ($rect.Width / 2)), [int]($rect.Y + ($rect.Height / 2)))
    [System.Windows.Forms.Cursor]::Position = $point
    [System.Windows.Forms.SendKeys]::SendWait(' ')
}

function Set-AutomationText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    try {
        $valuePattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($null -ne $valuePattern) {
            $valuePattern.SetValue($Text)
            return
        }
    }
    catch {
    }

    $Element.SetFocus()
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.SendKeys]::SendWait($Text)
}

function Get-AutomationTextValue {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    try {
        $valuePattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($null -ne $valuePattern) {
            return [string]$valuePattern.Current.Value
        }
    }
    catch {
    }

    return [string]$Element.Current.Name
}

function Select-FirstComboBoxItem {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$ComboBox
    )

    Invoke-AutomationClick -Element $ComboBox
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('{DOWN}')
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 250

    $selectedText = Get-AutomationTextValue -Element $ComboBox
    if ([string]::IsNullOrWhiteSpace($selectedText)) {
        return '<selected-by-keyboard>'
    }

    return $selectedText
}

function Save-WindowScreenshot {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$WindowHandle,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    [QaNativeMethods]::ShowWindow($WindowHandle, 9) | Out-Null
    [QaNativeMethods]::SetForegroundWindow($WindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300

    $rect = New-Object QaNativeMethods+RECT
    if (-not [QaNativeMethods]::GetWindowRect($WindowHandle, [ref]$rect)) {
        throw "截图失败：GetWindowRect 返回 false。"
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "截图失败：窗口尺寸无效（${width}x${height}）。"
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            (New-Object System.Drawing.Point($rect.Left, $rect.Top)),
            [System.Drawing.Point]::Empty,
            (New-Object System.Drawing.Size($width, $height)))
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-PythonCommand {
    $python = Get-Command -Name 'python' -ErrorAction SilentlyContinue
    if ($null -ne $python) {
        return [pscustomobject]@{
            FilePath = $python.Source
            Arguments = @('-')
        }
    }

    $py = Get-Command -Name 'py' -ErrorAction SilentlyContinue
    if ($null -ne $py) {
        return [pscustomobject]@{
            FilePath = $py.Source
            Arguments = @('-3', '-')
        }
    }

    throw 'SQLite verification requires Python (python or py launcher) on PATH.'
}

function Invoke-DatabaseVerification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OrderTitle,
        [Parameter(Mandatory = $true)]
        [string]$OrderRequirement,
        [Parameter(Mandatory = $true)]
        [string]$NoteContent
    )

    $python = Get-PythonCommand
    $previousDbPath = $env:ORDERLY_QA_DB_PATH
    $previousOrderTitle = $env:ORDERLY_QA_ORDER_TITLE
    $previousOrderRequirement = $env:ORDERLY_QA_ORDER_REQUIREMENT
    $previousNoteContent = $env:ORDERLY_QA_NOTE_CONTENT
    try {
        $env:ORDERLY_QA_DB_PATH = Get-DefaultDatabasePath
        $env:ORDERLY_QA_ORDER_TITLE = $OrderTitle
        $env:ORDERLY_QA_ORDER_REQUIREMENT = $OrderRequirement
        $env:ORDERLY_QA_NOTE_CONTENT = $NoteContent

        $verificationJson = @'
import json
import os
import sqlite3

db_path = os.environ["ORDERLY_QA_DB_PATH"]
order_title = os.environ["ORDERLY_QA_ORDER_TITLE"]
order_requirement = os.environ["ORDERLY_QA_ORDER_REQUIREMENT"]
note_content = os.environ["ORDERLY_QA_NOTE_CONTENT"]

conn = sqlite3.connect(db_path)
cur = conn.cursor()

result = {
    "orderCount": cur.execute(
        "SELECT COUNT(1) FROM Orders WHERE Title = ? AND Requirement = ?",
        (order_title, order_requirement),
    ).fetchone()[0],
    "noteCount": cur.execute(
        "SELECT COUNT(1) FROM CustomerNotes WHERE Content = ?",
        (note_content,),
    ).fetchone()[0],
    "orderActivityCount": cur.execute(
        "SELECT COUNT(1) FROM ActivityLogs WHERE Type = 6 AND (Description = ? OR MetadataJson LIKE '%\"source\":\"runtime\"%')",
        (order_title,),
    ).fetchone()[0],
    "noteActivityCount": cur.execute(
        "SELECT COUNT(1) FROM ActivityLogs WHERE Type = 8 AND (Description = ? OR MetadataJson LIKE '%\"source\":\"runtime\"%')",
        (note_content,),
    ).fetchone()[0],
    "runtimeTaggedActivityCount": cur.execute(
        "SELECT COUNT(1) FROM ActivityLogs WHERE MetadataJson LIKE '%\"qa\":{\"tag\":\"p13qa\"%' AND MetadataJson LIKE '%\"source\":\"runtime\"%'"
    ).fetchone()[0],
}

print(json.dumps(result))
'@ | & $python.FilePath @($python.Arguments)

        $verification = $verificationJson | ConvertFrom-Json -AsHashtable

        if ($verification.orderCount -lt 1) {
            throw "SQLite 未找到新建订单：$OrderTitle"
        }

        if ($verification.noteCount -lt 1) {
            throw "SQLite 未找到新建备注：$NoteContent"
        }

        if ($verification.orderActivityCount -lt 1) {
            throw "SQLite 未找到运行态订单 ActivityLog。"
        }

        if ($verification.noteActivityCount -lt 1) {
            throw "SQLite 未找到运行态备注 ActivityLog。"
        }

        if ($verification.runtimeTaggedActivityCount -lt 1) {
            throw "SQLite 未找到带 QA runtime 元数据的 ActivityLog。"
        }

        return $verification
    }
    finally {
        $env:ORDERLY_QA_DB_PATH = $previousDbPath
        $env:ORDERLY_QA_ORDER_TITLE = $previousOrderTitle
        $env:ORDERLY_QA_ORDER_REQUIREMENT = $previousOrderRequirement
        $env:ORDERLY_QA_NOTE_CONTENT = $previousNoteContent
    }
}

$run = New-QaSmokeRunDirectory
$logPath = Join-Path $run.Path 'smoke-log.txt'
$reportPath = Join-Path $run.Path 'smoke-report.json'
$process = $null
$failureMessage = $null
$report = [ordered]@{
    timestamp       = $run.Timestamp
    repoRoot        = Get-RepoRoot
    outputDirectory = $run.Path
    databasePath    = Get-DefaultDatabasePath
    result          = 'FAIL'
    launchArguments = @('--qa-mode')
    checks          = [ordered]@{}
    screenshots     = [ordered]@{}
    notCovered      = @(
        '未覆盖登录流程、浏览器/localhost/DOM。',
        '未覆盖搜索/筛选、FollowUp 完成/延期/取消、Deal 推进与状态切换的完整端到端动作。',
        '未覆盖视觉比对、125% 缩放、托盘、悬浮窗、全局快捷键。'
    )
}
$logLines = New-Object System.Collections.Generic.List[string]

function Add-LogLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Message"
    $logLines.Add($line) | Out-Null
    Write-Host $line
}

try {
    Assert-NoRunningOrderlyProcess
    Add-LogLine "开始执行 UIA smoke"
    Add-LogLine "输出目录：$($run.Path)"

    if (-not $SkipReset) {
        Add-LogLine "执行 smoke 前 reset-qa-data"
        $resetResult = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--reset-qa-data')
        $report.checks.preReset = $resetResult.StdOut
    }

    $runtimeMarker = '[P1_QA_RUNTIME]'
    $runToken = "uia-$($run.Timestamp)"
    $orderTitle = "$runtimeMarker Order $runToken"
    $orderRequirement = "$runtimeMarker Requirement $runToken"
    $orderRemark = "$runtimeMarker Remark $runToken"
    $noteContent = "$runtimeMarker Note $runToken"

    $exePath = Get-OrderlyAppExePath
    $process = Start-Process -FilePath $exePath -ArgumentList @('--qa-mode') -PassThru
    $window = Wait-MainWindow -Process $process -TimeoutSeconds $WindowTimeoutSeconds
    $windowHandle = [IntPtr]$process.MainWindowHandle

    $mainWindowPath = Join-Path $run.Path '00-main-window.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $mainWindowPath
    $report.screenshots.mainWindow = $mainWindowPath
    $report.mainWindowTitle = [string]$window.Current.Name

    $customerOrderTab = Find-AutomationElement -Root $window -AutomationId 'Tab_CustomerOrder' -TimeoutSeconds 5
    if ($null -eq $customerOrderTab) {
        throw "未找到客户/订单 Tab。"
    }

    Invoke-AutomationClick -Element $customerOrderTab
    Start-Sleep -Milliseconds 700

    $qaCustomer = Find-AutomationElement -Root $window -AutomationId 'p13qa-customer-a' -TimeoutSeconds 5
    if ($null -eq $qaCustomer) {
        throw "未找到 QA 客户：p13qa-customer-a"
    }

    Invoke-AutomationClick -Element $qaCustomer
    Start-Sleep -Milliseconds 500
    $report.checks.selectedCustomer = 'p13qa-customer-a'

    $addOrderButton = Find-AutomationElement -Root $window -AutomationId 'Btn_AddOrder' -TimeoutSeconds 5
    if ($null -eq $addOrderButton) {
        throw "未找到新增订单按钮。"
    }

    Invoke-AutomationClick -Element $addOrderButton
    Start-Sleep -Milliseconds 700

    $orderDialog = Find-AutomationElement -Root $window -AutomationId 'AddOrderDialog' -TimeoutSeconds 5
    if ($null -eq $orderDialog) {
        throw "未找到 AddOrderDialog。"
    }

    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Title' -TimeoutSeconds 2) -Text $orderTitle
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Amount' -TimeoutSeconds 2) -Text '199'
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Requirement' -TimeoutSeconds 2) -Text $orderRequirement
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Remark' -TimeoutSeconds 2) -Text $orderRemark

    $orderDialogPath = Join-Path $run.Path '01-add-order-dialog.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $orderDialogPath
    $report.screenshots.addOrderDialog = $orderDialogPath

    Invoke-AutomationClick -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Confirm' -TimeoutSeconds 2)
    Wait-AutomationElementMissing -Root $window -AutomationId 'AddOrderDialog' -TimeoutSeconds 8
    Start-Sleep -Milliseconds 800
    $report.checks.addOrder = $orderTitle

    $afterOrderPath = Join-Path $run.Path '02-after-add-order.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $afterOrderPath
    $report.screenshots.afterAddOrder = $afterOrderPath

    $dashboardTab = Find-AutomationElement -Root $window -AutomationId 'Tab_Dashboard' -TimeoutSeconds 5
    if ($null -eq $dashboardTab) {
        throw "未找到工作台 Tab。"
    }

    Invoke-AutomationClick -Element $dashboardTab
    Start-Sleep -Milliseconds 700

    $addNoteButton = Find-AutomationElement -Root $window -AutomationId 'Btn_AddNote' -TimeoutSeconds 5
    if ($null -eq $addNoteButton) {
        throw "未找到新增备注按钮。"
    }

    Invoke-AutomationClick -Element $addNoteButton
    Start-Sleep -Milliseconds 700

    $noteDialog = Find-AutomationElement -Root $window -AutomationId 'AddNoteDialog' -TimeoutSeconds 5
    if ($null -eq $noteDialog) {
        throw "未找到 AddNoteDialog。"
    }

    $templateCombo = Find-AutomationElement -Root $noteDialog -AutomationId 'AddNoteDialog_Template' -TimeoutSeconds 2
    $templateName = Select-FirstComboBoxItem -ComboBox $templateCombo
    $insertButton = Find-AutomationElement -Root $noteDialog -AutomationId 'AddNoteDialog_InsertTemplate' -TimeoutSeconds 2
    $contentBox = Find-AutomationElement -Root $noteDialog -AutomationId 'AddNoteDialog_Content' -TimeoutSeconds 2
    $contentBefore = Get-AutomationTextValue -Element $contentBox

    Invoke-AutomationClick -Element $insertButton
    Start-Sleep -Milliseconds 400

    $contentAfterInsert = Get-AutomationTextValue -Element $contentBox
    $templateInserted = -not [string]::IsNullOrWhiteSpace($contentAfterInsert) -and $contentAfterInsert -ne $contentBefore
    $finalNoteContent = if ($templateInserted) {
        $contentAfterInsert.TrimEnd() + [Environment]::NewLine + $noteContent
    }
    else {
        $noteContent
    }

    Set-AutomationText -Element $contentBox -Text $finalNoteContent

    $noteDialogPath = Join-Path $run.Path '03-add-note-dialog.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $noteDialogPath
    $report.screenshots.addNoteDialog = $noteDialogPath

    Invoke-AutomationClick -Element (Find-AutomationElement -Root $noteDialog -AutomationId 'AddNoteDialog_Confirm' -TimeoutSeconds 2)
    Wait-AutomationElementMissing -Root $window -AutomationId 'AddNoteDialog' -TimeoutSeconds 8
    Start-Sleep -Milliseconds 800
    $report.checks.addNote = [ordered]@{
        template                 = $templateName
        templateInsertVerified   = $templateInserted
        content                  = $finalNoteContent
    }

    if (-not $templateInserted) {
        $report.notCovered += 'AddNote 模板插入在当前 UIA 下未稳定命中；本次仅覆盖 AddNote 保存与 SQLite 回读。'
    }

    $afterNotePath = Join-Path $run.Path '04-after-add-note.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $afterNotePath
    $report.screenshots.afterAddNote = $afterNotePath

    Add-LogLine "执行 SQLite 回读验证"
    $report.checks.sqlite = Invoke-DatabaseVerification -OrderTitle $orderTitle -OrderRequirement $orderRequirement -NoteContent $finalNoteContent
}
catch {
    $failureMessage = $_.Exception.ToString()
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not $SkipFinalReset) {
        try {
            Add-LogLine "执行 smoke 后 reset-qa-data，还原 QA 基线"
            $finalResetResult = Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--reset-qa-data')
            $report.checks.finalReset = $finalResetResult.StdOut
        }
        catch {
            if ([string]::IsNullOrWhiteSpace($failureMessage)) {
                $failureMessage = "smoke 后 reset 失败：$($_.Exception.Message)"
            }
            else {
                $report.finalResetError = $_.Exception.Message
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($failureMessage)) {
        $report.result = 'PASS'
        Add-LogLine 'UIA smoke PASS'
        Save-Utf8Text -Path $logPath -Content (($logLines -join [Environment]::NewLine) + [Environment]::NewLine)
        Save-Utf8Json -Path $reportPath -Value $report
        Write-Host "PASS"
        Write-Host "Report: $reportPath"
    }
    else {
        $report.result = 'FAIL'
        $report.error = $failureMessage
        Add-LogLine "UIA smoke FAIL: $failureMessage"
        Save-Utf8Text -Path $logPath -Content (($logLines -join [Environment]::NewLine) + [Environment]::NewLine)
        Save-Utf8Json -Path $reportPath -Value $report
        Write-Error $failureMessage
        exit 1
    }
}
