param([switch]$Connect)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
function Find-Named([string]$name, [string]$type) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $all.Count; $i++) {
        $item = $all.Item($i)
        if ($item.Current.Name -eq $name -and
            $item.Current.ControlType.ProgrammaticName -eq $type) { return $item }
    }
    throw "UIA control unavailable: $name ($type)"
}
(Find-Named '当前局面' 'ControlType.TabItem').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
if ($Connect) {
    (Find-Named '连接游戏' 'ControlType.Button').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
$detailElement = Find-Named '识别说明' 'ControlType.Edit'
$detailBefore = $detailElement.GetCurrentPattern(
    [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
(Find-Named '重新扫描' 'ControlType.Button').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$deadline = [DateTime]::UtcNow.AddSeconds(8)
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $current = $detailElement.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($current -ne $detailBefore -and $current.Contains('截图时间')) { break }
}
$scene = Find-Named '当前场景' 'ControlType.Edit'
$detail = Find-Named '识别说明' 'ControlType.Edit'
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$clickable = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $item = $all.Item($i)
    if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $item.Current.Name.Contains('可按回车点击')) { $clickable += $item.Current.Name }
}
[pscustomobject]@{
    Scene = $scene.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Detail = $detail.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Clickable = $clickable -join ' | '
}
