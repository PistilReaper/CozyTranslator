param([Parameter(Mandatory=$true)][int]$AppProcessId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ThemeMouse {
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
}
'@
$oldDpi=[ThemeMouse]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$cursor=[ThemeMouse+POINT]::new();$null=[ThemeMouse]::GetCursorPos([ref]$cursor)
$processFilter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppProcessId)
function Find-Element([string]$name,$type=$null) {
 $roots=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$processFilter)
 $filter=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
 foreach($root in $roots){
  if($root.Current.Name -eq $name -and -not $root.Current.IsOffscreen -and ($null -eq $type -or $root.Current.ControlType -eq $type)){return $root}
  $items=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)
  foreach($item in $items){if(-not $item.Current.IsOffscreen -and ($null -eq $type -or $item.Current.ControlType -eq $type)){return $item}}
 }
 return $null
}
function Await-Element([string]$name,$type=$null){for($i=0;$i -lt 35;$i++){$item=Find-Element $name $type;if($null -ne $item){return $item};Start-Sleep -Milliseconds 100};throw "Element not found: $name"}
function Click($element) {
 $r=$element.Current.BoundingRectangle
 $point=[ThemeMouse+POINT]::new();$point.X=[int]($r.Left+$r.Width/2);$point.Y=[int]($r.Top+$r.Height/2)
 $owner=[ThemeMouse]::GetAncestor([ThemeMouse]::WindowFromPoint($point),2)
 [uint32]$ownerId=0;$null=[ThemeMouse]::GetWindowThreadProcessId($owner,[ref]$ownerId)
 if($ownerId -ne $AppProcessId){throw 'Target is covered by another application'}
 $null=[ThemeMouse]::SetCursorPos($point.X,$point.Y);Start-Sleep -Milliseconds 150
 [ThemeMouse]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 60;[ThemeMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 350
}
function Selection($combo){return $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name}
function Choose($combo,[string]$value){Click $combo;Click (Await-Element $value ([System.Windows.Automation.ControlType]::ListItem))}
function Paper-Color($window) {
 $r=$window.Current.BoundingRectangle;$bitmap=[System.Drawing.Bitmap]::new(1,1);$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.CopyFromScreen([int]$r.Left+12,[int]$r.Top+90,0,0,[System.Drawing.Size]::new(1,1));$c=$bitmap.GetPixel(0,0);return '#{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B}finally{$graphics.Dispose();$bitmap.Dispose()}
}
$failures=0;$settings=$null;$opened=$false
try {
 $exe=(Get-Process -Id $AppProcessId).Path
 $second=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru;$null=$second.WaitForExit(5000)
 $main=Await-Element 'CozyTranslator';$settings=Find-Element 'CozyTranslator · 设置'
 if($null -eq $settings){(Await-Element '设置' ([System.Windows.Automation.ControlType]::Button)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();$settings=Await-Element 'CozyTranslator · 设置';$opened=$true}
 Start-Sleep -Milliseconds 500
 $theme=Await-Element '主题' ([System.Windows.Automation.ControlType]::ComboBox)
 $original=Selection $theme
 Write-Output ('Initial theme: '+$original+'; paper: '+(Paper-Color $settings))
 foreach($case in @(@('深色','#242522'),@('浅色','#F8F6F1'),@('跟随系统',$(if((Get-ItemProperty -LiteralPath 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Themes/Personalize').AppsUseLightTheme -eq 0){'#242522'}else{'#F8F6F1'})))){
  Choose $theme $case[0];$selected=Selection $theme;$color=Paper-Color $settings
  if($selected -eq $case[0] -and $color -eq $case[1]){Write-Output ('PASS Mouse selects '+$case[0]+' and window renders '+$color)}else{$failures++;Write-Output ('FAIL Requested '+$case[0]+'; selected='+$selected+'; paper='+$color)}
 }
} finally {
 [ThemeMouse]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 if($settings -and $theme){
  if((Selection $theme) -ne $original){Choose $theme $original}
 }
 if($opened){(Await-Element '关闭设置' ([System.Windows.Automation.ControlType]::Button)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
 $null=[ThemeMouse]::SetCursorPos($cursor.X,$cursor.Y);$null=[ThemeMouse]::SetThreadDpiAwarenessContext($oldDpi)
}
if($failures){throw "$failures theme interaction checks failed"}
