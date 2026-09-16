using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfMath.Controls;
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using Table = System.Windows.Documents.Table;
using List = System.Windows.Documents.List;

namespace CozyTranslator.Desktop.Rendering;

public sealed class MarkdownRenderer(MarkdownView owner)
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().DisableHtml();
        builder.InlineParsers.Insert(0, new MathParser());
        return builder.Build();
    }

    public FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity };
        BindingOperations.SetBinding(document, TextElement.FontSizeProperty, new Binding(nameof(owner.FontSize)) { Source = owner });
        BindingOperations.SetBinding(document, TextElement.FontFamilyProperty, new Binding(nameof(owner.FontFamily)) { Source = owner });
        document.SetResourceReference(TextElement.ForegroundProperty, "Ink");
        AddBlocks(document.Blocks, Markdown.Parse(markdown, Pipeline));
        return document;
    }

    private Paragraph Paragraph() => new() { Margin = new Thickness(0, 0, 0, 10), LineHeight = owner.FontSize * 1.55 };

    private void AddBlocks(BlockCollection target, ContainerBlock source)
    {
        foreach (var block in source)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var title = Paragraph();
                    title.FontSize = owner.FontSize * (heading.Level <= 2 ? 1.2 : 1.08);
                    title.FontWeight = FontWeights.SemiBold;
                    title.Margin = new Thickness(0, 5, 0, 10);
                    AddInlines(title.Inlines, heading.Inline);
                    target.Add(title);
                    break;
                case ParagraphBlock paragraph:
                    AddParagraph(target, paragraph.Inline);
                    break;
                case ListBlock list:
                    var output = new List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(24, 0, 0, 0) };
                    if (int.TryParse(list.OrderedStart, out var start) && start > 0) output.StartIndex = start;
                    foreach (ListItemBlock item in list)
                    {
                        var li = new ListItem();
                        AddBlocks(li.Blocks, item);
                        foreach (var child in li.Blocks) child.Margin = new Thickness(0, 0, 0, 4);
                        output.ListItems.Add(li);
                    }
                    target.Add(output);
                    break;
                case QuoteBlock quote:
                    var section = new Section { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2), Margin = new Thickness(0, 4, 0, 12) };
                    section.SetResourceReference(Block.BorderBrushProperty, "AccentSoft");
                    AddBlocks(section.Blocks, quote);
                    target.Add(section);
                    break;
                case CodeBlock code:
                    var codeParagraph = Paragraph();
                    codeParagraph.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                    codeParagraph.FontSize = owner.FontSize * .9;
                    codeParagraph.Padding = new Thickness(10);
                    codeParagraph.SetResourceReference(TextElement.BackgroundProperty, "Wash");
                    codeParagraph.Inlines.Add(new Run(code.Lines.ToString()));
                    target.Add(codeParagraph);
                    break;
                case Markdig.Extensions.Tables.Table table:
                    var result = new Table { CellSpacing = 0, Margin = new Thickness(0, 3, 0, 12) };
                    var group = new TableRowGroup(); result.RowGroups.Add(group);
                    foreach (Markdig.Extensions.Tables.TableRow row in table)
                    {
                        var resultRow = new TableRow(); group.Rows.Add(resultRow);
                        foreach (Markdig.Extensions.Tables.TableCell cell in row)
                        {
                            var resultCell = new TableCell { Padding = new Thickness(7), BorderThickness = new Thickness(0, 0, 0, 1) };
                            resultCell.SetResourceReference(TableCell.BorderBrushProperty, "Line");
                            if (row.IsHeader) { resultCell.FontWeight = FontWeights.SemiBold; resultCell.SetResourceReference(TextElement.BackgroundProperty, "Wash"); }
                            AddBlocks(resultCell.Blocks, cell);
                            resultRow.Cells.Add(resultCell);
                        }
                    }
                    target.Add(result);
                    break;
                case ThematicBreakBlock:
                    var rule = Paragraph(); rule.BorderThickness = new Thickness(0, 1, 0, 0);
                    rule.SetResourceReference(Block.BorderBrushProperty, "Line"); target.Add(rule);
                    break;
            }
        }
    }

    private void AddParagraph(BlockCollection target, ContainerInline? source)
    {
        var paragraph = Paragraph();
        if (source is null) return;
        foreach (var inline in source)
        {
            if (inline is FormulaInline { Display: true } math)
            {
                if (paragraph.Inlines.Count > 0) { target.Add(paragraph); paragraph = Paragraph(); }
                var formula = CreateFormula(math);
                if (formula is null) { paragraph.Inlines.Add(new Run(math.Source)); continue; }
                var scroll = FormulaHost(formula, true);
                var container = new BlockUIContainer(scroll) { Tag = math.Source, Margin = new Thickness(0, 0, 0, 10) };
                target.Add(container);
            }
            else if (inline is not LineBreakInline || paragraph.Inlines.Count > 0) AddInline(paragraph.Inlines, inline);
        }
        if (paragraph.Inlines.Count > 0) target.Add(paragraph);
    }

    private FormulaControl? CreateFormula(FormulaInline math) => owner.Formula(math.Source, () =>
    {
        var formula = new FormulaControl { Formula = math.Latex, SystemTextFontName = "Segoe UI", Focusable = false,
            ToolTip = math.Source, HorizontalAlignment = HorizontalAlignment.Left };
        formula.SetResourceReference(Control.ForegroundProperty, "Ink");
        formula.SetBinding(FormulaControl.ScaleProperty, new Binding(nameof(owner.FontSize)) { Source = owner });
        System.Windows.Automation.AutomationProperties.SetName(formula, math.Latex);
        return formula.HasError ? null : formula;
    });

    private ScrollViewer FormulaHost(FormulaControl formula, bool display)
    {
        var scroll = new ScrollViewer { Content = formula, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
            Padding = display ? new Thickness(2, 8, 2, 8) : new Thickness(0) };
        scroll.SetBinding(FrameworkElement.MaxWidthProperty, new Binding(nameof(owner.FormulaWidth)) { Source = owner });
        return scroll;
    }

    private void AddInlines(InlineCollection target, ContainerInline? source)
    {
        if (source is not null) foreach (var inline in source) AddInline(target, inline);
    }

    private void AddInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
            case LineBreakInline: target.Add(new LineBreak()); break;
            case CodeInline code:
                var run = new Run(code.Content) { FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                run.SetResourceReference(TextElement.BackgroundProperty, "Wash"); target.Add(run); break;
            case FormulaInline math:
                var formula = CreateFormula(math);
                if (formula is null) target.Add(new Run(math.Source));
                else target.Add(new InlineUIContainer(FormulaHost(formula, false)) { BaselineAlignment = BaselineAlignment.Center, Tag = math.Source });
                break;
            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                else span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, emphasis); target.Add(span); break;
            case LinkInline link:
                // Translate the label; external content is not loaded by the result view.
                var label = new Span { ToolTip = link.Url };
                label.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                AddInlines(label.Inlines, link); target.Add(label); break;
            case AutolinkInline link: target.Add(new Run(link.Url)); break;
            case ContainerInline container: AddInlines(target, container); break;
        }
    }
}
