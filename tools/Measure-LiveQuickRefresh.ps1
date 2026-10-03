$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
function Find-Named($name, $type) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $item = $all.Item($i)
        if ($item.Current.Name -eq $name -and
            $item.Current.ControlType.ProgrammaticName -eq $type) { return $item }
    }
    throw "UIA control unavailable: $name ($type)"
}

($overview = Find-Named '当前局面' 'ControlType.TabItem').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$scan = Find-Named '重新扫描' 'ControlType.Button'
$scene = Find-Named '当前场景' 'ControlType.Edit'
$scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 3
$sceneValue = $scene.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if ($sceneValue -notin @('牌桌', '叫地主阶段', '对局回放')) {
    throw "Current scene is $sceneValue; no quick refresh requested."
}

(Find-Named '手牌' 'ControlType.TabItem').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$button = Find-Named '快速刷新手牌' 'ControlType.Button'
$status = Find-Named '手牌识别与操作提示' 'ControlType.Edit'
$watch = [Diagnostics.Stopwatch]::StartNew()
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
do {
    Start-Sleep -Milliseconds 50
    $value = $status.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
} while ($value -notmatch '快速刷新 \d+ 毫秒|快速刷新失败' -and $watch.Elapsed.TotalSeconds -lt 12)
[pscustomobject]@{
    Scene = $sceneValue
    WallMilliseconds = [math]::Round($watch.Elapsed.TotalMilliseconds)
    Status = $value
}
