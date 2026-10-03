param([int]$Count = 2)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
$tabCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TabItem)
$tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
for ($i = 0; $i -lt $tabs.Count; $i++) {
    if ($tabs.Item($i).Current.Name -eq '当前局面') {
        ($tabs.Item($i).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    }
}
$scan = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '重新扫描')))
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$scene = $null
for ($i = 0; $i -lt $elements.Count; $i++) {
    $candidate = $elements.Item($i)
    if ($candidate.Current.Name -eq '当前场景' -and
        $candidate.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit') {
        $scene = $candidate
        break
    }
}
if ($null -eq $scan -or $null -eq $scene) { throw 'Assistant scan controls not found.' }
$invoke = $scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$value = $scene.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
for ($run = 1; $run -le $Count; $run++) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $invoke.Invoke()
    do {
        Start-Sleep -Milliseconds 100
        $current = $value.Current.Value
    } while ($current -eq '正在识别' -and $watch.Elapsed.TotalSeconds -lt 12)
    [pscustomobject]@{ Run = $run; Milliseconds = [math]::Round($watch.Elapsed.TotalMilliseconds); Scene = $current }
}
