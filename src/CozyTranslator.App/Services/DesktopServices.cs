using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CozyTranslator.Desktop.Services;
public sealed class DesktopServices : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd,int id);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointInt point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int command);
    public static void RestoreWithoutActivation(Window window)=>ShowWindow(new WindowInteropHelper(window).Handle,4);
    [StructLayout(LayoutKind.Sequential)] private struct PointInt {public int X;public int Y;}
    private readonly HwndSource source=new(new HwndSourceParameters("CozyTranslator.Hotkey"){ParentWindow=new IntPtr(-3),WindowStyle=0});
    private int registeredId;
    private string chord="";
    public event Action? Activated;
    public event Action? ClipboardChanged;
    public DesktopServices()
    {
        source.AddHook(Hook);
        if(!AddClipboardFormatListener(source.Handle))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法监听剪贴板。");
    }
    private IntPtr Hook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled){if(msg==0x312){handled=true;Activated?.Invoke();}else if(msg==0x31d){handled=true;ClipboardChanged?.Invoke();}return IntPtr.Zero;}
    public static bool IsClipboardFromThisProcess(){var owner=GetClipboardOwner();GetWindowThreadProcessId(owner,out var processId);return owner!=IntPtr.Zero&&processId==Environment.ProcessId;}
    public bool SetHotkey(string value,out string error)
    {
        error="";var parts=value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        uint modifiers=0;Key key=Key.None;
        foreach(var part in parts)
        {
            switch(part.ToUpperInvariant()){case "CTRL":modifiers|=2;break;case "ALT":modifiers|=1;break;case "SHIFT":modifiers|=4;break;case "WIN":modifiers|=8;break;
            default: if(key!=Key.None || !Enum.TryParse(part.Length==1&&char.IsDigit(part[0])?"D"+part:part,true,out key)){error="快捷键示例：Ctrl+Alt+T";return false;}break;}
        }
        if(modifiers==0||key==Key.None||!Enum.IsDefined(key)||key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin){error="请使用修饰键加字母、数字或功能键。";return false;}
        var canonical=$"{modifiers}:{KeyInterop.VirtualKeyFromKey(key)}";
        if(canonical==chord)return true;
        var next=registeredId+1;if(!RegisterHotKey(source.Handle,next,modifiers|0x4000,(uint)KeyInterop.VirtualKeyFromKey(key))){error="快捷键已被其他应用占用，请更换组合。";return false;}
        if(registeredId!=0)UnregisterHotKey(source.Handle,registeredId);registeredId=next;chord=canonical;return true;
    }
    public static void Clamp(Window window,bool nearCursor=false)
    {
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;
        var scale=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        GetCursorPos(out var cursor);
        var screen=nearCursor?System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X,cursor.Y)):System.Windows.Forms.Screen.FromHandle(hwnd);
        var area=screen.WorkingArea;
        var maxWidth=Math.Max(window.MinWidth,area.Width/scale.DpiScaleX);var maxHeight=Math.Max(window.MinHeight,area.Height/scale.DpiScaleY);
        window.Width=Math.Min(window.Width,maxWidth);window.Height=Math.Min(window.Height,maxHeight);
        int w=(int)Math.Ceiling(window.Width*scale.DpiScaleX),h=(int)Math.Ceiling(window.Height*scale.DpiScaleY);
        int x=nearCursor?cursor.X+20:(int)(window.Left*scale.DpiScaleX),y=nearCursor?cursor.Y+20:(int)(window.Top*scale.DpiScaleY);
        x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-w));y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-h));
        SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,0x0015);
    }
    public void Dispose(){RemoveClipboardFormatListener(source.Handle);if(registeredId!=0)UnregisterHotKey(source.Handle,registeredId);source.RemoveHook(Hook);source.Dispose();}
}

public sealed class SpeechService : IDisposable
{
    private dynamic? voice;
    public string Speak(string word)
    {
        try
        {
            if(voice is null)
            {
                var type=Type.GetTypeFromProgID("SAPI.SpVoice");if(type is null)return "Windows 语音服务不可用。";
                voice=Activator.CreateInstance(type);dynamic voices=voice!.GetVoices();bool found=false;
                for(int i=0;i<voices.Count;i++)
                {
                    dynamic token=voices.Item(i);string language=token.GetAttribute("Language");
                    if(language.Split(';').Any(v=>int.TryParse(v,System.Globalization.NumberStyles.HexNumber,null,out var id)&&(id&0x3ff)==9)){voice.Voice=token;found=true;break;}
                    Marshal.ReleaseComObject(token);
                }
                Marshal.ReleaseComObject(voices);
                if(!found){Dispose();return "请在 Windows 语言设置中安装英语语音。";}
            }
            voice!.Speak(word,3);return "正在使用 Windows 英文语音朗读";
        }
        catch(COMException){return "朗读失败，请检查 Windows 语音和音频输出。";}
        catch(UnauthorizedAccessException){return "Windows 拒绝了语音访问，请检查系统语音权限。";}
    }
    public void Dispose(){if(voice is not null){try{voice.Speak("",3);}catch(COMException){}catch(UnauthorizedAccessException){}Marshal.ReleaseComObject(voice);voice=null;}}
}
