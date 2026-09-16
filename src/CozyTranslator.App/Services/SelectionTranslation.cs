using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;

namespace CozyTranslator.Desktop.Services;

/// <summary>Left held + right click reads the selection without touching the clipboard or input state.</summary>
public sealed class SelectionTranslation : IDisposable
{
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private readonly Action<string> selected, notice;
    private readonly Thread thread;
    private Dispatcher? hookDispatcher;
    private readonly HookProc callback;
    private nint hook, leftWindow;
    private bool leftHeld, consumeRightUp;
    private volatile bool enabled, suspended, disposed;
    private int reading;
    public bool Enabled { get => enabled; set => enabled = value; }
    public bool Suspended { get => suspended; set => suspended = value; }

    public SelectionTranslation(Action<string> selected, Action<string> notice)
    {
        this.selected = selected; this.notice = notice; callback = Mouse;
        using var started = new ManualResetEventSlim();
        Exception? failure = null;
        thread = new Thread(() =>
        {
            try
            {
                hookDispatcher = Dispatcher.CurrentDispatcher;
                hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { failure = ex; }
            finally { started.Set(); }
            if (failure is not null) return;
            try { Dispatcher.Run(); }
            finally { UnhookWindowsHookEx(hook); }
        }) { IsBackground = true, Name = "CozyTranslator.SelectionGesture" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); started.Wait();
        if (failure is not null) throw failure;
    }

    private nint Mouse(int code, nint message, nint data)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        var input = Marshal.PtrToStructure<MouseData>(data);
        int kind = (int)message;
        if (kind == 0x201) // left down
        {
            leftHeld = true;
            leftWindow = GetAncestor(WindowFromPoint(input.Point), 2);
        }
        else if (kind == 0x202) leftHeld = false;
        else if (kind == 0x205 && consumeRightUp)
        {
            consumeRightUp = false; return 1;
        }
        else if (kind == 0x204 && enabled && !suspended && !disposed && leftHeld && !consumeRightUp)
        {
            var target = GetAncestor(WindowFromPoint(input.Point), 2);
            GetWindowThreadProcessId(target, out var process);
            if (target != 0 && target == leftWindow && target == GetForegroundWindow() && process != Environment.ProcessId &&
                !ModifiersPressed() && Interlocked.CompareExchange(ref reading, 1, 0) == 0)
            {
                consumeRightUp = true;
                // Never make cross-process UI Automation calls inside the low-level hook.
                _ = ReadSelection(input.Point, target, process);
                return 1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private async Task ReadSelection(NativePoint point, nint window, uint process)
    {
        try
        {
            var text = await Task.Run(() => ReadText(point, process)).WaitAsync(TimeSpan.FromSeconds(2));
            if (disposed || !enabled || suspended || window != GetForegroundWindow()) return;
            await ui.InvokeAsync(() =>
            {
                if (disposed || !enabled || suspended) return;
                if (string.IsNullOrWhiteSpace(text)) notice("未读取到选中文字，请确认文档支持文本选择。");
                else selected(text);
            });
        }
        catch (Exception ex) when (ex is TimeoutException or ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            if (!disposed) await ui.InvokeAsync(() => notice("无法读取当前选区，请检查文档或应用权限。"));
        }
        finally { Interlocked.Exchange(ref reading, 0); }
    }

    private static string ReadText(NativePoint point, uint process)
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        // The pointer often lands on a word/span; its document ancestor owns TextPattern.
        for (int depth = 0; element is not null && depth < 16; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
        {
            if (element.Current.ProcessId != process) break;
            if (element.Current.IsPassword) return "";
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                return string.Join("\n", ((TextPattern)pattern).GetSelection().Select(range => range.GetText(-1)).Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        return "";
    }

    private static bool ModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    public void Dispose()
    {
        disposed = true; enabled = false;
        hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativePoint Point; public uint Mouse, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
