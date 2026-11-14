using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using BigMac.Scripting;
using ElezenTools.UI;
using ImGuiColorTextEditNet;
using ImGuiColorTextEditNet.Syntax;

namespace BigMac.UI;

internal sealed class ScriptEditor
{
    private TextEditor _editor = CreateEditor();
    private string _text = string.Empty;

    public void SetText(string text)
    {
        _editor = CreateEditor();
        _editor.AllText = text;
        _text = text;
        _editor.ScrollToLine(0);
    }

    public bool Draw(ref string text, Vector2 size, int maxLength)
    {
        if (!string.Equals(text, _text, StringComparison.Ordinal))
            SetText(text);

        if (!_editor.Render("##bigmac-script", size))
            return false;

        var edited = _editor.AllText;
        while (edited.Length > maxLength && _editor.UndoIndex > 0)
        {
            _editor.Undo();
            edited = _editor.AllText;
        }

        if (edited == _text)
            return false;

        text = _text = edited;
        return true;
    }

    public void SetError(ScriptParseException? error)
        => _editor.ErrorMarkers.SetErrorMarkers(error is null ? [] : new Dictionary<int, object>
        {
            [error.Line] = error.Message,
        });

    private static TextEditor CreateEditor()
    {
        var editor = new TextEditor { SyntaxHighlighter = new BigMacHighlighter() };
        editor.Options.IndentWithSpaces = true;
        editor.Renderer.IsShowingWhitespace = false;
        var palette = BigMacTheme.Palette;
        for (var index = 0; index < (int)PaletteIndex.Custom; index++)
            editor.SetColor((PaletteIndex)index, Colour.Vector4ToColour(BigMacTheme.Text));

        editor.SetColor(PaletteIndex.Background, Colour.Vector4ToColour(palette.CompactBg));
        editor.SetColor(PaletteIndex.LineNumber, Colour.Vector4ToColour(palette.CompactTextMuted));
        editor.SetColor(PaletteIndex.Selection, Colour.Vector4ToColour(Colour.WithAlpha(palette.Accent, 0.30f)));
        editor.SetColor(PaletteIndex.ErrorMarker, Colour.Vector4ToColour(Colour.WithAlpha(palette.BooleanFalse, 0.20f)));
        editor.SetColor(PaletteIndex.CurrentLineFill, Colour.Vector4ToColour(palette.CompactPanelAlt));
        editor.SetColor(PaletteIndex.CurrentLineFillInactive, 0);
        editor.SetColor(PaletteIndex.CurrentLineEdge, 0);
        editor.SetColor(PaletteIndex.Breakpoint, 0);
        editor.SetColor(PaletteIndex.ExecutingLine, 0);

        string[] colours =
        [
            "#F3EBDD", // text: cream
            "#E8AD52", // keywords: amber
            "#D8B4E2", // functions: lavender
            "#E6CAA0", // variables: sand
            "#B7D69A", // strings: sage
            "#F0BF8A", // numbers: apricot
            "#D8B4E2", // booleans: lavender
            "#A69E92", // comments: warm grey
            "#F1C875", // slash commands: gold
            "#A8D5CC", // targets: mint
            "#C7BFB2", // punctuation: stone
            "#E8AD52", // interpolation delimiters: amber
        ];
        for (var index = 0; index < colours.Length; index++)
            editor.SetColor((PaletteIndex)((int)PaletteIndex.Custom + index), Colour.Vector4ToColour(Colour.HexToVector4(colours[index])));
        return editor;
    }

    private sealed class BigMacHighlighter : ISyntaxHighlighter
    {
        public bool AutoIndentation => true;
        public int MaxLinesPerFrame => 256;
        public string? GetTooltip(string id) => ExpressionVariableDescriber.IsBuiltInVariableName(id)
            ? ExpressionVariableDescriber.Describe(id) : null;

        public object Colorize(Span<Glyph> line, object? state)
        {
            var chars = ArrayPool<char>.Shared.Rent(line.Length);
            var colours = ArrayPool<ScriptColour>.Shared.Rent(line.Length);
            try
            {
                for (var index = 0; index < line.Length; index++) chars[index] = line[index].Char;
                var next = ScriptHighlighter.HighlightLine(chars.AsSpan(0, line.Length), colours.AsSpan(0, line.Length),
                    state is ScriptHighlightState previous ? previous : default);
                for (var index = 0; index < line.Length; index++)
                    line[index] = new Glyph(line[index].Char, (PaletteIndex)((int)PaletteIndex.Custom + (int)colours[index]));
                return next;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
                ArrayPool<ScriptColour>.Shared.Return(colours);
            }
        }
    }
}
