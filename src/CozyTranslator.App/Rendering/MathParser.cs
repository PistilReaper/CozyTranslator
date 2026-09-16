using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace CozyTranslator.Desktop.Rendering;

// Parsing inside Markdig keeps code spans, fenced code and escaped delimiters literal.
public sealed class FormulaInline(string source, string latex, bool display) : LeafInline
{
    public string Source { get; } = source;
    public string Latex { get; } = latex;
    public bool Display { get; } = display;
}

public sealed class MathParser : InlineParser
{
    public MathParser() => OpeningCharacters = ['$', '\\'];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice.Start;
        var text = slice.Text;
        bool slash = text[start] == '\\';
        if (slash && (start + 1 > slice.End || text[start + 1] is not ('(' or '['))) return false;
        bool display = slash ? text[start + 1] == '[' : start + 1 <= slice.End && text[start + 1] == '$';
        int size = slash || display ? 2 : 1;
        string close = slash ? (display ? "\\]" : "\\)") : (display ? "$$" : "$");
        int contentStart = start + size;
        // Single dollars need non-whitespace content; prices such as "$5 and $10" stay text.
        if (!display && !slash && (contentStart > slice.End || char.IsWhiteSpace(text[contentStart]))) return false;
        for (int i = contentStart; i <= slice.End; i++)
        {
            if (!display && text[i] is '\r' or '\n') return false;
            if (i + close.Length - 1 <= slice.End && text.AsSpan(i, close.Length).SequenceEqual(close))
            {
                if (!slash && ((!display && (i == contentStart || char.IsWhiteSpace(text[i - 1]) ||
                    (i + 1 <= slice.End && char.IsDigit(text[i + 1])))) ||
                    (i + close.Length <= slice.End && text[i + close.Length] == '$'))) continue;
                var latex = text[contentStart..i].Trim();
                if (latex.Length == 0) return false;
                var end = i + close.Length;
                processor.Inline = new FormulaInline(text[start..end], latex, display);
                slice.Start = end;
                return true;
            }
            if (text[i] == '\\') i++; // Escaped dollars/brackets are part of the formula.
        }
        return false; // A streaming formula becomes math only when its closing delimiter arrives.
    }
}
