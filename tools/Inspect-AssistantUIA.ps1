param([switch]$InvokeScan)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. "$PSScriptRoot\Get-AssistantWindow.ps1"
$assistant = Get-AssistantWindow
$root = [System.Windows.Automation.AutomationElement]::FromHandle($assistant.Handle)
if ($null -eq $root) { throw 'Assistant UIA root not found.' }
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $elements.Count; $i++) {
    $item = $elements.Item($i)
    $current = $item.Current
    if ($current.ControlType.ProgrammaticName -match 'Button|TabItem|Edit|List|CheckBox|ComboBox') {
        [pscustomobject]@{
            Type = $current.ControlType.ProgrammaticName
            Name = $current.Name
            Enabled = $current.IsEnabled
            Focusable = $current.IsKeyboardFocusable
        }
    }
}
if ($InvokeScan) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '当前局面')
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $overview = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.AndCondition($nameCondition, $typeCondition)))
    ($overview.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    $scan = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, '重新扫描')))
    if ($null -eq $scan) { throw 'Scan button not found in UIA.' }
    $pattern = $scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Seconds 5
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($name in @('连接状态', '当前场景', '识别说明', '当前画面公开文字')) {
        for ($i = 0; $i -lt $elements.Count; $i++) {
            $item = $elements.Item($i)
            if ($item.Current.Name -eq $name -and
                $item.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit') {
                $value = ''
                $value = ($item.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
                [pscustomobject]@{ Name = $name; Value = $value }
                break
            }
        }
    }
}
