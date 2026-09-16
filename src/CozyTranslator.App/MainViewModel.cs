using System.ComponentModel;
using CozyTranslator.Core;
using CozyTranslator.Desktop.Services;

namespace CozyTranslator.Desktop;
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Func<AppSettings> settings;
    private readonly Action<AppSettings> save;
    private string source="",notice="";
    private int modeIndex,style,direction;
    private QueryRequest? lastQuery;
    public QuerySession Session {get;}
    public MainViewModel(QuerySession session,Func<AppSettings> settings,Action<AppSettings> save)
    {
        Session=session;this.settings=settings;this.save=save;style=settings().Style;direction=(int)settings().Direction;
        session.Changed+=state=>{if(state.Status is QueryStatus.Idle or QueryStatus.Loading or QueryStatus.Success or QueryStatus.Cancelled or QueryStatus.Error)notice="";Refresh();};
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
    public string Source {get=>source;set{if(source==value)return;source=value;notice="";lastQuery=null;Session.Reset();Refresh();}}
    public int ModeIndex {get=>modeIndex;set{if(modeIndex==value)return;modeIndex=value;notice="";Refresh();}}
    public int DirectionIndex {get=>direction;set{direction=value;Refresh();}}
    public int Style {get=>style;set{style=Math.Clamp(value,0,4);Refresh();}}
    public string StyleLabel=>new[]{"学术","专业","通用","自然","口语"}[style];
    public bool IsDictionary=>lastQuery?.Mode==QueryMode.Dictionary || (lastQuery is null&&modeIndex==(int)QueryMode.Dictionary);
    public bool ShowTone=>!IsDictionary;
    public QueryState State=>Session.State;
    public bool Busy=>State.Status is QueryStatus.Loading or QueryStatus.Streaming;
    public bool HasQuery=>lastQuery is not null;
    public bool HasResult=>State.Text.Length>0 || State.Entry is not null;
    public bool HasDictionary=>State.Entry is not null;
    public bool HasTranslation=>State.Text.Length>0;

    public bool Configured=>!string.IsNullOrWhiteSpace(settings().Provider.ApiKey)&&!string.IsNullOrWhiteSpace(settings().Provider.Model);
    public bool ShowConnect=>!Configured&&!Busy;
    public bool CanCopy=>HasResult;
    public string ResultText=>State.Text;
    public string Word=>State.Entry?.Word??"";
    public bool CanSpeak=>State.Entry?.Found==true;
    public string WordHint=>State.Entry?.Found==false?"暂未识别这个词条，可切换到翻译再试。":"";
    public IEnumerable<object> Phonetics=>State.Entry?.Phonetics.Select(p=>(object)new {Label=p.Accent.ToUpperInvariant() switch {"UK"=>"英","US"=>"美",_=>p.Accent},Ipa="/"+p.Ipa.Trim('/')+"/"})??[];
    public IEnumerable<object> Meanings=>State.Entry?.Meanings.Select(m=>(object)new {Pos=m.PartOfSpeech,Definition=string.Join("；",m.Definitions)})??[];
    public string ResultLabel=>IsDictionary?"词典":"译文";
    public string Status=>notice.Length>0?notice:State.Status switch{QueryStatus.Loading=>"正在连接模型…",QueryStatus.Streaming=>"正在生成 · 可随时停止",QueryStatus.Success=>"已完成",QueryStatus.Cancelled=>State.Error,QueryStatus.Error=>State.Error,_=>""};
    public bool IsError=>State.Status==QueryStatus.Error;
    public string CopyText=>State.Entry is {} e?e.Word+"\n"+string.Join("  ",e.Phonetics.Select(p=>$"{p.Accent} /{p.Ipa.Trim('/')}/"))+"\n"+string.Join("\n",e.Meanings.Select(m=>m.PartOfSpeech+" "+string.Join("；",m.Definitions))):State.Text;
    public void Notify(string text){notice=text;Refresh();}
    public async Task TranslateAsync()
    {
        notice="";
        var effective=QueryPlanner.Resolve(source,(QueryMode)modeIndex);
        var text=effective==QueryMode.Dictionary?QueryPlanner.NormalizeWord(source):source;
        lastQuery=new(text,effective,(TranslationDirection)direction,style,settings().SystemPrompt,settings().Provider);
        await Session.StartAsync(lastQuery);
    }
    public async Task CommitStyleAsync()
    {
        save(settings() with {Style=style});
        if(lastQuery is not null && lastQuery.Style!=style && !IsDictionary)await TranslateAsync();
    }
    public async Task CommitDirectionAsync()
    {
        save(settings() with {Direction=(TranslationDirection)direction});
        if(lastQuery is not null&&!IsDictionary)await TranslateAsync();
    }
    public async Task CommitModeAsync(){if(lastQuery is not null)await TranslateAsync();else Refresh();}
    public void ConfigurationChanged(){Session.Reset();lastQuery=null;notice="设置已保存";Refresh();}
}
