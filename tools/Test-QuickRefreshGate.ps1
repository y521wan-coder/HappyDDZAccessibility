Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = Get-Process 'HappyDDZ.Assistant' -ErrorAction Stop | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $elements.Count; $i++) {
    $item = $elements.Item($i)
    if ($item.Current.Name -eq '手牌' -and
        $item.Current.ControlType.ProgrammaticName -eq 'ControlType.TabItem') {
        ($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        break
    }
}
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$button = $null
$status = $null
for ($i = 0; $i -lt $elements.Count; $i++) {
    $item = $elements.Item($i)
    if ($item.Current.Name -eq '快速刷新手牌') { $button = $item }
    if ($item.Current.Name -eq '手牌识别与操作提示') { $status = $item }
}
if ($null -eq $button -or $null -eq $status) { throw 'Hand controls missing.' }
($button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
Start-Sleep -Seconds 1
$value = ($status.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
if ($value -notmatch '完整扫描|窗口未连接|快速刷新需要') {
    throw "Unexpected refresh gate result: $value"
}
Write-Output "Safe refresh rejection: $value"

