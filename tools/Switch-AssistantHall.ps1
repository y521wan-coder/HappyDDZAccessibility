Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
$combo = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '目标窗口')))
if ($null -eq $combo) { throw 'Window selector not found.' }
($combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
$items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$hall = $null
for ($i = 0; $i -lt $items.Count; $i++) {
    if ($items.Item($i).Current.Name -eq 'QQ 游戏大厅') { $hall = $items.Item($i); break }
}
if ($null -eq $hall) { throw 'QQ hall option not found.' }
($hall.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
$scan = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '重新扫描')))
if ($null -eq $scan) { throw 'Scan button not found.' }
($scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
Start-Sleep -Seconds 4
Write-Output 'Selected QQ hall and requested scan.'

