using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace CozyTranslator.Desktop.Services;

/// <summary>Left held + right click copies the selection and reads that copy from the source application.</summary>
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
    private int reading, revision;
    public bool Enabled { get => enabled; set { if (enabled != value) { enabled = value; Interlocked.Increment(ref revision); } } }
    public bool Suspended { get => suspended; set { if (suspended != value) { suspended = value; Interlocked.Increment(ref revision); } } }

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
                // Queue after the hook returns. Clipboard access must run on the UI's STA thread.
                int requestRevision = Volatile.Read(ref revision);
                _ = ui.InvokeAsync(() => CopySelection(target, process, requestRevision)).Task.Unwrap();
                return 1;
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private bool IsCurrent(nint window, int requestRevision) =>
        !disposed && enabled && !suspended && requestRevision == Volatile.Read(ref revision) && window == GetForegroundWindow();

    private async Task CopySelection(nint window, uint process, int requestRevision)
    {
        try
        {
            if (!IsCurrent(window, requestRevision) || ModifiersPressed()) return;
            uint beforeCopy = DesktopServices.GetClipboardSequenceNumber();
            Input[] keys = [Key(0x11), Key(0x43), Key(0x43, true), Key(0x11, true)];
            uint sent = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
            if (sent != keys.Length)
            {
                if (sent > 0) SendInput(2, [Key(0x43, true), Key(0x11, true)], Marshal.SizeOf<Input>());
                notice("未能发送复制命令，请检查原应用权限。"); return;
            }
            long deadline = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < deadline)
            {
                // Give the source time to process Ctrl+C and publish its clipboard data.
                await Task.Delay(25);
                if (!IsCurrent(window, requestRevision)) return;
                uint copiedSequence = DesktopServices.GetClipboardSequenceNumber();
                if (copiedSequence == beforeCopy) continue;
                GetWindowThreadProcessId(GetClipboardOwner(), out uint owner);
                if (owner != process) continue;
                try
                {
                    if (!Clipboard.ContainsText()) continue;
                    string text = Clipboard.GetText();
                    if (copiedSequence != DesktopServices.GetClipboardSequenceNumber()) continue;
                    if (!string.IsNullOrWhiteSpace(text)) { selected(text); return; }
                }
                catch (COMException) { /* A source may still be writing or rendering clipboard formats. */ }
            }
            if (IsCurrent(window, requestRevision)) notice("未复制到选中文字，请确认原文可以复制。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            if (IsCurrent(window, requestRevision)) notice("无法读取本次复制，请检查原应用权限。");
        }
        finally { Interlocked.Exchange(ref reading, 0); }
    }

    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 2u : 0 } } };

    private static bool ModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    public void Dispose()
    {
        disposed = true; enabled = false;
        hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativePoint Point; public uint Mouse, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
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
