using System.Windows;
using System.Windows.Input;
using CozyTranslator.Desktop.Services;
namespace CozyTranslator.Desktop;
public partial class BubbleWindow : Window
{
    private System.Windows.Point down;
    private bool dragging;
    public event Action? Invoked;
    public event Action? Moved;
    public BubbleWindow()=>InitializeComponent();
    private void PointerDown(object sender,MouseButtonEventArgs e){down=e.GetPosition(this);dragging=false;}
    private void PointerMove(object sender,System.Windows.Input.MouseEventArgs e){if(e.LeftButton!=MouseButtonState.Pressed||dragging)return;var p=e.GetPosition(this);if(Math.Abs(p.X-down.X)+Math.Abs(p.Y-down.Y)>5){dragging=true;DragMove();DesktopServices.Clamp(this);Moved?.Invoke();}}
    private void PointerUp(object sender,MouseButtonEventArgs e){if(!dragging)Invoked?.Invoke();dragging=false;}
}
