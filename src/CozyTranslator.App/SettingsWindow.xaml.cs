using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CozyTranslator.Core;
using CozyTranslator.Desktop.Services;

namespace CozyTranslator.Desktop;
public partial class SettingsWindow : Window
{
    private readonly AppSettings initial;
    private readonly Func<AppSettings,string?> apply;
    private readonly HttpClient http;
    private readonly Action<ThemePreference,int>? preview;
    private QuerySession? testSession;
    private bool ready;
    public SettingsWindow(AppSettings initial,Func<AppSettings,string?> apply,HttpClient http,Action<ThemePreference,int>? preview=null)
    {
        this.initial=initial;this.apply=apply;this.http=http;this.preview=preview;InitializeComponent();WindowDragging.Attach(this);
        ProtocolBox.SelectedIndex=(int)initial.Provider.Kind;BaseUrlBox.Text=initial.Provider.BaseUrl;ModelBox.Text=initial.Provider.Model;KeyBox.Password=initial.Provider.ApiKey;
        PromptBox.Text=initial.SystemPrompt;HotkeyBox.Text=initial.Hotkey;
        ThemeBox.SelectedIndex=(int)initial.Theme;FontSizeBox.SelectedIndex=(initial.FontSize-16)/2;TriggerBox.SelectedIndex=(int)initial.Trigger;
        VersionText.Text="CozyTranslator v"+VersionInfo.Current;ready=true;
        Closed+=(_,_)=>testSession?.Dispose();
        Loaded+=(_,_)=>DesktopServices.Clamp(this);
    }
    private void ProtocolChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready)return;
        BaseUrlBox.Text=ProtocolBox.SelectedIndex switch{1=>"https://api.anthropic.com/v1",2=>"https://generativelanguage.googleapis.com/v1beta",_=>"https://api.deepseek.com"};
        ModelBox.Text="";KeyBox.Clear();StatusText.Text="请填写此服务的模型名称与密钥。";
    }
    private void AppearanceChanged(object sender,SelectionChangedEventArgs e){if(ready)preview?.Invoke((ThemePreference)ThemeBox.SelectedIndex,16+2*FontSizeBox.SelectedIndex);}
    private AppSettings Read(bool requireProvider=false)
    {
        var p=new ProviderSettings((ProviderKind)ProtocolBox.SelectedIndex,BaseUrlBox.Text.Trim(),KeyBox.Password.Trim(),ModelBox.Text.Trim());
        if(requireProvider||p.ApiKey.Length>0||p.Model.Length>0)
        {using var request=ProtocolRequest.Create(new("Hello.",QueryMode.Translation,TranslationDirection.EnglishToChinese,2,PromptBox.Text,p),true);}
        if(string.IsNullOrWhiteSpace(PromptBox.Text))throw new QueryException("请填写翻译提示词，或恢复默认。");
        return initial with {Provider=p,SystemPrompt=PromptBox.Text.Trim(),Hotkey=HotkeyBox.Text.Trim(),Theme=(ThemePreference)ThemeBox.SelectedIndex,FontSize=16+2*FontSizeBox.SelectedIndex,Trigger=(TranslationTrigger)TriggerBox.SelectedIndex};
    }
    private void SaveClick(object sender,RoutedEventArgs e)
    {
        try{var error=apply(Read());if(error is not null){StatusText.Text=error;return;}DialogResult=true;}
        catch(QueryException ex){StatusText.Text=ex.Message;}
        catch(Exception){StatusText.Text="无法保存设置，请检查本地应用数据目录的写入权限。";}
    }
    private async void TestClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var value=Read(true);TestButton.IsEnabled=false;SaveButton.IsEnabled=false;StatusText.Text="正在验证模型连接…";
            testSession?.Dispose();testSession=new(new ProviderClient(http));
            await testSession.StartAsync(new("Hello.",QueryMode.Translation,TranslationDirection.EnglishToChinese,2,QueryPlanner.DefaultPrompt,value.Provider));
            StatusText.Text=testSession.State.Status==QueryStatus.Success?"连接成功，模型已返回译文。":testSession.State.Error;
        }
        catch(QueryException ex){StatusText.Text=ex.Message;}
        finally{TestButton.IsEnabled=true;SaveButton.IsEnabled=true;}
    }
    private void ResetPromptClick(object sender,RoutedEventArgs e)=>PromptBox.Text=QueryPlanner.DefaultPrompt;
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void WindowKeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Escape)Close();}
}
