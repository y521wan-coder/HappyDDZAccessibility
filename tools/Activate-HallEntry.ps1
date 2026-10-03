Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$entry = $null
$button = $null
for ($i = 0; $i -lt $elements.Count; $i++) {
    $item = $elements.Item($i)
    if ($item.Current.Name -match '^第 \d+ 项，游戏入口：欢乐斗地主\(') { $entry = $item }
    if ($item.Current.Name -eq '点击所选文字') { $button = $item }
}
if ($null -eq $entry -or $null -eq $button) { throw 'Verified hall entry is unavailable.' }
($entry.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
if (-not $button.Current.IsEnabled) { throw 'Entry action is disabled.' }
($button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
Write-Output 'Requested one verified hall-entry action; check assistant result before any retry.'

