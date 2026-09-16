using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CozyTranslator.Desktop.Rendering;
using WpfMath.Controls;
namespace CozyTranslator.Desktop.Services;
public static class WindowDragging
{
    public static void Attach(Window window)=>window.PreviewMouseLeftButtonDown+=(_,e)=>
    {
        if(e.LeftButton!=MouseButtonState.Pressed||e.ClickCount!=1)return;
        var node=e.OriginalSource as DependencyObject;
        for(;node is not null&&node!=window;node=Parent(node))
        {
            if(node is ButtonBase or ComboBox or Slider or ScrollBar or Thumb or PasswordBox or FormulaControl)return;
            if(node is MarkdownView markdown)
            {
                if(markdown.IsOverText(e.GetPosition(markdown)))return;
            }
            else if(node is TextBoxBase)
            {
                if(node is not TextBox box||!box.IsReadOnly||box.GetCharacterIndexFromPoint(e.GetPosition(box),false)>=0)return;
            }
        }
        // Popup clicks can route to the owner, but belong to a separate visual tree.
        if(node!=window)return;
        e.Handled=true;window.DragMove();
    };
    private static DependencyObject? Parent(DependencyObject node)=>node is Visual or Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
}
