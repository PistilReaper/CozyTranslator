using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CozyTranslator.Desktop.Services;

namespace CozyTranslator.Desktop;
public partial class MainWindow : Window
{
    public MainViewModel ViewModel {get;}
    public bool DialogOpen {get;set;}
    public event Action? SettingsRequested;
    public event Action? CollapseRequested;
    public event Action? HideToTrayRequested;
    public event Action? CopyRequested;
    public event Action? SpeakRequested;
    private bool ready;
    public MainWindow(MainViewModel vm){ViewModel=vm;InitializeComponent();DataContext=vm;ResultEditor.CopyFeedback+=vm.Notify;WindowDragging.Attach(this);BrandTitle.ToolTip="CozyTranslator v"+VersionInfo.Current;ready=true;}
    private void PinClick(object sender,RoutedEventArgs e){Topmost=!Topmost;PinButton.SetResourceReference(Control.ForegroundProperty,Topmost?"Accent":"Muted");PinButton.ToolTip=Topmost?"取消置顶":"置顶";}
    private void SettingsClick(object sender,RoutedEventArgs e)=>SettingsRequested?.Invoke();
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void HideToTrayClick(object sender,RoutedEventArgs e)=>HideToTrayRequested?.Invoke();
    private void CopyClick(object sender,RoutedEventArgs e)=>CopyRequested?.Invoke();
    private void SpeakClick(object sender,RoutedEventArgs e)=>SpeakRequested?.Invoke();
    private void ClearClick(object sender,RoutedEventArgs e){ViewModel.Source="";SourceEditor.Focus();}
    private async void TranslateClick(object sender,RoutedEventArgs e)=>await Submit();
    private async Task Submit(){if(ViewModel.Busy){ViewModel.Session.Stop();return;}if(!ViewModel.Configured){SettingsRequested?.Invoke();return;}await ViewModel.TranslateAsync();}
    private async void WindowKeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Escape){CollapseRequested?.Invoke();e.Handled=true;}else if(e.Key==Key.Enter&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){e.Handled=true;await Submit();}}
    private async void ModeChecked(object sender,RoutedEventArgs e){if(!ready)return;ViewModel.ModeIndex=int.Parse((string)((RadioButton)sender).Tag);await ViewModel.CommitModeAsync();}
    private async void DirectionChanged(object sender,SelectionChangedEventArgs e){if(ready)await ViewModel.CommitDirectionAsync();}
    private async void StyleCommitted(object sender,MouseButtonEventArgs e){if(ready)await ViewModel.CommitStyleAsync();}
    private async void StyleKeyUp(object sender,System.Windows.Input.KeyEventArgs e){if(ready&&e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)await ViewModel.CommitStyleAsync();}
}
