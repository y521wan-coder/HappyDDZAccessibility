param([int]$MaxScans = 35)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow

function Find-Named([string]$name, [string]$type) {
    for ($retry = 0; $retry -lt 4; $retry++) {
        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
            $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($item in $all) {
                if ($item.Current.Name -eq $name -and
                    $item.Current.ControlType.ProgrammaticName -eq $type) { return $item }
            }
            throw "Missing $name ($type)"
        }
        catch [System.Windows.Automation.ElementNotAvailableException] {
            Start-Sleep -Milliseconds 120
        }
    }
    throw "UIA tree unavailable: $name"
}
function Value([string]$name) {
    (Find-Named $name 'ControlType.Edit').GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Wait-Status([string]$expected) {
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 120
        $status = Value '手牌识别与操作提示'
        if ($status.Contains($expected)) { return $status }
        if ($status.Contains('已取消') -or $status.Contains('未确认')) { throw $status }
    }
    throw "Timed out waiting for ${expected}: $(Value '手牌识别与操作提示')"
}

if ((Value '当前场景') -eq '结算') {
    & "$PSScriptRoot\Activate-AssistantItem.ps1" -Target '继续游戏' -ExpectedScene '结算' -WaitSeconds 1 |
        Out-Null
    Start-Sleep -Seconds 1
}
if ((Value '当前场景') -eq '场次等候页') {
    & "$PSScriptRoot\Activate-AssistantItem.ps1" -Target '进入普通匹配按钮：开始游戏' -ExpectedScene '场次等候页' -WaitSeconds 1 |
        Out-Null
    Start-Sleep -Seconds 1
}

for ($i = 1; $i -le $MaxScans; $i++) {
    $result = & "$PSScriptRoot\Scan-AssistantScene.ps1" -Connect
    $playGate = $result.Detail.Contains('本人出牌按钮与钟面已双帧确认')
    $passGate = $result.Detail.Contains('本人不出按钮与钟面已双帧确认')
    Write-Output "scan=$i scene=$($result.Scene) play=$playGate pass=$passGate detail=$($result.Detail)"
    if ($result.Scene -eq '结算' -or $result.Scene -eq '超时离房弹窗') { break }
    if (-not $playGate) { Start-Sleep -Milliseconds 100; continue }
    (Find-Named '手牌' 'ControlType.TabItem').GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $cards = @($all | Where-Object {
        $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $_.Current.Name -match '^第 \d+ 张，点数候选：'
    })
    if ($cards.Count -notin @(17,20) -or
        @($cards | Where-Object { $_.Current.Name.Contains('未知') }).Count -ne 0) { continue }
    $first = @($cards | Where-Object { $_.Current.Name.StartsWith('第 1 张，') })
    if ($first.Count -ne 1) { throw 'First card missing.' }
    $first[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Find-Named '在游戏拿起或放下当前一张（实验）' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "lift-result=$(Wait-Status '已稳定抬起第 1 张')"
    $playButton = Find-Named '出牌' 'ControlType.Button'
    if (-not $playButton.Current.IsEnabled) { throw 'Play button remained disabled after verified lift.' }
    $playButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "play-result=$(Wait-Status '出牌单次点击已发送')"
    for ($followup = 1; $followup -le 3; $followup++) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        $remaining = @($all | Where-Object {
            $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
            $_.Current.Name -match '^第 \d+ 张，点数候选：'
        })
        if ($remaining.Count -ne 16 -or
            @($remaining | Where-Object { $_.Current.Name.Contains('未知') }).Count -ne 0) {
            Start-Sleep -Milliseconds 250
            continue
        }
        $nextFirst = @($remaining | Where-Object { $_.Current.Name.StartsWith('第 1 张，') })
        if ($nextFirst.Count -ne 1) { throw 'Sixteen-card first item unavailable.' }
        $nextFirst[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        (Find-Named '在游戏拿起或放下当前一张（实验）' 'ControlType.Button').GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Write-Output "16-lift-result=$(Wait-Status '已稳定抬起第 1 张')"
        (Find-Named '在游戏拿起或放下当前一张（实验）' 'ControlType.Button').GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Write-Output "16-drop-result=$(Wait-Status '已回落')"
        break
    }
    break
}
