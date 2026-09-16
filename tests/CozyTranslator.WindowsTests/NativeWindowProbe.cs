using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

internal static class NativeWindowProbe
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd,uint message,nint wParam,nint lParam);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    internal static async Task Drag(FrameworkElement element,Point local,double dx,double dy)
    {
        var start=element.PointToScreen(local);
        var handle=new WindowInteropHelper(Window.GetWindow(element)).Handle;
        GetCursorPos(out var original);
        await Task.Run(()=>
        {
            try
            {
                var point=new NativePoint{X=(int)start.X,Y=(int)start.Y};
                if(GetAncestor(WindowFromPoint(point),2)!=handle)throw new Exception("Pointer target is not the test window");
                SetCursorPos(point.X,point.Y);Thread.Sleep(120);mouse_event(2,0,0,0,0);Thread.Sleep(80);
                SetCursorPos((int)(start.X+dx),(int)(start.Y+dy));Thread.Sleep(120);mouse_event(4,0,0,0,0);
            }
            finally{mouse_event(4,0,0,0,0);SetCursorPos(original.X,original.Y);}
        });
        await Task.Delay(150);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public int X,Y;}
    internal static bool Reachable(Window window,double x,double y)
    {
        var p=window.PointToScreen(new Point(x,y));
        return GetAncestor(WindowFromPoint(new NativePoint{X=(int)p.X,Y=(int)p.Y}),2)==new WindowInteropHelper(window).Handle;
    }
    internal static bool HasTaskbarStyle(Window window)
    {
        var style=GetWindowLong(new WindowInteropHelper(window).Handle,-20);
        return (style&0x40000)!=0&&(style&0x80)==0;
    }
    internal static bool HasNativeFrame(Window window)=>
        (GetWindowLong(new WindowInteropHelper(window).Handle,-16)&0xc40000)==0xc40000;
    internal static int Hit(Window window,double x,double y)
    {
        var p=window.PointToScreen(new Point(x,y));
        int packed=((int)p.Y<<16)|((int)p.X&0xffff);
        return (int)SendMessage(new WindowInteropHelper(window).Handle,0x84,0,(nint)packed);
    }
}
