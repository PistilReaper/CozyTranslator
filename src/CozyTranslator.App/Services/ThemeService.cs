using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace CozyTranslator.Desktop.Services;
public sealed class ThemeService : IDisposable
{
    private readonly RegistryKey? key;
    private readonly EventWaitHandle changed=new(false,EventResetMode.AutoReset);
    private readonly RegisteredWaitHandle? registration;
    private readonly object gate=new();
    private ThemePreference preference;
    private bool disposed;
    [DllImport("advapi32.dll")] private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key,bool subtree,uint filter,SafeWaitHandle changeEvent,bool asynchronous);
    public ThemeService(ThemePreference preference=ThemePreference.System,string registryPath=@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
    {
        this.preference=preference;
        key=Registry.CurrentUser.OpenSubKey(registryPath);
        if(key is not null)
        {
            Arm();
            registration=ThreadPool.RegisterWaitForSingleObject(changed,(_,_)=>
            {
                lock(gate){if(disposed)return;Arm();}
                var dispatcher=System.Windows.Application.Current.Dispatcher;
                if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(Refresh);
            },null,Timeout.Infinite,false);
        }
        Refresh();
    }
    private void Arm(){var result=RegNotifyChangeKeyValue(key!.Handle,false,0x10000004,changed.SafeWaitHandle,true);if(result!=0)throw new System.ComponentModel.Win32Exception(result);}
    public void SetPreference(ThemePreference value){preference=value;Refresh();}
    public void Refresh()
    {
        if(disposed)return;
        Apply(preference==ThemePreference.Dark||(preference==ThemePreference.System&&key?.GetValue("AppsUseLightTheme") is int light&&light==0));
    }
    public static void Apply(bool dark)
    {
#pragma warning disable WPF0001
        System.Windows.Application.Current.ThemeMode=dark?ThemeMode.Dark:ThemeMode.Light;
#pragma warning restore WPF0001
        var values=dark?new[]{"#242522","#2E2F2B","#EDE8DF","#AEA99E","#D79476","#4B3930","#42433C","#33342F","#E9A095"}:new[]{"#F8F6F1","#FFFEFB","#2D2C29","#77736B","#B9674D","#EFE0D5","#E4DFD5","#EFEBE3","#AB443B"};
        string[] keys=["Paper","Surface","Ink","Muted","Accent","AccentSoft","Line","Wash","ErrorInk"];
        for(int i=0;i<keys.Length;i++){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));brush.Freeze();System.Windows.Application.Current.Resources[keys[i]]=brush;}
    }
    public void Dispose(){lock(gate){disposed=true;registration?.Unregister(null);key?.Dispose();changed.Dispose();}}
}
