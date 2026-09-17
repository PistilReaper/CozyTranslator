using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using CozyTranslator.Desktop.Services;

// Optional real-browser regression: --browser-selection <chromium-browser.exe> <output-folder>
internal static class BrowserSelectionChecks
{
    private const string Sentence = "robust research results improve translation.";
    public static async Task Run(string browser, Action<string, Action> check, Action<bool, string> assert, string output)
    {
        string token = "CozySelection" + Guid.NewGuid().ToString("N");
        string page = Path.Combine(output, token + ".html");
        File.WriteAllText(page, """
            <!doctype html><meta charset="utf-8"><style>body{margin:40px;font:24px monospace}p{white-space:pre}</style>
            <p id="text">robust research results improve translation.</p>
            <script>
            const prefix = 'TOKEN'; let copies = 0, contexts = 0;
            function state() {
              const node = document.getElementById('text').firstChild;
              function point(offset) { const r=document.createRange();r.setStart(node,offset);r.collapse(true);const b=r.getBoundingClientRect();return [b.x,b.y+b.height/2]; }
              document.title = prefix + '|' + JSON.stringify({start:point(0),word:point(6),end:point(node.length),width:innerWidth,height:innerHeight,dpr:devicePixelRatio,selected:String(getSelection()),copies,contexts}) + '|';
            }
            addEventListener('load',state);addEventListener('resize',state);document.addEventListener('selectionchange',state);
            document.addEventListener('copy',()=>{copies++;state()});document.addEventListener('contextmenu',e=>{contexts++;e.preventDefault();state()});
            </script>
            """.Replace("TOKEN", token));
        var start = new ProcessStartInfo(browser) { UseShellExecute = false };
        start.ArgumentList.Add("--user-data-dir=" + Path.Combine(output, token + "-profile"));
        start.ArgumentList.Add("--no-first-run"); start.ArgumentList.Add("--no-default-browser-check");
        start.ArgumentList.Add("--window-position=100,100"); start.ArgumentList.Add("--window-size=1000,600");
        start.ArgumentList.Add("--app=" + new Uri(page).AbsoluteUri);
        using var host = Process.Start(start)!;
        string selected = "", notice = ""; int calls = 0;
        using var service = new SelectionTranslation(text => { selected = text; calls++; }, text => notice = text) { Enabled = true };
        var clipboard = Clipboard.GetDataObject(); GetCursorPos(out var oldCursor);
        nint window = 0;
        try
        {
            for (int i = 0; i < 150 && window == 0; i++)
            {
                EnumWindows((hwnd, _) => { if (Title(hwnd).StartsWith(token + "|")) window = hwnd; return true; }, 0);
                if (window == 0) await Task.Delay(100);
            }
            if (window == 0) throw new Exception("Browser fixture did not open");
            SetForegroundWindow(window); await Task.Delay(300);
            using var ready = State(window); var data = ready.RootElement;
            GetWindowRect(window, out var rect);
            double scale = data.GetProperty("dpr").GetDouble();
            double border = (rect.Right - rect.Left - data.GetProperty("width").GetDouble() * scale) / 2;
            double contentTop = rect.Bottom - border - data.GetProperty("height").GetDouble() * scale;
            (int X, int Y) Point(string name)
            {
                var p = data.GetProperty(name);
                return ((int)Math.Round(rect.Left + border + p[0].GetDouble() * scale), (int)Math.Round(contentTop + p[1].GetDouble() * scale));
            }
            var first = Point("start"); var word = Point("word"); var end = Point("end");
            Clipboard.SetText("Stale browser clipboard fixture"); await Task.Delay(100);
            await Gesture(first.X + 1, first.Y, word.X, end.X + 30);
            for (int i = 0; i < 30 && calls == 0 && notice.Length == 0; i++) await Task.Delay(100);
            check("Browser held-left plus right-click copies a word", () => assert(selected == "robust" && calls == 1, selected + " / " + notice + " / " + Title(window)));
            check("Browser source focus and clipboard match the selection", () => assert(GetForegroundWindow() == window && Clipboard.GetText() == "robust", "Focus or clipboard mismatch"));
            using (var state = State(window))
                check("Browser receives one copy and no context menu for the chord", () => assert(state.RootElement.GetProperty("copies").GetInt32() == 1 && state.RootElement.GetProperty("contexts").GetInt32() == 0, state.RootElement.ToString()));
            await Gesture(first.X + 1, first.Y, end.X + 8, end.X + 30);
            for (int i = 0; i < 30 && calls < 2; i++) await Task.Delay(100);
            check("Browser right-click copies the complete sentence", () => assert(selected == Sentence && calls == 2, selected + " / " + notice));
            SetCursorPos(end.X + 30, first.Y); mouse_event(8, 0, 0, 0, 0); mouse_event(16, 0, 0, 0, 0); await Task.Delay(200);
            using (var state = State(window))
                check("Browser ordinary right-click remains available", () => assert(state.RootElement.GetProperty("contexts").GetInt32() == 1 && calls == 2, "Ordinary right-click was intercepted"));
        }
        finally
        {
            mouse_event(4 | 16, 0, 0, 0, 0); SetCursorPos(oldCursor.X, oldCursor.Y);
            if (!host.HasExited) { host.Kill(true); await host.WaitForExitAsync(); }
            if (clipboard is not null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear();
        }
    }
    private static string Title(nint window) { var title = new StringBuilder(2048); GetWindowText(window, title, title.Capacity); return title.ToString(); }
    private static JsonDocument State(nint window) => JsonDocument.Parse(Title(window).Split('|')[1]);
    private static async Task Gesture(int x, int y, int end, int clear)
    {
        await Task.Run(() =>
        {
            SetCursorPos(clear, y); mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); Thread.Sleep(600);
            SetCursorPos(x, y); Thread.Sleep(100); mouse_event(2, 0, 0, 0, 0); Thread.Sleep(100);
            SetCursorPos(end, y); Thread.Sleep(200); mouse_event(8, 0, 0, 0, 0); Thread.Sleep(70);
            mouse_event(16, 0, 0, 0, 0); Thread.Sleep(400); mouse_event(4, 0, 0, 0, 0);
        });
        await Task.Delay(300);
    }
    private delegate bool EnumProc(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointInt { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointInt point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
}
