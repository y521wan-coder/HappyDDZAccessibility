param([int]$MaxScans = 40)

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

if ((Value '当前场景') -eq '结算') {
    & "$PSScriptRoot\Activate-AssistantItem.ps1" -Target '继续游戏' -ExpectedScene '结算' -WaitSeconds 1 |
        Out-Null
    Start-Sleep -Seconds 1
}

for ($i = 1; $i -le $MaxScans; $i++) {
    $result = & "$PSScriptRoot\Scan-AssistantScene.ps1" -Connect
    Write-Output "scan=$i scene=$($result.Scene) gate=$($result.Detail.Contains('本人不出按钮与钟面已双帧确认'))"
    if ($result.Scene -eq '结算' -or $result.Scene -eq '超时离房弹窗') { break }
    if (-not $result.Detail.Contains('本人不出按钮与钟面已双帧确认')) {
        Start-Sleep -Milliseconds 120
        continue
    }
    (Find-Named '手牌' 'ControlType.TabItem').GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $button = Find-Named '过牌' 'ControlType.Button'
    if (-not $button.Current.IsEnabled) { throw 'Pass gate reported but button is disabled.' }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 150
        $status = Value '手牌识别与操作提示'
        if ($status.Contains('已发送') -or $status.Contains('已取消') -or
            $status.Contains('未确认')) { Write-Output "pass-result=$status"; break }
    }
    break
}
