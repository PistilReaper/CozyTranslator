param([int]$AppProcessId, [switch]$Exercise)
$ErrorActionPreference = 'Stop'
if ($Exercise) {
    & (Join-Path $PSScriptRoot 'window-check.ps1') -AppProcessId $AppProcessId
    return
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$filter = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppProcessId)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $filter)
foreach ($window in $windows) {
    [PSCustomObject]@{Name=$window.Current.Name;Offscreen=$window.Current.IsOffscreen;Handle=$window.Current.NativeWindowHandle} | ConvertTo-Json -Compress
}
