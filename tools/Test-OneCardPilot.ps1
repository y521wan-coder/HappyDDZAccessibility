param([int]$MaxScans = 8, [ValidateRange(1, 20)][int]$RequiredCount = 17, [switch]$ViaKeys, [ValidateRange(1, 20)][int]$Position = 1)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$app = $assistant.Process
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
if ($null -eq $root) { throw 'Assistant window unavailable.' }

function AllControls {
    for ($retry = 0; $retry -lt 4; $retry++) {
        try {
            $app.Refresh()
            $currentRoot = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
            return $currentRoot.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            Start-Sleep -Milliseconds 150
        }
    }
    throw 'Assistant UIA tree changed during test.'
}
function FindControl([string]$name, [string]$type) {
    foreach ($control in (AllControls)) {
        if ($control.Current.Name -eq $name -and
            $control.Current.ControlType.ProgrammaticName -eq $type) { return $control }
    }
    throw "UIA control unavailable: $name"
}
function SelectTab([string]$name) {
    (FindControl $name 'ControlType.TabItem').GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Value([string]$name) {
    (FindControl $name 'ControlType.Edit').GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function SendCardAction([string]$key) {
    if (-not $ViaKeys) {
        (FindControl '在游戏拿起或放下当前一张（实验）' 'ControlType.Button').GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return
    }
    $mode = (FindControl '上下键操作游戏单张（实验）' 'ControlType.CheckBox').GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)
    if ($mode.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
        $mode.Toggle()
    }
    (FindControl '手牌' 'ControlType.List').SetFocus()
    $app.Refresh()
    if ([Pilot.Win]::GetForegroundWindow() -ne $assistant.Handle) {
        throw 'Assistant is not foreground; keyboard action cancelled.'
    }
    [System.Windows.Forms.SendKeys]::SendWait($key)
}

if ($ViaKeys) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();' -Name Win -Namespace Pilot
}

for ($attempt = 1; $attempt -le $MaxScans; $attempt++) {
    SelectTab '当前局面'
    $before = Value '识别说明'
    (FindControl '重新扫描' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $detail = Value '识别说明'
        if ($detail -ne $before -and $detail.Contains('截图时间')) { break }
    }
    $scene = Value '当前场景'
    SelectTab '手牌'
    $cards = @()
    foreach ($control in (AllControls)) {
        if ($control.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $control.Current.Name -match '^第 \d+ 张，点数候选：') { $cards += $control }
    }
    $status = Value '手牌识别与操作提示'
    Write-Output "scan $attempt scene=$scene cards=$($cards.Count) status=$status"
    if ($scene -in @('结算', '收费页面', '超时离房弹窗')) { break }
    if ($scene -notin @('牌桌', '叫地主阶段')) { continue }
    if ($cards.Count -ne $RequiredCount -or
        @($cards | Where-Object { $_.Current.Name.Contains('未知') }).Count -gt 0) { continue }
    $first = $cards | Where-Object { $_.Current.Name.StartsWith("第 $Position 张，") } |
        Select-Object -First 1
    if ($null -eq $first) { throw 'Selected card unavailable despite complete hand.' }
    $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    SendCardAction '{UP}'
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $status = Value '手牌识别与操作提示'
        if ($status.Contains('已稳定抬起') -or $status.Contains('未确认') -or $status.Contains('已取消')) { break }
    }
    Write-Output "pilot result=$status"
    if ($status.Contains('已稳定抬起')) {
        SendCardAction '{DOWN}'
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $status = Value '手牌识别与操作提示'
            if ($status.Contains('已回落') -or $status.Contains('未确认') -or $status.Contains('已取消')) { break }
        }
        Write-Output "drop result=$status"
    }
    break
}
