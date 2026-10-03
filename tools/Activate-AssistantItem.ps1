param(
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$ExpectedScene = '',
    [ValidateRange(0, 10)][int]$WaitSeconds = 2
)

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

$scene = Find-Named '当前场景' 'ControlType.Edit'
$before = $scene.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if ($ExpectedScene -and $before -ne $ExpectedScene) {
    throw "Current scene is $before; expected $ExpectedScene."
}

$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$matches = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $item = $all.Item($i)
    if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
        $item.Current.Name.Contains($Target) -and
        $item.Current.Name.Contains('可按回车点击')) { $matches += $item }
}
if ($matches.Count -ne 1) { throw "Expected one clickable item for '$Target'; found $($matches.Count)." }
$item = $matches[0]
$itemName = $item.Current.Name
$button = Find-Named '点击所选文字' 'ControlType.Button'
($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
if (-not $button.Current.IsEnabled) { throw 'Action is disabled.' }
($button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
if ($WaitSeconds -gt 0) { Start-Sleep -Seconds $WaitSeconds }
[pscustomobject]@{
    Before = $before
    Requested = $itemName
    After = $scene.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Detail = (Find-Named '识别说明' 'ControlType.Edit').GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
