using BigMac.Scripting;
using Xunit;

namespace BigMac.Tests;

public sealed class ScriptHighlighterTests
{
    [Theory]
    [InlineData("repeat(3) {", "repeat", ScriptColour.Keyword)]
    [InlineData("wait(1.5)", "1.5", ScriptColour.Number)]
    [InlineData("let greeting = upper(arg(1))", "greeting", ScriptColour.Variable)]
    [InlineData("let greeting = upper(arg(1))", "upper", ScriptColour.Function)]
    [InlineData("if (arg_count >= 2 && true) {", "arg_count", ScriptColour.Variable)]
    [InlineData("if (arg_count >= 2 && true) {", "true", ScriptColour.Boolean)]
    [InlineData("if (action_ready(\"Rescue\")) {", "Rescue", ScriptColour.String)]
    [InlineData("// repeat(3) { /echo hello", "repeat", ScriptColour.Comment)]
    [InlineData("repeat(3) { // hello", "hello", ScriptColour.Comment)]
    [InlineData("/ac \"Cure II\" <2>", "/ac", ScriptColour.Command)]
    [InlineData("/ac \"Cure II\" <2>", "Cure II", ScriptColour.String)]
    [InlineData("/ac \"Cure II\" <2>", "<2>", ScriptColour.Target)]
    [InlineData("/echo ${upper(arg(1))}", "arg", ScriptColour.Function)]
    [InlineData("/echo ${upper(arg(1))}", "${", ScriptColour.Interpolation)]
    [InlineData("/echo random([\"a\", \"b\"])", "random", ScriptColour.Function)]
    [InlineData("/echo random([\"a\", \"b\"])", "b", ScriptColour.String)]
    public void ColoursLanguageConstructs(string line, string token, ScriptColour expected)
    {
        var (colours, _) = Scan(line);
        var start = line.IndexOf(token, StringComparison.Ordinal);
        Assert.All(colours.Skip(start).Take(token.Length), actual => Assert.Equal(expected, actual));
    }

    [Theory]
    [InlineData("/echo http://example.com", "example")]
    [InlineData("/echo hello // literal chat text", "literal")]
    [InlineData("/echo don't panic", "panic")]
    public void CommandTextDoesNotBecomeCommentsOrUnclosedStrings(string line, string token)
    {
        var (colours, state) = Scan(line);
        Assert.Equal(ScriptColour.Text, colours[line.IndexOf(token, StringComparison.Ordinal)]);
        Assert.False(state.Command);
        Assert.Equal('\0', state.Quote);
    }

    [Fact]
    public void CommentsInsideStringsAreLiteral()
    {
        var (colours, _) = Scan("let url = \"https://example.com\"");
        Assert.Equal(ScriptColour.String, colours[17]);
    }

    [Fact]
    public void EscapedQuotesDoNotEndStrings()
    {
        const string line = "let text = \"say \\\"hello\\\" // still string\"";
        var (colours, state) = Scan(line);
        Assert.Equal(ScriptColour.String, colours[line.IndexOf("still", StringComparison.Ordinal)]);
        Assert.Equal('\0', state.Quote);
    }

    [Fact]
    public void InterpolationInsideCommandQuotesRestoresOuterString()
    {
        const string line = "/echo \"Hello ${choose(true, \"friend\", player_name)}!\"";
        var (colours, state) = Scan(line);
        Assert.Equal(ScriptColour.Function, colours[line.IndexOf("choose", StringComparison.Ordinal)]);
        Assert.Equal(ScriptColour.Variable, colours[line.IndexOf("player_name", StringComparison.Ordinal)]);
        Assert.Equal(ScriptColour.String, colours[line.IndexOf('!')]);
        Assert.False(state.Command);
    }

    [Fact]
    public void MultilineCommandAndInterpolationCarryStateThenResumeScript()
    {
        var (_, first) = Scan("/echo random([");
        Assert.True(first.Command);
        var (secondColours, second) = Scan("    \"hello\",", first);
        Assert.Equal(ScriptColour.String, secondColours[5]);
        var (_, third) = Scan("])", second);
        Assert.False(third.Command);
        var (fourthColours, _) = Scan("repeat(2) {", third);
        Assert.Equal(ScriptColour.Keyword, fourthColours[0]);

        var (_, expression) = Scan("/echo ${choose(");
        var (expressionColours, end) = Scan("true, \"hi\", \"bye\")}", expression);
        Assert.Equal(ScriptColour.Boolean, expressionColours[0]);
        Assert.False(end.Command);
    }

    [Fact]
    public void MultilineStringsCarryState()
    {
        var (_, state) = Scan("let text = \"unfinished");
        var (colours, end) = Scan("// still string\"", state);
        Assert.All(colours, colour => Assert.Equal(ScriptColour.String, colour));
        Assert.Equal('\0', end.Quote);
    }

    [Theory]
    [InlineData("")]
    [InlineData("repeat(")]
    [InlineData("/echo ${")]
    [InlineData("let = \"unfinished")]
    [InlineData("😀 日本語 é ")]
    public void IncompleteAndUnicodeTextIsPreserved(string line)
    {
        var (colours, _) = Scan(line);
        Assert.Equal(line.Length, colours.Length);
    }

    private static (ScriptColour[] Colours, ScriptHighlightState State) Scan(string line, ScriptHighlightState state = default)
    {
        var colours = new ScriptColour[line.Length];
        var next = ScriptHighlighter.HighlightLine(line.AsSpan(), colours.AsSpan(), state);
        return (colours, next);
    }
}
