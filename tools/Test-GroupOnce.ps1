param([ValidateRange(1, 15)][int]$MaxScans = 10)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow

function Root { [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle) }
function Find-Named([string]$name, [string]$type) {
    $all = (Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $item = $all.Item($i)
        if ($item.Current.Name -eq $name -and
            $item.Current.ControlType.ProgrammaticName -eq $type) { return $item }
    }
    throw "UIA control unavailable: $name ($type)"
}
function Value([string]$name) {
    (Find-Named $name 'ControlType.Edit').GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Tab([string]$name) {
    (Find-Named $name 'ControlType.TabItem').GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Current-Cards {
    $all = (Root).FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -match '^第 (\d+) 张，点数候选：([^，]+)，花色候选：([^，]+)，未拿起$'
    } | ForEach-Object {
        $null = $_.Current.Name -match '^第 (\d+) 张，点数候选：([^，]+)，花色候选：([^，]+)，未拿起$'
        [pscustomobject]@{ Element = $_; Position = [int]$Matches[1]; Rank = $Matches[2]; Suit = $Matches[3] }
    })
}
function Wait-GroupResult([string]$successText) {
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 150
        $status = Value '手牌识别与操作提示'
        if ($status.Contains($successText)) { return $status }
        if ($status.Contains('整组操作停在')) { throw $status }
    }
    throw "Timed out waiting for group result: $(Value '手牌识别与操作提示')"
}

for ($attempt = 1; $attempt -le $MaxScans; $attempt++) {
    Tab '当前局面'
    $before = Value '识别说明'
    (Find-Named '重新扫描' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 120
        $now = Value '识别说明'
        if ($now -ne $before -and $now.Contains('截图时间')) { break }
    }
    $scene = Value '当前场景'
    Tab '手牌'
    $cards = Current-Cards
    Write-Output "scan=$attempt scene=$scene cards=$($cards.Count)"
    if ($scene -in @('结算', '收费页面', '超时离房弹窗')) { throw "Match ended in scene $scene before the test." }
    if ($scene -ne '牌桌' -or ($cards.Count -notin 1..17 -and $cards.Count -ne 20) -or
        @($cards | Where-Object { $_.Rank -eq '未知' -or $_.Suit -eq '未知' }).Count -ne 0) {
        continue
    }
    $group = $cards | Group-Object Rank | Where-Object { $_.Count -ge 2 -and $_.Count -le 4 } |
        Sort-Object Count -Descending | Select-Object -First 1
    if ($null -eq $group) { throw 'No verified duplicate rank is available in this hand.' }
    $positions = @($group.Group.Position | Sort-Object)
    $first = @($cards | Where-Object Position -eq $positions[0])[0]
    $first.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Named '在游戏拿起当前点数整组（实验）' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $raised = Wait-GroupResult '同点数牌已逐张抬起并核对'
    Write-Output "rank=$($group.Name) positions=$($positions -join ',') raised=$raised"
    (Find-Named '在游戏放下全部已抬起牌（实验）' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $dropped = Wait-GroupResult '已逐张放下并核对'
    Write-Output "dropped=$dropped"
    exit 0
}
throw 'No stable live table with a complete 1-17/20-card hand appeared before the scan limit.'
