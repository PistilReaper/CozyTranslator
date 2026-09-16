using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CozyTranslator.Core;
using CozyTranslator.Desktop;
using CozyTranslator.Desktop.Services;

internal static class SelectionChecks
{
    public static int Host(string path)
    {
        var app = new Application();
        var editor = new TextBox { Text = "robust research results improve translation.", FontSize = 22, Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap };
        var window = new Window { Title = "CozyTranslator Selection Test", Content = editor, Width = 740, Height = 240, Left = 100, Top = 100, Topmost = true };
        int rightClicks = 0;
        void Write()
        {
            var first = editor.GetRectFromCharacterIndex(0); var word = editor.GetRectFromCharacterIndex(6);
            var last = editor.GetRectFromCharacterIndex(editor.Text.Length);
            var start = editor.PointToScreen(new Point(first.Left + 1, first.Top + first.Height / 2));
            var wordEnd = editor.PointToScreen(new Point(word.Left, word.Top + word.Height / 2));
            var end = editor.PointToScreen(new Point(last.Left, last.Top + last.Height / 2));
            var value = new { X = start.X, Y = start.Y, WordX = wordEnd.X, EndX = end.X, RightClicks = rightClicks, Selection = editor.SelectedText };
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
        }
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() => { editor.Focus(); Write(); }, DispatcherPriority.ApplicationIdle);
        editor.SelectionChanged += (_, _) => { if (window.IsLoaded) Write(); };
        editor.PreviewMouseRightButtonDown += (_, _) => { rightClicks++; Write(); };
        app.Run(window); return 0;
    }

    public static async Task Run(Action<string, Action> check, Action<bool, string> assert, string output)
    {
        var file = Path.Combine(output, "selection-host.json");
        if (File.Exists(file)) File.Delete(file);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--selection-host"); start.ArgumentList.Add(file);
        using var host = Process.Start(start)!;
        var provider = new FixtureProvider(); using var session = new QuerySession(provider);
        var settings = new AppSettings { Provider = new(ApiKey: "test-key-not-real", Model: "test-model") };
        var vm = new MainViewModel(session, () => settings, _ => { });
        var resultWindow = new MainWindow(vm) { ShowActivated = false, Left = 900, Top = 100 };
        string notice = ""; bool heldAtRead = false;
        using var service = new SelectionTranslation(text =>
        {
            heldAtRead = (GetAsyncKeyState(1) & 0x8000) != 0; vm.Source = text;
            if (!resultWindow.IsVisible) resultWindow.Show();
            if (resultWindow.WindowState == WindowState.Minimized) DesktopServices.RestoreWithoutActivation(resultWindow);
            _ = vm.TranslateAsync();
        }, text => notice = text) { Enabled = true };
        GetCursorPos(out var cursor);
        var clipboard = Clipboard.GetDataObject();
        try
        {
            for (int i = 0; i < 50 && !File.Exists(file); i++) await Task.Delay(100);
            if (!File.Exists(file)) throw new Exception("Selection test process did not open");
            await Task.Delay(250);
            using var ready = JsonDocument.Parse(File.ReadAllText(file)); var root = ready.RootElement;
            int x = (int)root.GetProperty("X").GetDouble(), y = (int)root.GetProperty("Y").GetDouble();
            int wordX = (int)root.GetProperty("WordX").GetDouble(), endX = (int)root.GetProperty("EndX").GetDouble();
            Clipboard.SetText("Cozy clipboard preservation fixture"); await Task.Delay(150);
            var sequence = DesktopServices.GetClipboardSequenceNumber();
            await Gesture(x, y, wordX, endX + 20);
            check("Held-left plus right-click translates an external selected word", () =>
                assert(vm.Source == "robust" && vm.HasDictionary && provider.Calls == 1 && heldAtRead, "Word selection did not reach dictionary: " + vm.Source + " / " + notice));
            check("Selection gesture preserves clipboard and source focus", () =>
                assert(Clipboard.GetText() == "Cozy clipboard preservation fixture" && DesktopServices.GetClipboardSequenceNumber() == sequence && GetForegroundWindow() == host.MainWindowHandle, "Clipboard or focus changed"));
            using (var state = JsonDocument.Parse(File.ReadAllText(file)))
                check("Only the trigger chord suppresses the source right-click menu", () => assert(state.RootElement.GetProperty("RightClicks").GetInt32() == 0, "Trigger reached the source context menu"));
            resultWindow.WindowState = WindowState.Minimized;
            await Gesture(x, y, endX, endX + 20);
            check("Sentence selection flows to translation rather than dictionary", () =>
                assert(vm.Source == "robust research results improve translation." && vm.HasTranslation && provider.Calls == 2, "Sentence mode or selection is wrong: " + vm.Source));
            check("Selection restores a minimized translator without stealing focus", () =>
                assert(resultWindow.WindowState == WindowState.Normal && GetForegroundWindow() == host.MainWindowHandle, "Restoring results changed source focus"));
            service.Enabled = false; await Gesture(x, y, wordX, endX + 20);
            check("Right-click gesture is inactive in other trigger modes", () => assert(provider.Calls == 2, "Disabled gesture fired"));
            Escape(); await Task.Delay(150);
            service.Enabled = true; service.Suspended = true; await Gesture(x, y, wordX, endX + 20);
            check("Settings suspension prevents selection translation", () => assert(provider.Calls == 2, "Suspended gesture fired"));
            Escape(); await Task.Delay(150); service.Suspended = false;
            SetCursorPos(wordX, y); mouse_event(8, 0, 0, 0, 0); mouse_event(16, 0, 0, 0, 0); await Task.Delay(250);
            using (var state = JsonDocument.Parse(File.ReadAllText(file)))
                check("Ordinary right-click still reaches the source application", () => assert(provider.Calls == 2 && state.RootElement.GetProperty("RightClicks").GetInt32() >= 3, "Normal right-click was intercepted"));
            Escape();
        }
        finally
        {
            mouse_event(4 | 16, 0, 0, 0, 0); SetCursorPos(cursor.X, cursor.Y);
            resultWindow.Close();
            if (!host.HasExited) { host.Kill(); await host.WaitForExitAsync(); }
            if (clipboard is not null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear();
        }
    }
    private static async Task Gesture(int x, int y, int end, int clear)
    {
        await Task.Run(() =>
        {
            // Start a fresh selection, rather than starting WPF drag-and-drop on the previous selection.
            SetCursorPos(clear, y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); Thread.Sleep(600);
            SetCursorPos(x, y); Thread.Sleep(100); mouse_event(2, 0, 0, 0, 0); Thread.Sleep(100);
            SetCursorPos(end, y); Thread.Sleep(150); mouse_event(8, 0, 0, 0, 0); Thread.Sleep(70);
            mouse_event(16, 0, 0, 0, 0); Thread.Sleep(350); mouse_event(4, 0, 0, 0, 0);
        });
        await Task.Delay(400);
    }
    private static void Escape() { keybd_event(27, 0, 0, 0); keybd_event(27, 0, 2, 0); }
    [StructLayout(LayoutKind.Sequential)] private struct PointInt { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointInt point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
