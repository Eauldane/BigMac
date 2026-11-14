using BigMac.Scripting;
using ImGuiColorTextEditNet;
using ImGuiColorTextEditNet.Editor;
using ImGuiColorTextEditNet.Syntax;
using Xunit;

namespace BigMac.Tests;

public sealed class ScriptEditorModelTests
{
    [Fact]
    public void TextRoundTripsUnicodeAndNormalizesLineEndings()
    {
        var text = new TextEditorText(new TextEditorOptions());
        text.SetText("/echo 日本語 😀 café\nrepeat(2) {\r\n}\n");

        Assert.Equal(string.Join(Environment.NewLine, "/echo 日本語 😀 café", "repeat(2) {", "}", ""),
            text.GetText((0, 0), (text.LineCount, 0)));
    }

    [Fact]
    public void MultilineInsertionAndDeletionPreserveSurroundingText()
    {
        var text = new TextEditorText(new TextEditorOptions());
        text.SetText("/echo before after");
        var end = text.InsertTextAt((0, 12), "\n/echo 日本語\n");

        Assert.Equal(new Coordinates(2, 0), end);
        Assert.Equal(" after", text.GetLineText(2));
        text.DeleteRange((0, 12), end);
        Assert.Equal("/echo before after", text.GetLineText(0));
    }

    [Fact]
    public void TabCoordinatesRoundTrip()
    {
        var options = new TextEditorOptions { TabSize = 4 };
        var text = new TextEditorText(options);
        text.SetText("\t/echo hello");

        Assert.Equal(1, text.GetCharacterIndex((0, 4)));
        Assert.Equal(4, text.GetCharacterColumn(0, 1));
    }

    [Fact]
    public void RecolouringAnEarlierLineUpdatesFollowingLexicalState()
    {
        var options = new TextEditorOptions();
        var text = new TextEditorText(options);
        var colour = new TextEditorColor(options, text) { SyntaxHighlighter = new TestHighlighter() };
        text.SetText("let message = \"unfinished\nrepeat(2) {}\n\"\nrepeat(3) {}");
        colour.ColorizeIncremental();
        Assert.Equal((PaletteIndex)ScriptColour.String, text.GetLine(1)[0].ColorIndex);

        text.InsertTextAt((0, 25), "\"");
        colour.InvalidateColor(0, 1);
        colour.ColorizeIncremental();

        Assert.Equal((PaletteIndex)ScriptColour.Keyword, text.GetLine(1)[0].ColorIndex);
        Assert.Equal((PaletteIndex)ScriptColour.String, text.GetLine(3)[0].ColorIndex);
    }

    [Fact]
    public void SelectToEndIncludesTheFinalLine()
    {
        var text = new TextEditorText(new TextEditorOptions());
        text.SetText("first\nlast");
        var selection = new TextEditorSelection(text);
        var movement = new TextEditorMovement(selection, text);

        movement.MoveToEndOfFile(true);

        Assert.Equal(new Coordinates(1, 4), selection.Cursor);
        Assert.Equal("first" + Environment.NewLine + "last", selection.GetSelectedText());
    }

    [Fact]
    public void PendingRecolouringIsSafeAfterDocumentShrinks()
    {
        var options = new TextEditorOptions();
        var text = new TextEditorText(options);
        var colour = new TextEditorColor(options, text) { SyntaxHighlighter = new TestHighlighter() };
        text.SetText(string.Join('\n', Enumerable.Repeat("repeat(2) {}", 300)));
        colour.ColorizeIncremental();
        text.SetText("/echo short");

        colour.ColorizeIncremental();

        Assert.Equal((PaletteIndex)ScriptColour.Command, text.GetLine(0)[0].ColorIndex);
    }

    private sealed class TestHighlighter : ISyntaxHighlighter
    {
        public bool AutoIndentation => true;
        public int MaxLinesPerFrame => 256;
        public string? GetTooltip(string id) => null;

        public object Colorize(Span<Glyph> line, object? state)
        {
            var chars = line.ToArray().Select(glyph => glyph.Char).ToArray();
            var colours = new ScriptColour[line.Length];
            var result = ScriptHighlighter.HighlightLine(chars, colours,
                state is ScriptHighlightState previous ? previous : default);
            for (var index = 0; index < line.Length; index++)
                line[index] = new Glyph(line[index].Char, (PaletteIndex)colours[index]);
            return result;
        }
    }
}
