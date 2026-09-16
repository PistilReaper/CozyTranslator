using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CozyTranslator.Core;
using CozyTranslator.Desktop.Services;
using Forms=System.Windows.Forms;

namespace CozyTranslator.Desktop;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private EventWaitHandle? wake;
    private RegisteredWaitHandle? wakeRegistration;
    private readonly SettingsStore store=new();
    private AppSettings settings=new();
    private ThemeService? theme;
    private DesktopServices? desktop;
    private ClipboardTranslation? clipboardTranslation;
    private SelectionTranslation? selectionTranslation;
    private readonly List<Forms.ToolStripMenuItem> triggerItems=[];
    private readonly SpeechService speech=new();
    private readonly HttpClient http=new(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=Timeout.InfiniteTimeSpan};
    private QuerySession? session;
    private MainViewModel? vm;
    private MainWindow? panel;
    private BubbleWindow? bubble;
    private Forms.NotifyIcon? tray;
    private uint ownClipboard;
    private bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex=new Mutex(true,"Local\\CozyTranslator.App",out var created);
        if(!created){try{using var signal=EventWaitHandle.OpenExisting("Local\\CozyTranslator.Wake");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return;}
        wake=new(false,EventResetMode.AutoReset,"Local\\CozyTranslator.Wake");
        wakeRegistration=ThreadPool.RegisterWaitForSingleObject(wake,(_,_)=>Dispatcher.BeginInvoke(()=>OpenPanel()),null,Timeout.Infinite,false);
        try{settings=store.Load();}catch(Exception){System.Windows.MessageBox.Show("设置文件无法读取。请备份并检查本地应用数据目录中的 CozyTranslator/settings.json。","CozyTranslator",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown();return;}
        theme=new(settings.Theme);TypographyService.Apply(settings.FontSize);desktop=new();desktop.Activated+=()=>_ = ReadClipboardAndTranslate();
        session=new(new ProviderClient(http));vm=new(session,()=>settings,SavePreferences);
        panel=new(vm);MainWindow=panel;
        panel.Width=Math.Max(settings.PanelWidth,panel.MinWidth);panel.Height=Math.Max(settings.PanelHeight,panel.MinHeight);
        clipboardTranslation=new(desktop,vm,()=>settings.Trigger==TranslationTrigger.Clipboard,()=>panel.DialogOpen);
        selectionTranslation=new(text=>_ = TranslateSelection(text),message=>vm.Notify(message)){Enabled=settings.Trigger==TranslationTrigger.RightClick};
        panel.SettingsRequested+=OpenSettings;panel.CollapseRequested+=Collapse;panel.HideToTrayRequested+=HideToTray;panel.CopyRequested+=CopyResult;panel.SpeakRequested+=()=>vm.Notify(speech.Speak(vm.Word));
        panel.Closing+=(_,args)=>{if(!exiting){args.Cancel=true;HideToTray();}};
        bubble=new();bubble.Invoked+=()=>_ = ReadClipboardAndTranslate();bubble.Moved+=()=>SavePreferences(settings with {BubbleLeft=bubble.Left,BubbleTop=bubble.Top});
        var area=SystemParameters.WorkArea;
        bubble.Left=double.IsFinite(settings.BubbleLeft)?settings.BubbleLeft:area.Right-95;bubble.Top=double.IsFinite(settings.BubbleTop)?settings.BubbleTop:area.Top+area.Height*0.55;
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开窗口",null,(_,_)=>Dispatcher.Invoke(OpenPanel));menu.Items.Add("翻译剪贴板",null,(_,_)=>Dispatcher.InvokeAsync(()=>ReadClipboardAndTranslate()));menu.Items.Add("显示悬浮球",null,(_,_)=>Dispatcher.Invoke(Collapse));menu.Items.Add("隐藏到托盘",null,(_,_)=>Dispatcher.Invoke(HideToTray));menu.Items.Add("设置",null,(_,_)=>Dispatcher.Invoke(OpenSettings));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Quit));
        var triggers=new Forms.ToolStripMenuItem("翻译触发");
        foreach(var mode in Enum.GetValues<TranslationTrigger>())
        {
            var item=new Forms.ToolStripMenuItem(mode switch {TranslationTrigger.Clipboard=>"模式 1 · 复制即翻译",TranslationTrigger.RightClick=>"模式 2 · 右键模式",_=>"仅手动"}){Checked=settings.Trigger==mode};
            item.Click+=(_,_)=>Dispatcher.Invoke(()=>SavePreferences(settings with {Trigger=mode}));
            triggerItems.Add(item);triggers.DropDownItems.Add(item);
        }
        menu.Items.Insert(2,triggers);
        using var iconStream=GetResourceStream(new Uri("pack://application:,,,/CozyTranslator;component/Assets/cozy.ico")).Stream;
        tray=new Forms.NotifyIcon {Icon=new System.Drawing.Icon(iconStream),Text="CozyTranslator v"+VersionInfo.Current,ContextMenuStrip=menu,Visible=true};
        tray.MouseClick+=(_,args)=>{if(args.Button==Forms.MouseButtons.Left)Dispatcher.Invoke(OpenPanel);};
        OpenPanel();
        if(!desktop.SetHotkey(settings.Hotkey,out var error))vm.Notify(error);
    }
    private void SavePreferences(AppSettings next)
    {
        try{store.Save(next);settings=next;UpdateTrigger();}catch(Exception){vm?.Notify("设置未能保存，请检查本地目录的写入权限。");}
    }
    private void UpdateTrigger()
    {
        if(selectionTranslation is not null)selectionTranslation.Enabled=settings.Trigger==TranslationTrigger.RightClick;
        for(int i=0;i<triggerItems.Count;i++)triggerItems[i].Checked=(int)settings.Trigger==i;
    }
    private void OpenPanel()=>ShowPanel(true);
    private void ShowPanel(bool activate)
    {
        if(panel is null||bubble is null)return;
        if(!panel.IsVisible)
        {
            bool hasPosition=double.IsFinite(settings.PanelLeft)&&double.IsFinite(settings.PanelTop);
            if(hasPosition){panel.Left=settings.PanelLeft;panel.Top=settings.PanelTop;}
            panel.ShowActivated=activate;panel.Show();DesktopServices.Clamp(panel,!hasPosition);
        }
        if(panel.WindowState==WindowState.Minimized){if(activate)panel.WindowState=WindowState.Normal;else DesktopServices.RestoreWithoutActivation(panel);}
        bubble.Hide();if(activate)panel.Activate();
    }
    private void SavePanelBounds()
    {
        if(panel is null||!panel.IsVisible)return;
        var bounds=panel.WindowState==WindowState.Normal?new Rect(panel.Left,panel.Top,panel.ActualWidth,panel.ActualHeight):panel.RestoreBounds;
        if(!bounds.IsEmpty)SavePreferences(settings with {PanelLeft=bounds.Left,PanelTop=bounds.Top,PanelWidth=bounds.Width,PanelHeight=bounds.Height});
    }
    private void HideToTray()
    {
        if(panel is null||bubble is null||panel.DialogOpen||exiting)return;
        SavePanelBounds();panel.Hide();bubble.Hide();
    }
    private void Collapse()
    {
        if(panel is null||bubble is null||panel.DialogOpen||exiting)return;
        SavePanelBounds();
        panel.Hide();bubble.Show();DesktopServices.Clamp(bubble);
    }
    private async Task TranslateSelection(string text)
    {
        if(vm is null||panel is null||panel.DialogOpen)return;
        vm.Source=text;ShowPanel(false);
        if(!vm.Configured){vm.Notify("请先配置模型");return;}
        await vm.TranslateAsync();
    }
    private async Task ReadClipboardAndTranslate()
    {
        if(vm is null)return;OpenPanel();
        try
        {
            if(ownClipboard!=0&&DesktopServices.GetClipboardSequenceNumber()==ownClipboard)return;
            if(!System.Windows.Clipboard.ContainsText()){vm.Source="";vm.Notify("剪贴板中没有文字，可以直接在上方输入。");return;}
            var text=System.Windows.Clipboard.GetText();
            if(text==vm.Source&&vm.HasQuery)return;
            vm.Source=text;
            if(string.IsNullOrWhiteSpace(text)){vm.Notify("剪贴板中没有文字，可以直接在上方输入。");return;}
            if(!vm.Configured){OpenSettings();return;}
            await vm.TranslateAsync();
        }
        catch(COMException){vm.Notify("剪贴板正被其他应用占用，请重新点击悬浮球。");}
    }
    private void CopyResult()
    {
        if(vm is null||!vm.CanCopy)return;
        try{System.Windows.Clipboard.SetText(vm.CopyText);ownClipboard=DesktopServices.GetClipboardSequenceNumber();vm.Notify("已复制");}
        catch(COMException){vm.Notify("剪贴板暂时不可写，请重试。");}
    }
    private void OpenSettings()
    {
        if(panel is null||vm is null||desktop is null||panel.DialogOpen)return;
        OpenPanel();panel.DialogOpen=true;if(selectionTranslation is not null)selectionTranslation.Suspended=true;
        try
        {
            var window=new SettingsWindow(settings,next=>
            {
                if(!desktop.SetHotkey(next.Hotkey,out var error))return error;
                try{store.Save(next);settings=next;vm.ConfigurationChanged();UpdateTrigger();return null;}
                catch(Exception){desktop.SetHotkey(settings.Hotkey,out _);return "设置保存失败，请检查本地目录的写入权限。";}
            },http,(preference,size)=>{theme?.SetPreference(preference);TypographyService.Apply(size);}){Owner=panel};
            window.ShowDialog();
        }
        finally{theme?.SetPreference(settings.Theme);TypographyService.Apply(settings.FontSize);panel.DialogOpen=false;if(selectionTranslation is not null)selectionTranslation.Suspended=false;panel.Activate();}
    }
    private void Quit(){SavePanelBounds();exiting=true;Shutdown();}
    protected override void OnExit(ExitEventArgs e)
    {
        exiting=true;selectionTranslation?.Dispose();clipboardTranslation?.Dispose();session?.Dispose();speech.Dispose();theme?.Dispose();desktop?.Dispose();http.Dispose();
        if(tray is not null){tray.Visible=false;tray.Icon?.Dispose();tray.ContextMenuStrip?.Dispose();tray.Dispose();}
        wakeRegistration?.Unregister(null);wake?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
}
