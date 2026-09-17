using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CozyTranslator.Core;
using CozyTranslator.Desktop;
using CozyTranslator.Desktop.Services;

// The application opts into WPF Fluent in App.xaml; this harness switches that same theme API for visual QA.
#pragma warning disable WPF0001

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.FirstOrDefault()=="--selection-host")return SelectionChecks.Host(args[1]);
        if(args.FirstOrDefault()=="--clipboard"){System.Windows.Clipboard.SetText(args[1]);return 0;}
        int failed=0,passed=0;
        bool selectionOnly=args.FirstOrDefault()=="--selection-only";
        bool browserSelection=args.FirstOrDefault()=="--browser-selection";
        string output=Path.GetFullPath((browserSelection?args.ElementAtOrDefault(2):selectionOnly?args.ElementAtOrDefault(1):args.FirstOrDefault())??"artifacts/qa");Directory.CreateDirectory(output);
        var app=new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/CozyTranslator;component/Theme.xaml")});
        typeof(System.Windows.Application).GetProperty("ThemeMode")!.SetValue(app,ThemeMode.System);
        void Check(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        app.Startup+=async(_,_)=>
        {
            try
            {
                if(selectionOnly){await SelectionChecks.Run(Check,Assert,output);return;}
                if(browserSelection){await BrowserSelectionChecks.Run(args[1],Check,Assert,output);return;}
                var settings=new AppSettings {Provider=new(ApiKey:"test-key-not-real",Model:"your-model")};
                Check("DPAPI roundtrip keeps key out of settings JSON",()=>
                {
                    var path=Path.Combine(output,"settings-test");var store=new SettingsStore(path);store.Save(settings);
                    Assert(!File.ReadAllText(Path.Combine(path,"settings.json")).Contains("test-key-not-real"),"Plaintext key in settings");
                    Assert(store.Load().Provider.ApiKey=="test-key-not-real","Decrypt mismatch");
                });
                Check("Trigger preference persists and old clipboard preference migrates",()=>
                {
                    var path=Path.Combine(output,"trigger-settings-test");var store=new SettingsStore(path);
                    store.Save(new AppSettings{Trigger=TranslationTrigger.RightClick});
                    Assert(store.Load().Trigger==TranslationTrigger.RightClick,"Right-click mode did not persist");
                    var file=Path.Combine(path,"settings.json");
                    var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
                    json.AsObject().Remove("Trigger");json["AutoTranslateClipboard"]=false;
                    File.WriteAllText(file,json.ToJsonString());
                    Assert(store.Load().Trigger==TranslationTrigger.Manual,"An old paused clipboard setting became automatic");
                });
                Check("Hotkey conflict does not replace existing registration",()=>
                {
                    using var first=new DesktopServices();using var second=new DesktopServices();
                    Assert(first.SetHotkey("Ctrl+Alt+Shift+F11",out var error),error);
                    Assert(!second.SetHotkey("Ctrl+Alt+Shift+F11",out _),"Conflict accepted");
                    Assert(first.SetHotkey("Ctrl+Alt+Shift+F12",out error),error);
                    Assert(second.SetHotkey("Ctrl+Alt+Shift+F11",out error),"Old chord was not released: "+error);
                });
                Check("Invalid hotkey is rejected without exception",()=>{using var service=new DesktopServices();Assert(!service.SetHotkey("Ctrl+9999",out _),"Invalid key accepted");});
                Check("Windows English speech can be initialized",()=>{using var speech=new SpeechService();Assert(speech.Speak("robust").StartsWith("正在"),"English SAPI voice unavailable");});
                var fixture=new FixtureProvider();using var session=new QuerySession(fixture);
                var vm=new MainViewModel(session,()=>settings,s=>settings=s);
                var window=new MainWindow(vm){Left=-10000,Top=-10000};window.Show();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check("Default text is larger and uses sans serif",()=>
                {
                    var editor=(TextBox)window.FindName("SourceEditor");
                    Assert(editor.FontSize>=18,"Source text remains too small");
                    Assert(!((FontFamily)app.Resources["ReadingFont"]).Source.Contains("Georgia"),"English output uses a serif font");
                });
                Check("Mode selector fits its measured content",()=>
                {
                    foreach(var radio in Descendants<RadioButton>(window))
                    {
                        Assert(radio.FontSize>=14,"Mode labels are too small");
                        var parent=VisualTreeHelper.GetParent(radio) as FrameworkElement;
                        Assert(parent is not null&&radio.ActualHeight>=radio.DesiredSize.Height-0.5,"Mode label is vertically clipped");
                    }
                });
                Check("Main window has a taskbar entry and icon",()=>Assert(window.ShowInTaskbar&&NativeWindowProbe.HasTaskbarStyle(window)&&window.Icon is not null,"Main window is hidden from the taskbar or has no icon"));
                Check("Native caption and sizing styles remain enabled",()=>Assert(NativeWindowProbe.HasNativeFrame(window),"Native move/resize styles were removed"));
                Check("Header blank space uses native caption hit testing",()=>Assert(NativeWindowProbe.Hit(window,210,38)==2,"Header does not return HTCAPTION"));
                Check("All eight resize directions use native hit testing",()=>
                {
                    double w=window.ActualWidth,h=window.ActualHeight;
                    foreach(var p in new[]{(2d,h/2,10),(w-2,h/2,11),(w/2,2d,12),(2d,2d,13),(w-2,2d,14),(w/2,h-2,15),(2d,h-2,16),(w-2,h-2,17)})
                        Assert(NativeWindowProbe.Hit(window,p.Item1,p.Item2)==p.Item3,$"Resize direction {p.Item3} is not reachable");
                });
                window.Left=SystemParameters.WorkArea.Left+40;window.Top=SystemParameters.WorkArea.Top+40;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Task.Delay(150);
                Check("Visible resize edges receive real pointer input",()=>Assert(NativeWindowProbe.Reachable(window,2,window.ActualHeight/2)&&NativeWindowProbe.Reachable(window,window.ActualWidth-2,window.ActualHeight/2),"Transparent resize edges pass through to another window"));
                window.Left=-10000;window.Top=-10000;
                int collapses=0;window.CollapseRequested+=()=>collapses++;
                window.Activate();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var focusWindow=new Window{Width=100,Height=100,Left=-10000,Top=-10000,ShowInTaskbar=false};focusWindow.Show();focusWindow.Activate();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check("Losing focus keeps the main window open",()=>Assert(collapses==0,"Window requested automatic collapse on deactivation"));focusWindow.Close();
                ThemeService.Apply(false);Capture(window,Path.Combine(output,"welcome-light.png"));
                Check("Source editor is keyboard-editable",()=>{var editor=(TextBox)window.FindName("SourceEditor");editor.Text="The best tools get out of the way.\nThey leave a little more room to think.";Assert(vm.Source==editor.Text,"Source binding not updated");});
                await vm.TranslateAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check("Translation binds to rendered view",()=>Assert(vm.HasTranslation&&vm.ResultText.Contains("空间"),"Missing translated output"));
                ThemeService.Apply(false);Capture(window,Path.Combine(output,"translation-light.png"));
                ThemeService.Apply(true);typeof(System.Windows.Application).GetProperty("ThemeMode")!.SetValue(app,ThemeMode.Dark);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,Path.Combine(output,"translation-dark.png"));
                await MarkdownChecks.Run(window,vm,Check,Assert,output,Capture);
                window.Height=680;vm.Source="robust";await vm.TranslateAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check("Word switches to dictionary card",()=>Assert(vm.HasDictionary&&!vm.ShowTone&&vm.CanSpeak,"Wrong dictionary state"));
                Check("Dictionary mode respects user window size",()=>Assert(window.Height==680,"Dictionary mode changed the user window height"));
                ThemeService.Apply(false);typeof(System.Windows.Application).GetProperty("ThemeMode")!.SetValue(app,ThemeMode.Light);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,Path.Combine(output,"dictionary-light.png"));
                ThemeService.Apply(true);typeof(System.Windows.Application).GetProperty("ThemeMode")!.SetValue(app,ThemeMode.Dark);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,Path.Combine(output,"dictionary-dark.png"));
                window.Height=610;vm.Source="";Check("Editing resets previous dictionary result",()=>Assert(!vm.HasResult,"Stale result visible"));
                Check("Leaving dictionary preserves manually resized height",()=>Assert(window.Height==610,"Leaving dictionary reset the window height"));
                vm.Source="Long text.";await vm.TranslateAsync();var count=fixture.Calls;vm.Style=4;await vm.CommitStyleAsync();await vm.CommitStyleAsync();
                Check("One request per changed committed style",()=>Assert(fixture.Calls==count+1,"Redundant request"));
                vm.Source="error text";await vm.TranslateAsync();ThemeService.Apply(false);Capture(window,Path.Combine(output,"error-light.png"));
                Check("Provider failure remains visible in UI",()=>Assert(vm.IsError&&vm.Status.Contains("测试错误"),"Error not shown"));
                vm.Source="Loading preview.";var loading=vm.TranslateAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(window,Path.Combine(output,"loading-light.png"));session.Stop();await loading;
                vm.Source="Partial preview.";var partial=vm.TranslateAsync();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);vm.Notify("已复制");session.Stop();await partial;Capture(window,Path.Combine(output,"stopped-light.png"));
                Check("Terminal status takes precedence over copy feedback",()=>Assert(vm.Status.Contains("未完成"),"Copy notice hides cancelled state"));
                vm.Source="unknownword";await vm.TranslateAsync();Capture(window,Path.Combine(output,"unknown-word.png"));
                Check("Unknown word is a readable empty dictionary",()=>Assert(vm.HasDictionary&&!vm.CanSpeak&&vm.WordHint.Length>0,"Invalid unknown word UI"));
                vm.Source="Long comparison.";await vm.TranslateAsync();Capture(window,Path.Combine(output,"long-text.png"));
                using var http=new System.Net.Http.HttpClient();
                var sw=new SettingsWindow(new(),_=>null,http){Left=-10000,Top=-10000};sw.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                typeof(System.Windows.Application).GetProperty("ThemeMode")!.SetValue(app,ThemeMode.Light);ThemeService.Apply(false);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(sw,Path.Combine(output,"settings-light.png"));sw.Close();
                Check("Window handles minimum size without overflow",()=>{window.Width=window.MinWidth;window.Height=window.MinHeight;window.UpdateLayout();var button=(Button)window.FindName("TranslateButton");var bottom=button.TransformToAncestor(window).Transform(new System.Windows.Point(button.ActualWidth,button.ActualHeight));Assert(bottom.Y<=window.ActualHeight-14,"Action button clipped at minimum size");Capture(window,Path.Combine(output,"minimum-size.png"));});
                TypographyService.Apply(24);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check("Largest font fits controls at minimum window size",()=>
                {
                    Assert(((TextBox)window.FindName("SourceEditor")).FontSize==24,"Font change not applied");
                    var button=(Button)window.FindName("TranslateButton");
                    Assert(button.TransformToAncestor(window).Transform(new System.Windows.Point(0,button.ActualHeight)).Y<=window.ActualHeight-14,"Large font clipped action button");
                    var strip=(Border)window.FindName("ModeStrip");
                    Assert(strip.ActualHeight>=strip.DesiredSize.Height-0.5,"Mode strip clipped at large font");
                    Capture(window,Path.Combine(output,"large-font.png"));
                });
                TypographyService.Apply(18);
                var savedAppearance=new AppSettings();
                var appearance=new SettingsWindow(new(),next=>{savedAppearance=next;return null;},http,(preference,size)=>{ThemeService.Apply(preference==ThemePreference.Dark);TypographyService.Apply(size);}){Owner=window};
                appearance.Loaded+=(_,_)=>appearance.Dispatcher.BeginInvoke(()=>
                {
                    ((ComboBox)appearance.FindName("ThemeBox")).SelectedIndex=2;
                    ((ComboBox)appearance.FindName("FontSizeBox")).SelectedIndex=3;
                    ((ComboBox)appearance.FindName("TriggerBox")).SelectedIndex=1;
                    Check("Settings previews theme and font immediately",()=>Assert(((TextBox)window.FindName("SourceEditor")).FontSize==22&&((SolidColorBrush)app.Resources["Paper"]).Color==Color.FromRgb(36,37,34),"Appearance preview did not propagate"));
                    ((Button)appearance.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                appearance.ShowDialog();
                Check("Appearance can be saved before configuring a model",()=>Assert(savedAppearance.Theme==ThemePreference.Dark&&savedAppearance.FontSize==22&&savedAppearance.Trigger==TranslationTrigger.RightClick,"Appearance settings were rejected without an API key"));
                TypographyService.Apply(18);ThemeService.Apply(false);
                var themePath=@"Software\CozyTranslator.Tests\"+Guid.NewGuid().ToString("N");
                using(var testKey=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(themePath))
                {
                    testKey.SetValue("AppsUseLightTheme",1);
                    using(var theme=new ThemeService(ThemePreference.System,themePath))
                    {
                        testKey.SetValue("AppsUseLightTheme",0);await Task.Delay(300);
                        Check("Registry change automatically applies dark theme",()=>Assert(((SolidColorBrush)app.Resources["Paper"]).Color==Color.FromRgb(36,37,34),"Dark registry update was missed"));
                        theme.SetPreference(ThemePreference.Light);
                        Check("Explicit light theme overrides system",()=>Assert(((SolidColorBrush)app.Resources["Paper"]).Color==Color.FromRgb(248,246,241),"Explicit theme was ignored"));
                        theme.SetPreference(ThemePreference.System);testKey.SetValue("AppsUseLightTheme",1);await Task.Delay(300);
                        Check("Second registry change automatically returns to light",()=>Assert(((SolidColorBrush)app.Resources["Paper"]).Color==Color.FromRgb(248,246,241),"Registry watcher was not rearmed"));
                    }
                }
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(themePath);
                var clipboardBackup=System.Windows.Clipboard.GetDataObject();
                try
                {
                    using var clipboardDesktop=new DesktopServices();var clipboardFixture=new FixtureProvider();using var clipboardSession=new QuerySession(clipboardFixture);
                    var clipboardVm=new MainViewModel(clipboardSession,()=>settings,_=>{});bool autoEnabled=true,dialogOpen=false;
                    using var clipboard=new ClipboardTranslation(clipboardDesktop,clipboardVm,()=>autoEnabled,()=>dialogOpen);
                    await CopyFromOtherProcess("A clipboard translation.");await Task.Delay(400);
                    Check("External copy automatically fills source and translates",()=>Assert(clipboardVm.Source=="A clipboard translation."&&clipboardVm.HasTranslation&&clipboardFixture.Calls==1,"Clipboard did not reach the translation pipeline"));
                    System.Windows.Clipboard.SetText("Text copied inside this app.");await Task.Delay(350);
                    Check("App-owned clipboard does not trigger a translation loop",()=>Assert(clipboardFixture.Calls==1,"App-owned clipboard was translated"));
                    await CopyFromOtherProcess("A clipboard translation.");await Task.Delay(350);
                    Check("Repeated external copy keeps the existing result",()=>Assert(clipboardFixture.Calls==1,"Duplicate clipboard triggered another request"));
                    autoEnabled=false;await CopyFromOtherProcess("Paused clipboard.");await Task.Delay(350);
                    Check("Clipboard pause prevents automatic translation",()=>Assert(clipboardFixture.Calls==1,"Paused clipboard was translated"));
                    autoEnabled=true;dialogOpen=true;await CopyFromOtherProcess("Settings clipboard.");await Task.Delay(350);
                    Check("Settings dialog suspends clipboard translation",()=>Assert(clipboardFixture.Calls==1,"Clipboard translated while editing API settings"));
                    dialogOpen=false;await CopyFromOtherProcess("robust");
                    for(int i=0;i<30&&!clipboardVm.HasDictionary;i++)await Task.Delay(100);
                    Check("Copied single word automatically opens dictionary result",()=>Assert(clipboardFixture.Calls==2&&clipboardVm.HasDictionary,$"Copied word did not produce a dictionary: source={clipboardVm.Source}, calls={clipboardFixture.Calls}"));
                }
                finally{if(clipboardBackup is not null)System.Windows.Clipboard.SetDataObject(clipboardBackup,true);else System.Windows.Clipboard.Clear();}
                await SelectionChecks.Run(Check,Assert,output);
                var bubble=new BubbleWindow{Left=-10000,Top=-10000};bubble.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Capture(bubble,Path.Combine(output,"bubble.png"));bubble.Close();window.Close();
            }
            catch(Exception ex){failed++;Console.WriteLine("FAIL Windows harness: "+ex);}
            finally{Console.WriteLine($"{passed}/{passed+failed} Windows checks passed");app.Shutdown();}
        };
        app.Run();return failed==0?0:1;
    }
    static void Capture(Window window,string path)
    {
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)(window.ActualWidth*1.5),(int)(window.ActualHeight*1.5),144,144,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var item in Descendants<T>(child))yield return item;
        }
    }
    static async Task CopyFromOtherProcess(string text)
    {
        var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--clipboard");start.ArgumentList.Add(text);
        using var process=System.Diagnostics.Process.Start(start)!;await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new Exception("Clipboard helper failed");
    }
}
sealed class FixtureProvider : CozyTranslator.Core.IQueryProvider
{
    public int Calls {get;private set;}
    public async IAsyncEnumerable<QueryUpdate> QueryAsync(QueryRequest request,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken cancellationToken=default)
    {
        Calls++;await Task.Yield();
        if(request.Text.StartsWith("Research"))
        {
            foreach(var part in MarkdownChecks.Sample.Chunk(24)){yield return new(Delta:new string(part));await Task.Delay(30,cancellationToken);}
            yield return new(Completed:true);yield break;
        }
        if(request.Text.StartsWith("Loading"))await Task.Delay(Timeout.Infinite,cancellationToken);
        if(request.Text.StartsWith("Partial")){yield return new(Delta:"目前已译出的一段内容…");await Task.Delay(Timeout.Infinite,cancellationToken);}
        if(request.Text.StartsWith("error"))throw new QueryException("测试错误：服务暂时不可用，请稍后重试。");
        if(request.Text=="unknownword"){yield return new(Entry:new("unknownword",false,[],[]),Completed:true);yield break;}
        if(request.Text.StartsWith("Long comparison")){yield return new(Delta:string.Join("\n\n",Enumerable.Repeat("翻译应保留原文的逻辑与细节，在准确与自然之间找到合适的表达。长文本在译文区域内滚动，操作始终保持可见。",40)),Completed:true);yield break;}
        if(QueryPlanner.Resolve(request.Text,request.Mode)==QueryMode.Dictionary)
            yield return new(Entry:new("robust",true,[new("UK","rəʊˈbʌst"),new("US","roʊˈbʌst")],[new("adj.",["强健的；结实的","稳健的；健壮的"])]),Completed:true);
        else {yield return new(Delta:"好的工具，会悄然退到一旁。\n让思考多一点空间。");yield return new(Completed:true);}
    }
}
