Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
$tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$tab = $null
for ($i = 0; $i -lt $tabs.Count; $i++) {
    $item = $tabs.Item($i)
    if ($item.Current.Name -eq '手牌' -and
        $item.Current.ControlType.ProgrammaticName -eq 'ControlType.TabItem') {
        $tab = $item
        break
    }
}
if ($null -eq $tab) { throw 'Hand tab not found.' }
($tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
foreach ($i in 0..($elements.Count - 1)) {
    $current = $elements.Item($i).Current
    if ($current.Name -match '手牌|刷新|出牌|上一组|下一组|拿起|放下') {
        [pscustomobject]@{
            Type = $current.ControlType.ProgrammaticName
            Name = $current.Name
            Enabled = $current.IsEnabled
            Focusable = $current.IsKeyboardFocusable
        }
    }
}

