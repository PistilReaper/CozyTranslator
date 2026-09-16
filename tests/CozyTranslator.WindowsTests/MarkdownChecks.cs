using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CozyTranslator.Desktop;
using CozyTranslator.Desktop.Rendering;
using CozyTranslator.Desktop.Services;
using WpfMath.Controls;

internal static class MarkdownChecks
{
    public const string Sample = """
        ## 对比学习目标
        我们最小化 **对比损失**，并将温度 $\tau$ 设为可学习参数。

        $$
        \mathcal{L} = -\frac{1}{N}\sum_{i=1}^{N}\log p_i
        $$

        - **归一化**：令 \(\|z_i\|_2 = 1\)。
        - *相似度*：使用余弦距离。

        > 公式中的符号与下标保持不变。

        | 参数 | 含义 |
        | --- | --- |
        | $N$ | 样本数 |
        | $\tau$ | 温度 |

        `loss.backward()` 保持原样。
        """;

    public static async Task Run(MainWindow window, MainViewModel vm, Action<string, Action> check,
        Action<bool, string> assert, string output, Action<Window, string> capture)
    {
        var view = (MarkdownView)window.FindName("ResultEditor");
        vm.Source = "Research Markdown example.";
        var query = vm.TranslateAsync();
        await Task.Delay(160);
        check("Streaming Markdown shows partial content before completion", () =>
            assert(vm.Busy && new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text.Contains("对比"), "Streaming view stayed blank"));
        await query; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check("Markdown flows through translation with heading, list and table", () =>
        {
            assert(view.Document.Blocks.OfType<Paragraph>().First().FontWeight == FontWeights.SemiBold, "Heading was not styled");
            assert(view.Document.Blocks.OfType<System.Windows.Documents.List>().Any(), "List not rendered");
            assert(view.Document.Blocks.OfType<Table>().Any(), "Table not rendered");
            assert(vm.CopyText == Sample, "Copy button changed the model's Markdown");
        });
        check("Scientific formulas become native vector controls", () =>
            assert(Visuals<FormulaControl>(view).Count(f => !f.HasError) >= 5, "Missing inline or display formulas"));
        window.Width = 580; window.Height = 940;
        ThemeService.Apply(false); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        capture(window, Path.Combine(output, "markdown-light.png"));
        ThemeService.Apply(true); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check("Formula ink follows the dark theme", () =>
            assert(Visuals<FormulaControl>(view).All(f => ((SolidColorBrush)f.Foreground).Color == ((SolidColorBrush)Application.Current.Resources["Ink"]).Color), "Formula did not recolor"));
        capture(window, Path.Combine(output, "markdown-dark.png"));
        TypographyService.Apply(24); window.Width = window.MinWidth; window.Height = window.MinHeight;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check("Formula size and width follow the reader settings", () =>
        {
            assert(Visuals<FormulaControl>(view).All(f => f.Scale == 24), "Formula size did not follow font size");
            foreach (var scroll in Visuals<ScrollViewer>(view).Where(s => s.Content is FormulaControl))
                assert(scroll.ActualWidth <= view.ActualWidth, "Display formula overflows narrow window");
        });
        capture(window, Path.Combine(output, "markdown-narrow.png"));
        TypographyService.Apply(18); ThemeService.Apply(false);
        window.Width = 520; window.Height = 700;

        var isolated = new MarkdownView { FontSize = 18 };
        void Set(string text) { isolated.Markdown = text; isolated.RenderNow(); }
        string Text() => new TextRange(isolated.Document.ContentStart, isolated.Document.ContentEnd).Text;
        check("All four math delimiters and Chinese adjacency are supported", () =>
        {
            Set("中文$x_i^2$行内 \\(escaped)\n\n$$x^2$$\n\n\\(\\frac{a}{b}\\)\n\n\\[\\sqrt{x}\\]");
            assert(FormulaCount(isolated.Document) == 4, "One delimiter style was missed");
        });
        check("Matrices, aligned equations, sums and integrals render", () =>
        {
            foreach (var latex in new[] { @"\begin{pmatrix}a&b\\c&d\end{pmatrix}", @"\begin{align}a&=b+c\\d&=e\end{align}", @"\int_0^\infty e^{-x}dx", @"\sum_{i=1}^N x_i" })
            { Set("$$\n" + latex + "\n$$"); assert(FormulaCount(isolated.Document) == 1, "Not rendered: " + latex); }
        });
        check("Code, currency and escaped dollars stay literal", () =>
        {
            Set("Cost $5 and $10. Escaped \\$x\\$. `\\(x\\) $y$`\n\n```python\nvalue = '$x$'\n``` ");
            assert(FormulaCount(isolated.Document) == 0 && Text().Contains("$5 and $10") && Text().Contains("$y$"), "Literal content parsed as math");
        });
        check("Incomplete and unsupported formulas remain readable", () =>
        {
            Set(@"before $\frac{x}{"); assert(Text().Contains(@"$\frac{x}{"), "Incomplete streamed formula was lost");
            Set(@"before $\notARealCommand{x}$ after");
            assert(Text().Contains(@"$\notARealCommand{x}$") && Text().Contains("after"), "Unsupported math swallowed content");
        });
        check("Partial selection and select-all preserve formula source", () =>
        {
            Set(@"before $x^2$ after");
            var p = isolated.Document.Blocks.OfType<Paragraph>().Single();
            var math = p.Inlines.OfType<InlineUIContainer>().Single();
            isolated.Selection.Select(math.ElementStart, math.ElementEnd);
            assert(isolated.SelectionText() == "$x^2$", "Formula selection lost LaTeX: " + isolated.SelectionText());
            isolated.SelectAll(); assert(isolated.SelectionText() == isolated.Markdown, "Select-all lost original Markdown");
            Set("before\n\n$$x^2$$\n\nafter");
            var block = isolated.Document.Blocks.OfType<BlockUIContainer>().Single();
            isolated.Selection.Select(block.ElementStart, block.ElementEnd);
            assert(isolated.SelectionText() == "$$x^2$$", "Display formula selection lost LaTeX");
        });
        window.Left = 100; window.Top = 100; window.Activate();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(300);
        var clipboardBackup = Clipboard.GetDataObject();
        try
        {
            view.Focus(); view.SelectAll();
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, view);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150); // Let the Windows clipboard/OLE message loop finish the copy.
            check("Rich text copy command retains LaTeX in clipboard", () =>
                assert(vm.Status == "已复制" && Clipboard.GetText() == view.Markdown, "Clipboard did not retain Markdown; status=" + vm.Status));
        }
        finally { if (clipboardBackup is not null) Clipboard.SetDataObject(clipboardBackup, true); else Clipboard.Clear(); }
        check("Result does not execute HTML or load remote images", () =>
        {
            Set("<script>alert(1)</script> ![figure](https://example.com/pixel.png)");
            assert(Text().Contains("<script>") && Text().Contains("figure") && !Visuals<Image>(isolated).Any(), "External content was loaded");
        });
        check("Long research output renders without a browser process", () =>
        {
            var timer = Stopwatch.StartNew(); Set(string.Join("\n\n", Enumerable.Repeat(Sample, 15))); timer.Stop();
            Console.WriteLine($"INFO Markdown {isolated.Markdown.Length} chars rendered in {timer.ElapsedMilliseconds} ms");
            assert(FormulaCount(isolated.Document) == 75, "Long document lost formulas");
            timer.Restart(); isolated.Markdown += "\n\n追加译文。"; isolated.RenderNow(); timer.Stop();
            Console.WriteLine($"INFO Incremental Markdown render with reused equations: {timer.ElapsedMilliseconds} ms");
            assert(FormulaCount(isolated.Document) == 75, "Incremental render lost cached equations");
        });
        var smallWindow = new Window { Content = isolated, Width = 320, Height = 230, Left = 100, Top = 100, Owner = window };
        try
        {
            Set("$" + string.Join(" + ", Enumerable.Repeat("x_i^2", 30)) + "$"); smallWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var horizontal = Visuals<ScrollViewer>(isolated).First(s => s.Content is FormulaControl);
            horizontal.ScrollToRightEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check("Overlong inline formulas have usable horizontal scrolling", () =>
                assert(horizontal.IsEnabled && horizontal.ScrollableWidth > 0 && horizontal.HorizontalOffset > 0 && horizontal.ActualWidth <= isolated.ActualWidth, "Wide formula cannot be read completely"));
            Set(string.Join("\n\n", Enumerable.Repeat("阅读中的段落保持原来的滚动位置。", 100)));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            isolated.ScrollToVerticalOffset(200); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var offset = isolated.VerticalOffset;
            isolated.Markdown += "\n\n继续生成。";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check("Streaming append preserves the reader's scroll position", () =>
                assert(offset > 0 && Math.Abs(isolated.VerticalOffset - offset) < 5, "Reader was moved back to the beginning"));
        }
        finally { smallWindow.Close(); }
        window.Left = 100; window.Top = 100; window.Activate();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(300);
        var first = view.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
        var before = new Point(window.Left, window.Top);
        await NativeWindowProbe.Drag(view, new Point(first.Left + 3, first.Top + first.Height / 2), 70, 0);
        check("Real mouse selects rendered text without moving the window", () =>
            assert(!view.Selection.IsEmpty && new Point(window.Left, window.Top) == before, "Result text drag moved the window or failed to select"));
        vm.Source = "A short result for blank-area dragging."; await vm.TranslateAsync();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        before = new Point(window.Left, window.Top);
        await NativeWindowProbe.Drag(view, new Point(view.ActualWidth / 2, view.ActualHeight - 20), 40, 20);
        check("Real mouse still drags unused space in the rich result view", () =>
            assert(window.Left > before.X + 10 && window.Top > before.Y + 5, "Blank rich-text space stopped dragging"));
        window.Left = -10000; window.Top = -10000;
    }

    private static int FormulaCount(DependencyObject node)
    {
        int count = node is FormulaControl { HasError: false } ? 1 : 0;
        foreach (var child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject d) count += FormulaCount(d);
        return count;
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match;
            foreach (var item in Visuals<T>(child)) yield return item;
        }
    }
}
