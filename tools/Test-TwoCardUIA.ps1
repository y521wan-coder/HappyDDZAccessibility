param([int]$MaxScans = 8)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow

function Find-Named([string]$name, [string]$type) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $all) {
        if ($item.Current.Name -eq $name -and
            $item.Current.ControlType.ProgrammaticName -eq $type) { return $item }
    }
    throw "UIA control unavailable: $name"
}
function Value([string]$name) {
    (Find-Named $name 'ControlType.Edit').GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Tab([string]$name) {
    (Find-Named $name 'ControlType.TabItem').GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Wait-Status([string]$expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(9)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 120
        $status = Value '手牌识别与操作提示'
        if ($status.Contains($expected)) { return $status }
        if ($status.Contains('未确认') -or $status.Contains('已取消')) { throw $status }
    }
    throw "Timed out waiting for $expected; status: $(Value '手牌识别与操作提示')"
}
function Toggle([int]$position, [string]$expected) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $cards = @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name.StartsWith("第 $position 张，点数候选：")
    })
    if ($cards.Count -ne 1) { throw "Card $position missing from assistant list." }
    $cards[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Named '在游戏拿起或放下当前一张（实验）' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $status = Wait-Status $expected
    Write-Output "position=$position status=$status"
}

Tab '当前局面'
if ((Value '当前场景') -ne '场次等候页') { throw 'Expected verified waiting room.' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$start = @($all | Where-Object {
    $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
    $_.Current.Name.Contains('进入普通匹配按钮：开始游戏') -and
    $_.Current.Name.Contains('可按回车点击')
})
if ($start.Count -ne 1) { throw 'Verified start item unavailable.' }
$start[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
(Find-Named '点击所选文字' 'ControlType.Button').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 2

for ($attempt = 1; $attempt -le $MaxScans; $attempt++) {
    Tab '当前局面'
    $detailBefore = Value '识别说明'
    (Find-Named '重新扫描' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(7)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 120
        $detailNow = Value '识别说明'
        if ($detailNow -ne $detailBefore -and $detailNow.Contains('截图时间')) { break }
    }
    $scene = Value '当前场景'
    Tab '手牌'
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $cards = @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -match '^第 \d+ 张，点数候选：'
    })
    Write-Output "scan=$attempt scene=$scene cards=$($cards.Count)"
    if ($scene -in @('结算', '收费页面', '超时离房弹窗')) { break }
    if ($scene -notin @('牌桌', '叫地主阶段') -or $cards.Count -ne 17 -or
        @($cards | Where-Object { $_.Current.Name.Contains('未知') }).Count -ne 0) { continue }
    Toggle 2 '稳定抬起第 2 张'
    Toggle 8 '稳定抬起第 2、8 张'
    Toggle 8 '稳定抬起第 2 张'
    Toggle 2 '已回落'
    break
}
