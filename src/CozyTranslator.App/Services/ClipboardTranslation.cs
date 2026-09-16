using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace CozyTranslator.Desktop.Services;
public sealed class ClipboardTranslation : IDisposable
{
    private readonly DesktopServices desktop;
    private readonly MainViewModel viewModel;
    private readonly Func<bool> enabled,suspended;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    public ClipboardTranslation(DesktopServices desktop,MainViewModel viewModel,Func<bool> enabled,Func<bool> suspended)
    {
        this.desktop=desktop;this.viewModel=viewModel;this.enabled=enabled;this.suspended=suspended;
        desktop.ClipboardChanged+=Changed;timer.Tick+=Read;
    }
    private void Changed(){timer.Stop();if(enabled()&&!suspended())timer.Start();}
    private async void Read(object? sender,EventArgs e)
    {
        timer.Stop();
        if(!enabled()||suspended()||DesktopServices.IsClipboardFromThisProcess())return;
        try
        {
            if(!Clipboard.ContainsText())return;
            var text=Clipboard.GetText();
            if(string.IsNullOrWhiteSpace(text)||(text==viewModel.Source&&viewModel.HasQuery))return;
            viewModel.Source=text;
            if(viewModel.Configured)await viewModel.TranslateAsync();
            else viewModel.Notify("请先配置模型");
        }
        catch(COMException){viewModel.Notify("剪贴板暂时被占用，请重新复制。");}
    }
    public void Dispose(){desktop.ClipboardChanged-=Changed;timer.Stop();timer.Tick-=Read;}
}
