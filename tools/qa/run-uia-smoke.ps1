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
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

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

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
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

function Get-AutomationElementDescription {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    try {
        $id = [string]$Element.Current.AutomationId
        $name = [string]$Element.Current.Name
        $type = if ($null -eq $Element.Current.ControlType) { '<unknown>' } else { $Element.Current.ControlType.ProgrammaticName }
        return "AutomationId='$id', Name='$name', ControlType='$type'"
    }
    catch {
        return '<stale automation element>'
    }
}

function Wait-AutomationElementReady {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,
        [int]$TimeoutSeconds = 5,
        [switch]$RequireEnabled,
        [switch]$RequireVisible,
        [switch]$RequireKeyboardFocusable
    )

    $description = Get-AutomationElementDescription -Element $Element
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastReason = 'unknown'
    while ((Get-Date) -lt $deadline) {
        try {
            $current = $Element.Current
            $enabled = (-not $RequireEnabled) -or $current.IsEnabled
            $visible = (-not $RequireVisible) -or (-not $current.IsOffscreen)
            $focusable = (-not $RequireKeyboardFocusable) -or $current.IsKeyboardFocusable
            $rect = $current.BoundingRectangle
            $hasBounds = $rect.Width -gt 0 -and $rect.Height -gt 0

            if ($enabled -and $visible -and $focusable -and ((-not $RequireVisible) -or $hasBounds)) {
                return
            }

            $lastReason = "Enabled=$($current.IsEnabled), Offscreen=$($current.IsOffscreen), Focusable=$($current.IsKeyboardFocusable), Bounds=$($rect.Width)x$($rect.Height)"
        }
        catch {
            $lastReason = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 200
    }

    throw "等待元素就绪超时：$description；最后状态：$lastReason"
}

function Test-AutomationElementMatch {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Left,
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Right
    )

    try {
        $leftId = @($Left.GetRuntimeId())
        $rightId = @($Right.GetRuntimeId())
        if ($leftId.Count -ne $rightId.Count) {
            return $false
        }

        for ($index = 0; $index -lt $leftId.Count; $index++) {
            if ($leftId[$index] -ne $rightId[$index]) {
                return $false
            }
        }

        return $true
    }
    catch {
        return $false
    }
}

function Wait-AutomationFocus {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,
        [int]$TimeoutSeconds = 3
    )

    $description = Get-AutomationElementDescription -Element $Element
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastFocused = '<none>'
    while ((Get-Date) -lt $deadline) {
        try {
            $Element.SetFocus()
        }
        catch {
        }

        Start-Sleep -Milliseconds 120

        try {
            $focusedElement = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($null -ne $focusedElement) {
                if (Test-AutomationElementMatch -Left $Element -Right $focusedElement) {
                    return
                }

                $targetId = [string]$Element.Current.AutomationId
                $focusedId = [string]$focusedElement.Current.AutomationId
                if (-not [string]::IsNullOrWhiteSpace($targetId) -and $targetId -eq $focusedId) {
                    return
                }
            }

            if ($null -ne $focusedElement) {
                $lastFocused = Get-AutomationElementDescription -Element $focusedElement
            }
        }
        catch {
            $lastFocused = $_.Exception.Message
        }
    }

    throw "等待焦点稳定超时：$description；最后聚焦元素：$lastFocused"
}

function Get-AutomationAncestorWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $current = $Element
    while ($null -ne $current) {
        try {
            if ($current.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                return $current
            }
        }
        catch {
        }

        $current = $walker.GetParent($current)
    }

    return $null
}

function Invoke-NativeLeftClick {
    param(
        [Parameter(Mandatory = $true)]
        [double]$X,
        [Parameter(Mandatory = $true)]
        [double]$Y
    )

    $targetX = [int][Math]::Round($X)
    $targetY = [int][Math]::Round($Y)
    if (-not [QaNativeMethods]::SetCursorPos($targetX, $targetY)) {
        throw "移动鼠标失败：($targetX, $targetY)"
    }

    Start-Sleep -Milliseconds 60
    [QaNativeMethods]::mouse_event([QaNativeMethods]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [QaNativeMethods]::mouse_event([QaNativeMethods]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-SendKeysSafely {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Keys,
        [string]$Context = 'UIA input',
        [int]$RetryCount = 2
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            [System.Windows.Forms.SendKeys]::SendWait($Keys)
            return $true
        }
        catch {
            $lastError = $_.Exception.Message
            if (Get-Command -Name Add-LogLine -ErrorAction SilentlyContinue) {
                Add-LogLine "SendWait 第 $attempt 次失败（$Context）：$lastError"
            }

            Start-Sleep -Milliseconds (150 * $attempt)
        }
    }

    if (Get-Command -Name Add-LogLine -ErrorAction SilentlyContinue) {
        Add-LogLine "SendWait 已放弃（$Context）：$lastError"
    }

    return $false
}

function Invoke-AutomationClick {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    Wait-AutomationElementReady -Element $Element -TimeoutSeconds 5 -RequireEnabled -RequireVisible

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

    try {
        $legacy = $Element.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
        if ($null -ne $legacy) {
            $legacy.DoDefaultAction()
            return
        }
    }
    catch {
    }

    $rect = $Element.Current.BoundingRectangle
    if ($rect.Width -le 0 -or $rect.Height -le 0) {
        throw "元素无法点击：AutomationId=$($Element.Current.AutomationId)，BoundingRectangle 无效。"
    }

    Invoke-NativeLeftClick -X ($rect.X + ($rect.Width / 2)) -Y ($rect.Y + ($rect.Height / 2))
}

function Set-AutomationText {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element,
        [Parameter(Mandatory = $true)]
        [string]$Text,
        [int]$RetryCount = 3
    )

    Wait-AutomationElementReady -Element $Element -TimeoutSeconds 5 -RequireEnabled -RequireVisible

    $description = Get-AutomationElementDescription -Element $Element
    $lastError = $null
    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            $valuePattern = $null
            try {
                $valuePattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            }
            catch {
            }

            if ($null -ne $valuePattern) {
                $valuePattern.SetValue($Text)
            }
            else {
                $legacy = $null
                try {
                    $legacy = $Element.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
                }
                catch {
                }

                if ($null -ne $legacy) {
                    $legacy.SetValue($Text)
                }
                else {
                    Wait-AutomationFocus -Element $Element -TimeoutSeconds 3
                    $selectAllSent = Invoke-SendKeysSafely -Keys '^a' -Context "$description select-all" -RetryCount 2
                    if (-not $selectAllSent) {
                        throw "无法发送 Ctrl+A。"
                    }

                    Start-Sleep -Milliseconds 120
                    $textSent = Invoke-SendKeysSafely -Keys $Text -Context "$description text-input" -RetryCount 2
                    if (-not $textSent) {
                        throw "无法发送文本。"
                    }
                }
            }

            Start-Sleep -Milliseconds (100 * $attempt)
            $actualText = Get-AutomationTextValue -Element $Element
            if ($actualText -eq $Text) {
                return
            }

            $lastError = "写入后回读不一致。expected='$Text', actual='$actualText'"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        if (Get-Command -Name Add-LogLine -ErrorAction SilentlyContinue) {
            Add-LogLine "文本写入重试 $attempt/$RetryCount：$description；原因：$lastError"
        }
        Start-Sleep -Milliseconds (180 * $attempt)
    }

    throw "设置文本失败：$description；$lastError"
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

    Wait-AutomationElementReady -Element $ComboBox -TimeoutSeconds 5 -RequireEnabled -RequireVisible
    $beforeText = Get-AutomationTextValue -Element $ComboBox

    $expanded = $false
    try {
        $expandCollapse = $ComboBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($null -ne $expandCollapse) {
            if ($expandCollapse.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                $expandCollapse.Expand()
            }
            $expanded = $true
        }
    }
    catch {
    }

    if ($expanded) {
        $comboRect = $ComboBox.Current.BoundingRectangle
        $processId = $ComboBox.Current.ProcessId
        $condition = New-Object System.Windows.Automation.AndCondition(
            (Get-AutomationPropertyCondition -Property ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) -Value $processId),
            (Get-AutomationPropertyCondition -Property ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) -Value ([System.Windows.Automation.ControlType]::ListItem)))

        $deadline = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $deadline) {
            $candidateCollection = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
            $candidates = for ($index = 0; $index -lt $candidateCollection.Count; $index++) { $candidateCollection.Item($index) }
            $match = $candidates |
                Where-Object {
                    try {
                        $current = $_.Current
                        $rect = $current.BoundingRectangle
                        -not $current.IsOffscreen -and
                        $current.IsEnabled -and
                        $rect.Width -gt 0 -and
                        $rect.Height -gt 0 -and
                        $rect.Top -ge ($comboRect.Top - 20) -and
                        $rect.Left -le ($comboRect.Right + 220) -and
                        $rect.Right -ge ($comboRect.Left - 120)
                    }
                    catch {
                        $false
                    }
                } |
                Sort-Object {
                    try {
                        [Math]::Abs($_.Current.BoundingRectangle.Top - $comboRect.Bottom)
                    }
                    catch {
                        [double]::MaxValue
                    }
                } |
                Select-Object -First 1

            if ($null -ne $match) {
                Invoke-AutomationClick -Element $match
                Start-Sleep -Milliseconds 250

                $selectedText = Get-AutomationTextValue -Element $ComboBox
                if (-not [string]::IsNullOrWhiteSpace($selectedText)) {
                    return $selectedText
                }
            }

            Start-Sleep -Milliseconds 150
        }
    }

    Wait-AutomationFocus -Element $ComboBox -TimeoutSeconds 3
    $downSent = Invoke-SendKeysSafely -Keys '{DOWN}' -Context 'combo-box down' -RetryCount 3
    Start-Sleep -Milliseconds 150
    $enterSent = Invoke-SendKeysSafely -Keys '{ENTER}' -Context 'combo-box enter' -RetryCount 3
    Start-Sleep -Milliseconds 250

    if (-not ($downSent -and $enterSent)) {
        throw "ComboBox 选择失败：无法完成键盘兜底输入。"
    }

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
    Add-LogLine "启动 Orderly.App：$exePath --qa-mode"
    $process = Start-Process -FilePath $exePath -ArgumentList @('--qa-mode') -PassThru
    Add-LogLine "等待主窗口就绪（超时 ${WindowTimeoutSeconds}s）"
    $window = Wait-MainWindow -Process $process -TimeoutSeconds $WindowTimeoutSeconds
    $windowHandle = [IntPtr]$process.MainWindowHandle
    Wait-AutomationElementReady -Element $window -TimeoutSeconds 10 -RequireEnabled -RequireVisible
    [QaNativeMethods]::ShowWindow($windowHandle, 9) | Out-Null
    [QaNativeMethods]::SetForegroundWindow($windowHandle) | Out-Null
    Start-Sleep -Milliseconds 350

    $mainWindowPath = Join-Path $run.Path '00-main-window.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $mainWindowPath
    $report.screenshots.mainWindow = $mainWindowPath
    $report.mainWindowTitle = [string]$window.Current.Name

    Add-LogLine '步骤：切换到客户/订单 Tab'
    $customerOrderTab = Find-AutomationElement -Root $window -AutomationId 'Tab_CustomerOrder' -TimeoutSeconds 5
    if ($null -eq $customerOrderTab) {
        throw "未找到客户/订单 Tab。"
    }

    Invoke-AutomationClick -Element $customerOrderTab
    Start-Sleep -Milliseconds 700

    Add-LogLine '步骤：选择 QA 客户 p13qa-customer-a'
    $qaCustomer = Find-AutomationElement -Root $window -AutomationId 'p13qa-customer-a' -TimeoutSeconds 5
    if ($null -eq $qaCustomer) {
        throw "未找到 QA 客户：p13qa-customer-a"
    }

    Invoke-AutomationClick -Element $qaCustomer
    Start-Sleep -Milliseconds 500
    $report.checks.selectedCustomer = 'p13qa-customer-a'

    Add-LogLine '步骤：打开新增订单对话框'
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

    Add-LogLine '步骤：填写新增订单表单'
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Title' -TimeoutSeconds 2) -Text $orderTitle
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Amount' -TimeoutSeconds 2) -Text '199'
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Requirement' -TimeoutSeconds 2) -Text $orderRequirement
    Set-AutomationText -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Remark' -TimeoutSeconds 2) -Text $orderRemark

    $orderDialogPath = Join-Path $run.Path '01-add-order-dialog.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $orderDialogPath
    $report.screenshots.addOrderDialog = $orderDialogPath

    Add-LogLine '步骤：提交新增订单'
    Invoke-AutomationClick -Element (Find-AutomationElement -Root $orderDialog -AutomationId 'AddOrderDialog_Confirm' -TimeoutSeconds 2)
    Wait-AutomationElementMissing -Root $window -AutomationId 'AddOrderDialog' -TimeoutSeconds 8
    Start-Sleep -Milliseconds 800
    $report.checks.addOrder = $orderTitle

    $afterOrderPath = Join-Path $run.Path '02-after-add-order.png'
    Save-WindowScreenshot -WindowHandle $windowHandle -Path $afterOrderPath
    $report.screenshots.afterAddOrder = $afterOrderPath

    Add-LogLine '步骤：切换到工作台 Tab'
    $dashboardTab = Find-AutomationElement -Root $window -AutomationId 'Tab_Dashboard' -TimeoutSeconds 5
    if ($null -eq $dashboardTab) {
        throw "未找到工作台 Tab。"
    }

    Invoke-AutomationClick -Element $dashboardTab
    Start-Sleep -Milliseconds 700

    Add-LogLine '步骤：打开新增备注对话框'
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

    Add-LogLine '步骤：选择备注模板并填写内容'
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

    Add-LogLine '步骤：提交新增备注'
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
