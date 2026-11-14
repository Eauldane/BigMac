using System;

namespace BigMac.Scripting;

public enum ScriptColour
{
    Text,
    Keyword,
    Function,
    Variable,
    String,
    Number,
    Boolean,
    Comment,
    Command,
    Target,
    Punctuation,
    Interpolation,
}

public readonly record struct ScriptHighlightState(
    bool Command = false,
    int Parentheses = 0,
    int Brackets = 0,
    char Quote = '\0',
    bool Escape = false,
    int InterpolationDepth = 0,
    char CommandQuote = '\0');

public static class ScriptHighlighter
{
    public static ScriptHighlightState HighlightLine(ReadOnlySpan<char> line, Span<ScriptColour> colours,
        ScriptHighlightState state = default)
    {
        if (colours.Length != line.Length)
        {
            throw new ArgumentException("One colour is required for each UTF-16 character.", nameof(colours));
        }

        colours.Clear();
        var command = state.Command;
        var parentheses = state.Parentheses;
        var brackets = state.Brackets;
        var quote = state.Quote;
        var escape = state.Escape;
        var interpolation = state.InterpolationDepth;
        var commandQuote = state.CommandQuote;

        for (var index = 0; index < line.Length;)
        {
            var current = line[index];
            var next = index + 1 < line.Length ? line[index + 1] : '\0';

            if (command && interpolation == 0 && current == '$' && next == '{')
            {
                colours.Slice(index, 2).Fill(ScriptColour.Interpolation);
                commandQuote = quote;
                quote = '\0';
                escape = false;
                interpolation = 1;
                index += 2;
                continue;
            }

            if (quote != '\0')
            {
                colours[index++] = ScriptColour.String;
                if (escape)
                    escape = false;
                else if (current == '\\')
                    escape = true;
                else if (current == quote)
                    quote = '\0';
                continue;
            }

            if (!command && current == '/' && next == '/')
            {
                colours[index..].Fill(ScriptColour.Comment);
                break;
            }

            if (!command && current == '/')
            {
                command = true;
                var start = index++;
                while (index < line.Length && !char.IsWhiteSpace(line[index]))
                    index++;
                colours[start..index].Fill(ScriptColour.Command);
                continue;
            }

            if (current is '"' or '\'' && !(command && interpolation == 0 && current == '\''
                && index > 0 && char.IsLetterOrDigit(line[index - 1]) && char.IsLetterOrDigit(next)))
            {
                quote = current;
                colours[index++] = ScriptColour.String;
                continue;
            }

            if (command && interpolation == 0)
            {
                if (current == '<')
                {
                    var end = index + 1;
                    while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '>')
                        end++;
                    if (end < line.Length && line[end] == '>')
                    {
                        colours.Slice(index, end - index + 1).Fill(ScriptColour.Target);
                        index = end + 1;
                        continue;
                    }
                }

                // Command arguments may be a multiline expression (e.g. random([...])).
                if (current == '(') parentheses++;
                if (current == ')') parentheses = Math.Max(0, parentheses - 1);
                if (current == '[') brackets++;
                if (current == ']') brackets = Math.Max(0, brackets - 1);
                // Only colour expression-like command arguments; ordinary chat text stays neutral.
                if (parentheses == 0 && brackets == 0 && current is not '(' and not '[' and not ')' and not ']')
                {
                    var start = index;
                    if (char.IsLetter(current) || current == '_')
                    {
                        while (index < line.Length && (char.IsLetterOrDigit(line[index]) || line[index] == '_'))
                            index++;
                        var following = index;
                        while (following < line.Length && char.IsWhiteSpace(line[following])) following++;
                        if (following < line.Length && line[following] == '(')
                            colours[start..index].Fill(ScriptColour.Function);
                        continue;
                    }
                    index++;
                    continue;
                }
            }

            if (interpolation > 0 && current == '}')
            {
                colours[index++] = ScriptColour.Interpolation;
                if (--interpolation == 0)
                    quote = commandQuote;
                continue;
            }
            if (interpolation > 0 && current == '{')
                interpolation++;

            if (char.IsLetter(current) || current == '_')
            {
                var start = index++;
                while (index < line.Length && (char.IsLetterOrDigit(line[index]) || line[index] == '_'))
                    index++;
                var identifier = line[start..index].ToString();
                var following = index;
                while (following < line.Length && char.IsWhiteSpace(line[following])) following++;
                var colour = identifier switch
                {
                    "if" or "else" or "chance" or "in" or "let" or "loop" or "repeat" or "wait" => ScriptColour.Keyword,
                    _ when string.Equals(identifier, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(identifier, "false", StringComparison.OrdinalIgnoreCase) => ScriptColour.Boolean,
                    _ when following < line.Length && line[following] == '(' => ScriptColour.Function,
                    _ => ScriptColour.Variable,
                };
                colours[start..index].Fill(colour);
                continue;
            }

            if (char.IsDigit(current))
            {
                var start = index++;
                while (index < line.Length && (char.IsDigit(line[index]) || line[index] == '.')) index++;
                colours[start..index].Fill(ScriptColour.Number);
                continue;
            }

            if (!char.IsWhiteSpace(current))
                colours[index] = ScriptColour.Punctuation;
            index++;
        }

        if (command && quote == '\0' && interpolation == 0 && parentheses == 0 && brackets == 0)
            command = false;
        return new ScriptHighlightState(command, parentheses, brackets, quote, escape, interpolation, commandQuote);
    }
}
