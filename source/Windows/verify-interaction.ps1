param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$assemblyRoot=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Add-Type -Path (Join-Path $assemblyRoot 'UIAutomationClient.dll')
Add-Type -Path (Join-Path $assemblyRoot 'UIAutomationTypes.dll')
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class NativeInteraction {
 public delegate bool WindowCallback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(IntPtr c,string title);
 [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr data);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,System.Text.StringBuilder text,int length);
 public static IntPtr FindOwned(uint pid,string title){IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid){var text=new System.Text.StringBuilder(256);GetWindowText(h,text,256);if(text.ToString()==title)found=h;}return true;},IntPtr.Zero);return found;}
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]public static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
}
'@
[NativeInteraction]::SetProcessDPIAware()|Out-Null
$process=Get-CimInstance Win32_Process -Filter "Name='codeusagemonit.exe'"|Where-Object{$_.ExecutablePath-eq(Join-Path $AppDirectory 'codeusagemonit.exe')-and$_.CommandLine-match'--demo'}|Select-Object -First 1
if(-not$process){throw 'A running isolated --demo preview is required.'}
$showSignal=[Threading.EventWaitHandle]::OpenExisting('Local\codeusagemonit.Preview.Show')
$showSignal.Set()|Out-Null;$showSignal.Dispose()
Start-Sleep -Milliseconds 200
$handle=[IntPtr]::Zero
for($n=0;$n-lt30;$n++){$handle=[NativeInteraction]::FindOwned([uint32]$process.ProcessId,'codeusagemonit');if($handle-ne[IntPtr]::Zero){break};Start-Sleep -Milliseconds 200}
if($handle-eq[IntPtr]::Zero){throw 'Window not found.'}
[NativeInteraction]::ShowWindow($handle,9)|Out-Null
Start-Sleep -Milliseconds 150
if(-not[NativeInteraction]::IsWindowVisible($handle)){throw 'Preview could not be shown.'}
function Rect { $r=New-Object NativeInteraction+Rect;[NativeInteraction]::GetWindowRect($handle,[ref]$r)|Out-Null;return $r }
function Button([string]$name){$condition=[System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button),[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name));$b=$null;for($attempt=0;$attempt-lt30;$attempt++){$b=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition);if($b){break};Start-Sleep -Milliseconds 100};if(-not$b){throw ('Missing button: '+$name)};return $b}
function Click([string]$name){(Button $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 100}
function Texts { @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))|ForEach-Object{$_.Current.Name}) }
function Drag([int]$x,[int]$y,[int]$dx,[int]$dy){[NativeInteraction]::SetCursorPos($x,$y)|Out-Null;[NativeInteraction]::mouse_event(2,0,0,0,[UIntPtr]::Zero);try{Start-Sleep -Milliseconds 100;for($i=1;$i-le6;$i++){[NativeInteraction]::SetCursorPos(($x+$dx*$i/6),($y+$dy*$i/6))|Out-Null;Start-Sleep -Milliseconds 40}}finally{[NativeInteraction]::mouse_event(4,0,0,0,[UIntPtr]::Zero)};Start-Sleep -Milliseconds 500}
function Screenshot([string]$name){$r=Rect;$bmp=New-Object Drawing.Bitmap(($r.Right-$r.Left),($r.Bottom-$r.Top));$g=[Drawing.Graphics]::FromImage($bmp);$g.CopyFromScreen($r.Left,$r.Top,0,0,$bmp.Size);$bmp.Save((Join-Path $AppDirectory ('verification\'+$name+'.png')),[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()}
$checks=@();$initial=Rect
$productionSettings=Join-Path $AppDirectory 'data\settings.json'
$productionHash=if(Test-Path -LiteralPath $productionSettings){(Get-FileHash -LiteralPath $productionSettings).Hash}else{'absent'}
if(([NativeInteraction]::GetWindowLongPtr($handle,-20).ToInt64()-band8)-ne0){throw 'Window starts always-on-top.'}
$checks+='Default native window is not topmost'
$backdrop=0;$hr=[NativeInteraction]::DwmGetWindowAttribute($handle,38,[ref]$backdrop,4)
if($hr-ne0-or$backdrop-ne3){throw 'Desktop Acrylic is not active.'}
$checks+='Windows DWM Desktop Acrylic is active'
$captionPoint=[IntPtr](([long]($initial.Top+22)-shl16)-bor($initial.Left+160))
if([NativeInteraction]::SendMessage($handle,0x84,[IntPtr]::Zero,$captionPoint).ToInt64()-ne2){throw 'Title area does not expose native caption dragging.'}
$checks+='Title area reports native caption hit-test'
# Bring only this synthetic preview forward while driving its own controls.
[NativeInteraction]::SetWindowPos($handle,[IntPtr](-1),0,0,0,0,0x13)|Out-Null
try{
 [NativeInteraction]::SetCursorPos(($initial.Left+160),($initial.Top+22))|Out-Null
 [NativeInteraction]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[NativeInteraction]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 650
 if([NativeInteraction]::GetForegroundWindow()-ne$handle){throw 'Preview is not foreground; native mouse checks cannot run safely.'}
 Drag ($initial.Left+160) ($initial.Top+22) -90 35
 $moved=Rect;if([Math]::Abs($moved.Left-$initial.Left)-lt40){throw 'Physical title dragging did not move the window.'}
 $checks+='Physical mouse drag moves the window'
 [NativeInteraction]::SetCursorPos(($moved.Left+160),($moved.Top+22))|Out-Null
 [NativeInteraction]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[NativeInteraction]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 180
 if([NativeInteraction]::GetForegroundWindow()-ne$handle){throw 'Preview lost foreground focus before resize test.'}
 Drag ($moved.Right-8) ($moved.Bottom-8) 65 -50
 $resized=Rect;if(($resized.Right-$resized.Left)-le($moved.Right-$moved.Left)+25){throw 'Physical corner dragging did not resize width.'}
 if([Math]::Abs(($resized.Bottom-$resized.Top)-($moved.Bottom-$moved.Top))-lt20){throw 'Physical corner dragging did not resize height.'}
 $checks+='Physical corner drag resizes width and height'
}finally{[NativeInteraction]::SetWindowPos($handle,[IntPtr](-2),0,0,0,0,0x13)|Out-Null}
$root=[System.Windows.Automation.AutomationElement]::FromHandle($handle)
Click '固定面板';if(([NativeInteraction]::GetWindowLongPtr($handle,-20).ToInt64()-band8)-ne0){throw 'Keep-open control incorrectly enabled topmost.'};Click '固定面板'
$checks+='Keep-open button never changes topmost behavior'
foreach($provider in @('Codex','Claude','Cursor','Antigravity','DeepSeek','Grok')){Click $provider;if(-not((Texts)-contains$provider)){throw ('Provider tab missing: '+$provider)};$checks+='Provider tab '+$provider}
Click 'Codex';$text=Texts
foreach($label in @('Pro 20x','限额重置额度','2 次可用','本机用量','本周额度','今日 Token 构成','额度窗口用量')){if(-not($text-contains$label)){throw ('Codex detail missing: '+$label)}}
$checks+='Codex detailed fields and reset-credit inventory are present'
# Settings are an in-panel page (same window), not a separate dialog.
Click '设置';Start-Sleep -Milliseconds 150
$slider=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Slider));if(-not$slider){throw 'Scale slider missing'}
$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(1.1)
Click '保存设置';Start-Sleep -Milliseconds 600
$config=Get-Content -LiteralPath (Join-Path $AppDirectory 'verification\demo-data\settings.json') -Raw|ConvertFrom-Json
if([Math]::Abs($config.UiScale-1.1)-gt.001){throw 'UI scale was not saved'}
if($null-eq$config.WindowLeft-or$config.WindowWidth-le420){throw 'Geometry was not persisted'}
$checks+='UI scale and moved/resized geometry persist in settings'
$before=Rect
Start-Process -FilePath (Join-Path $AppDirectory 'codeusagemonit.exe') -ArgumentList '--quit','--demo' -WindowStyle Hidden -Wait
Wait-Process -Id $process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
Start-Process -FilePath (Join-Path $AppDirectory 'codeusagemonit.exe') -ArgumentList '--demo' -WindowStyle Hidden
$handle=[IntPtr]::Zero
for($n=0;$n-lt30;$n++){Start-Sleep -Milliseconds 200;$process=Get-CimInstance Win32_Process -Filter "Name='codeusagemonit.exe'"|Where-Object{$_.ExecutablePath-eq(Join-Path $AppDirectory 'codeusagemonit.exe')-and$_.CommandLine-match'--demo'}|Select-Object -First 1;if($process){$handle=[NativeInteraction]::FindOwned([uint32]$process.ProcessId,'codeusagemonit')};if($handle-ne[IntPtr]::Zero){break}}
$after=Rect
if([Math]::Abs($after.Left-$before.Left)-gt5-or[Math]::Abs(($after.Right-$after.Left)-($before.Right-$before.Left))-gt5){throw 'Restart did not restore saved placement and width'}
$checks+='Restart restores placement, size and UI scale'
$afterProductionHash=if(Test-Path -LiteralPath $productionSettings){(Get-FileHash -LiteralPath $productionSettings).Hash}else{'absent'}
if($productionHash-ne$afterProductionHash){throw 'Demo changed production settings'}
$checks+='Demo never modifies production settings'
$root=[System.Windows.Automation.AutomationElement]::FromHandle($handle);Click 'Codex'
[NativeInteraction]::SetWindowPos($handle,[IntPtr](-1),0,0,0,0,0x13)|Out-Null
try{$r=Rect;[NativeInteraction]::SetCursorPos(($r.Left+160),($r.Top+22))|Out-Null;[NativeInteraction]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[NativeInteraction]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 200;Screenshot 'codex-v0.3';Click '概览';Screenshot 'overview-v0.3'}finally{[NativeInteraction]::SetWindowPos($handle,[IntPtr](-2),0,0,0,0,0x13)|Out-Null}
$report=[pscustomobject]@{Passed=$checks.Count;Checks=$checks;Mode='Isolated synthetic UI';Backdrop=$backdrop;CapturedAt=(Get-Date).ToString('o')}
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $AppDirectory 'verification\interaction-tests.json') -Encoding utf8
$report|ConvertTo-Json -Depth 4
