$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"

$root = [System.Windows.Automation.AutomationElement]::FromHandle(
    (Get-AssistantWindow).Handle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$tabs = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $item = $all.Item($i)
    if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::TabItem) {
        $tabs += $item
    }
}

$checked = 0
$failures = @()
$types = @(
    [System.Windows.Automation.ControlType]::Button,
    [System.Windows.Automation.ControlType]::CheckBox,
    [System.Windows.Automation.ControlType]::ComboBox,
    [System.Windows.Automation.ControlType]::List,
    [System.Windows.Automation.ControlType]::Edit
)
foreach ($tab in $tabs) {
    $tab.GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 100
    $controls = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $controls.Count; $i++) {
        $control = $controls.Item($i)
        if ($types -notcontains $control.Current.ControlType -or
            -not $control.Current.IsEnabled) { continue }
        # A combo box exposes an internal drop-down button; arrow keys operate
        # its focusable parent, so the child button is not a separate action.
        if ($control.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($control)
            if ($null -ne $parent -and
                $parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox) {
                continue
            }
        }
        $checked++
        if (-not $control.Current.IsKeyboardFocusable) {
            $failures += "$($tab.Current.Name): $($control.Current.Name)"
        }
    }
}

[pscustomobject]@{
    Tabs = $tabs.Count
    EnabledControls = $checked
    NotKeyboardFocusable = $failures.Count
    Failures = $failures -join ' | '
}
if ($failures.Count -gt 0) { exit 1 }
