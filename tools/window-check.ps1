param([Parameter(Mandatory=$true)][int]$AppProcessId, [switch]$ProbeOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowInput {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
'@
$oldDpi=[WindowInput]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
function Find-Window([string]$Name) {
  $windows=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
  $nameFilter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name)
  foreach($w in $windows){
    if($w.Current.Name -eq $Name){return $w}
    $child=$w.FindFirst([System.Windows.Automation.TreeScope]::Children,$nameFilter)
    if($null -ne $child){return $child}
  }
  return $null
}
function Await-Window([string]$Name) {
  for($i=0;$i -lt 40;$i++){ $w=Find-Window $Name;if($null -ne $w){return $w};Start-Sleep -Milliseconds 100 }
  throw "Window not found: $Name"
}
function Click-Button($window,[string]$name) {
  $filter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
  $button=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$filter)
  if($null -eq $button){throw "Button not found: $name"}
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 200
}
function Bounds([IntPtr]$handle){$rect=[WindowInput+RECT]::new();$null=[WindowInput]::GetWindowRect($handle,[ref]$rect);return $rect}
function Drag([IntPtr]$handle,[int]$x,[int]$y,[int]$dx,[int]$dy) {
  $point=[WindowInput+POINT]::new();$point.X=$x;$point.Y=$y
  if([WindowInput]::GetAncestor([WindowInput]::WindowFromPoint($point),2) -ne $handle){throw 'Pointer is not over the test application'}
  $null=[WindowInput]::SetCursorPos($x,$y)
  Start-Sleep -Milliseconds 120
  [WindowInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  try{for($i=1;$i -le 8;$i++){Start-Sleep -Milliseconds 35;$null=[WindowInput]::SetCursorPos($x+$dx*$i/8,$y+$dy*$i/8)}}
  finally{[WindowInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
  Start-Sleep -Milliseconds 200
}
function Wake-Instance {
  $exe=(Get-Process -Id $AppProcessId).Path
  $second=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
  if(-not $second.WaitForExit(6000)){throw 'Second instance did not exit'}
  return Await-Window 'CozyTranslator'
}
$cursor=[WindowInput+POINT]::new();$null=[WindowInput]::GetCursorPos([ref]$cursor)
$failures=0
function Check([string]$name,[scriptblock]$action){try{& $action;Write-Output "PASS $name"}catch{$script:failures++;Write-Output "FAIL ${name}: $($_.Exception.Message)"}}
try {
  $panel=Await-Window 'CozyTranslator';$handle=[IntPtr]::new($panel.Current.NativeWindowHandle)
  $null=$panel.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).WaitForInputIdle(5000)
  Start-Sleep -Milliseconds 600
  $original=Bounds $handle
  $scale=[WindowInput]::GetDpiForWindow($handle)/96.0
  $null=[WindowInput]::SetWindowPos($handle,[IntPtr]::Zero,100,100,(480*$scale),(620*$scale),0x14)
  $null=[WindowInput]::SetForegroundWindow($handle);Start-Sleep -Milliseconds 400
  Check 'Real mouse drags the blank title area' {
    $before=Bounds $handle;Drag $handle ($before.Left+210*$scale) ($before.Top+38*$scale) 72 48;$after=Bounds $handle
    if([Math]::Abs($after.Left-$before.Left-72) -gt 3 -or [Math]::Abs($after.Top-$before.Top-48) -gt 3){throw "Window did not follow the mouse: delta=($($after.Left-$before.Left),$($after.Top-$before.Top)), size=($($before.Right-$before.Left),$($before.Bottom-$before.Top)), dpi=$scale"}
  }
  Check 'Real mouse resizes the right edge' {
    $before=Bounds $handle;Drag $handle ($before.Right-2) (($before.Top+$before.Bottom)/2) 70 0;$after=Bounds $handle
    if([Math]::Abs($after.Right-$before.Right-70) -gt 3){throw "Right edge did not resize: delta=$($after.Right-$before.Right), width=$($before.Right-$before.Left) -> $($after.Right-$after.Left)"}
  }
  Check 'Real mouse drags blank space outside the title' {
    $before=Bounds $handle;Drag $handle ($before.Left+10*$scale) ($before.Top+300*$scale) 50 35;$after=Bounds $handle
    if([Math]::Abs($after.Left-$before.Left-50) -gt 3 -or [Math]::Abs($after.Top-$before.Top-35) -gt 3){throw 'Blank body space did not move the window'}
  }
  Check 'Real mouse drags the blank result area' {
    $before=Bounds $handle;Drag $handle (($before.Left+$before.Right)/2) ($before.Bottom-180*$scale) 40 20;$after=Bounds $handle
    if([Math]::Abs($after.Left-$before.Left-40) -gt 3 -or [Math]::Abs($after.Top-$before.Top-20) -gt 3){throw 'Blank result area did not move the window'}
  }
  Check 'Dragging inside the source editor does not move the window' {
    $sourceFilter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'原文')
    $candidates=$panel.FindAll([System.Windows.Automation.TreeScope]::Descendants,$sourceFilter)
    $editor=$null;foreach($candidate in $candidates){if($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit){$editor=$candidate}}
    if($null -eq $editor){throw 'Source editor not found'}
    $area=$editor.Current.BoundingRectangle;$before=Bounds $handle
    Drag $handle ($area.Left+15) ($area.Top+10) 25 5;$after=Bounds $handle
    if($after.Left -ne $before.Left -or $after.Top -ne $before.Top){throw 'Editor drag moved the window'}
  }
  Check 'Real mouse resizes the bottom edge' {
    $before=Bounds $handle;Drag $handle (($before.Left+$before.Right)/2) ($before.Bottom-2) 0 -60;$after=Bounds $handle
    if([Math]::Abs($after.Bottom-$before.Bottom+60) -gt 3){throw 'Bottom edge did not resize'}
  }
  if(-not $ProbeOnly){
    Check 'Main window is eligible for the taskbar' {if(([WindowInput]::GetWindowLong($handle,-20) -band 0x40080) -ne 0x40000){throw 'Wrong taskbar window style'}}
    Check 'Windows taskbar exposes the running app button' {
      $taskbarFilter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty,'Shell_TrayWnd')
      $taskbar=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$taskbarFilter)
      if($null -eq $taskbar){throw 'Windows taskbar not found'}
      $buttonFilter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
      $buttons=$taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants,$buttonFilter)
      $found=$false
      foreach($button in $buttons){if($button.Current.Name -like '*CozyTranslator*' -and $button.Current.ClassName -like '*TaskList*'){$found=$true}}
      if(-not $found){throw 'Running app taskbar button not found'}
    }
    Check 'Title settings button remains clickable' {Click-Button $panel '设置';$settings=Await-Window 'CozyTranslator · 设置';Click-Button $settings '关闭设置'}
    Check 'Minimize retains taskbar entry and reopening restores it' {
      Click-Button $panel '最小化'
      if(-not [WindowInput]::IsIconic($handle) -or -not [WindowInput]::IsWindowVisible($handle)){throw 'Minimize hid the window'}
      $null=Wake-Instance;if([WindowInput]::IsIconic($handle)){throw 'Reopening left the window minimized'}
    }
    Check 'Hide to tray removes both windows and can reopen' {
      Click-Button $panel '隐藏到托盘'
      if([WindowInput]::IsWindowVisible($handle) -or $null -ne (Find-Window 'CozyTranslator 悬浮球')){throw 'Hide to tray left a visible window'}
      $null=Wake-Instance;if(-not [WindowInput]::IsWindowVisible($handle)){throw 'Wake did not restore main window'}
    }
    Check 'Esc opens bubble; second instance restores main window' {
      $null=[WindowInput]::PostMessage($handle,0x100,[IntPtr]::new(27),[IntPtr]::Zero)
      $null=Await-Window 'CozyTranslator 悬浮球';$null=Wake-Instance
      if(-not [WindowInput]::IsWindowVisible($handle)){throw 'Main window not restored'}
    }
    Check 'Native close hides to tray without exiting' {
      $null=[WindowInput]::PostMessage($handle,0x10,[IntPtr]::Zero,[IntPtr]::Zero);Start-Sleep -Milliseconds 200
      if([WindowInput]::IsWindowVisible($handle)){throw 'Window remains visible'}
      $null=Get-Process -Id $AppProcessId;$null=Wake-Instance
    }
  }
} finally {
  [WindowInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  $null=[WindowInput]::SetCursorPos($cursor.X,$cursor.Y)
  if($original){
    $null=[WindowInput]::SetWindowPos($handle,[IntPtr]::Zero,$original.Left,$original.Top,($original.Right-$original.Left),($original.Bottom-$original.Top),0x14)
    if(-not $ProbeOnly){
      $null=[WindowInput]::PostMessage($handle,0x10,[IntPtr]::Zero,[IntPtr]::Zero);Start-Sleep -Milliseconds 200
      $null=Wake-Instance
    }
  }
  $null=[WindowInput]::SetThreadDpiAwarenessContext($oldDpi)
}
if($failures -gt 0){throw "$failures window checks failed"}
