using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using WpfMath.Controls;

namespace CozyTranslator.Desktop.Rendering;

public sealed class MarkdownView : RichTextBox, INotifyPropertyChanged
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(MarkdownView), new PropertyMetadata(false, Changed));
    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public bool IsStreaming { get => (bool)GetValue(IsStreamingProperty); set => SetValue(IsStreamingProperty, value); }
    public double FormulaWidth => Math.Max(24, ActualWidth - 24);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? CopyFeedback;
    private readonly DispatcherTimer timer;
    private string rendered = "";
    private double renderedSize;
    private Dictionary<string, Queue<FormulaControl>> formulas = new();
    private Dictionary<string, Queue<FormulaControl>> previousFormulas = new();

    public MarkdownView()
    {
        IsReadOnly = true; IsUndoEnabled = false; IsDocumentEnabled = true;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => RenderNow(), Dispatcher);
        timer.Stop();
        Unloaded += (_, _) => timer.Stop();
        Loaded += (_, _) => RenderNow();
        SizeChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormulaWidth)));
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Copy || Selection.IsEmpty) return;
            e.Handled = true;
            try { Clipboard.SetText(SelectionText()); CopyFeedback?.Invoke("已复制"); }
            catch (System.Runtime.InteropServices.COMException) { CopyFeedback?.Invoke("剪贴板暂时不可写，请重试。"); }
        });
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty && timer is not null) RenderNow();
    }

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (!view.IsStreaming || view.Markdown.Length == 0) view.RenderNow();
        else if (!view.timer.IsEnabled) view.timer.Start();
    }

    public void RenderNow()
    {
        timer.Stop();
        if (rendered == Markdown && renderedSize == FontSize) return;
        var offset = VerticalOffset;
        int start = Document.ContentStart.GetOffsetToPosition(Selection.Start);
        int end = Document.ContentStart.GetOffsetToPosition(Selection.End);
        bool selected = !Selection.IsEmpty;
        previousFormulas = formulas;
        formulas = new();
        foreach (var formula in previousFormulas.Values.SelectMany(q => q))
        {
            if (formula.Parent is InlineUIContainer inline) inline.Child = null;
            else if (formula.Parent is ContentControl content) content.Content = null;
        }
        Document = new MarkdownRenderer(this).Render(Markdown);
        previousFormulas.Clear();
        rendered = Markdown; renderedSize = FontSize;
        if (selected)
        {
            int length = Document.ContentStart.GetOffsetToPosition(Document.ContentEnd);
            Selection.Select(Document.ContentStart.GetPositionAtOffset(Math.Min(start, length))!, Document.ContentStart.GetPositionAtOffset(Math.Min(end, length))!);
        }
        ScrollToVerticalOffset(offset);
    }

    // Reuse completed equations during streaming instead of parsing them on every token batch.
    internal FormulaControl? Formula(string latex, Func<FormulaControl?> create)
    {
        var formula = previousFormulas.TryGetValue(latex, out var old) && old.Count > 0 ? old.Dequeue() : create();
        if (formula is null) return null;
        if (!formulas.TryGetValue(latex, out var current)) formulas[latex] = current = new();
        current.Enqueue(formula);
        return formula;
    }

    // Keep LaTeX when copying a selection containing an embedded formula.
    public string SelectionText()
    {
        if (Selection.IsEmpty) return "";
        if (Selection.Start.CompareTo(Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) <= 0 &&
            Selection.End.CompareTo(Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) >= 0) return Markdown;
        var text = new StringBuilder();
        var cursor = Selection.Start;
        while (cursor is not null && cursor.CompareTo(Selection.End) < 0)
        {
            var next = cursor.GetNextContextPosition(LogicalDirection.Forward);
            switch (cursor.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var stop = next is not null && next.CompareTo(Selection.End) < 0 ? next : Selection.End;
                    text.Append(new TextRange(cursor, stop).Text);
                    break;
                case TextPointerContext.EmbeddedElement:
                    if (cursor.Parent is FrameworkContentElement { Tag: string latex }) text.Append(latex);
                    break;
                case TextPointerContext.ElementEnd:
                    if (cursor.Parent is Paragraph or BlockUIContainer) text.AppendLine();
                    break;
                case TextPointerContext.ElementStart:
                    if (cursor.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) text.AppendLine();
                    break;
            }
            cursor = next;
        }
        return text.ToString().TrimEnd('\r', '\n');
    }

    public bool IsOverText(System.Windows.Point point)
    {
        var position = GetPositionFromPoint(point, false);
        if (position is null) return false;
        var left = position.GetCharacterRect(LogicalDirection.Backward);
        var right = position.GetCharacterRect(LogicalDirection.Forward);
        left.Union(right); left.Inflate(3, 1);
        return left.Contains(point);
    }
}
