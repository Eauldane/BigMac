using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BigMac.Scripting;

public sealed class CommandTemplate
{
    private readonly IReadOnlyList<CommandSegment> _segments;
    private readonly ExpressionNode? _bareExpression;

    private CommandTemplate(string rawText, string commandName, IReadOnlyList<CommandSegment> segments, ExpressionNode? bareExpression)
    {
        RawText = rawText;
        CommandName = commandName;
        _segments = segments;
        _bareExpression = bareExpression;
    }

    public string RawText { get; }

    public string CommandName { get; }

    public static CommandTemplate Parse(string rawText, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new ScriptParseException("Command lines may not be empty.", line, column);
        }

        var trimmed = rawText.Trim();
        if (!trimmed.StartsWith('/'))
        {
            throw new ScriptParseException("Commands must start with '/'.", line, column);
        }

        var firstSpace = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var commandName = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var arguments = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..].Trim();

        try
        {
            if (arguments.Length == 0)
            {
                return new CommandTemplate(trimmed, commandName, [], null);
            }

            if (!arguments.Contains("${", StringComparison.Ordinal)
             && ExpressionParser.TryParse(arguments, out var bareExpression)
             && bareExpression is not VariableExpression
             && bareExpression is not StringLiteralExpression)
            {
                return new CommandTemplate(trimmed, commandName, [], bareExpression);
            }

            return new CommandTemplate(trimmed, commandName, ParseSegments(arguments), null);
        }
        catch (ExpressionParseException exception)
        {
            throw new ScriptParseException(exception.Message, line, column);
        }
    }

    public string Render(Random random, IExpressionResolver resolver)
    {
        var arguments = _bareExpression is not null
            ? ExpressionEvaluator.Evaluate(_bareExpression, random, resolver).ToText()
            : RenderSegments(_segments, random, resolver);

        return string.IsNullOrWhiteSpace(arguments)
            ? CommandName
            : $"{CommandName} {arguments}";
    }

    public string Describe()
    {
        var lowerCommand = CommandName.ToLowerInvariant();
        var arguments = DescribeArguments();

        return lowerCommand switch
        {
            "/ac" or "/action" => arguments.Length == 0 ? "cast an action" : $"cast {arguments}",
            "/p" or "/party" => arguments.Length == 0 ? "send a party message" : $"send to party: {arguments}",
            "/echo" => arguments.Length == 0 ? "echo a message" : $"echo {arguments}",
            _ => arguments.Length == 0 ? $"run {CommandName}" : $"run {CommandName} {arguments}",
        };
    }

    private string DescribeArguments()
    {
        if (_bareExpression is not null)
        {
            return ExpressionDescriber.Describe(_bareExpression);
        }

        if (_segments.Count == 0)
        {
            return string.Empty;
        }

        if (_segments.Count == 1 && _segments[0] is TextSegment textSegment)
        {
            return NormalizeWhitespace(textSegment.Text);
        }

        var builder = new StringBuilder();
        foreach (var segment in _segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    builder.Append(text.Text);
                    break;
                case ExpressionSegment expression:
                    builder.Append(ExpressionDescriber.Describe(expression.Expression));
                    break;
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static string RenderSegments(IReadOnlyList<CommandSegment> segments, Random random, IExpressionResolver resolver)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    builder.Append(text.Text);
                    break;
                case ExpressionSegment expression:
                    builder.Append(ExpressionEvaluator.Evaluate(expression.Expression, random, resolver).ToText());
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<CommandSegment> ParseSegments(string arguments)
    {
        var segments = new List<CommandSegment>();
        var literal = new StringBuilder();

        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == '$' && index + 1 < arguments.Length && arguments[index + 1] == '{')
            {
                FlushLiteral(segments, literal);
                index += 2;
                var expressionBuilder = new StringBuilder();
                var depth = 1;
                var inString = false;
                var quote = '\0';
                var escape = false;

                for (; index < arguments.Length; index++)
                {
                    var current = arguments[index];
                    if (inString)
                    {
                        expressionBuilder.Append(current);
                        if (escape)
                        {
                            escape = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escape = true;
                            continue;
                        }

                        if (current == quote)
                        {
                            inString = false;
                            quote = '\0';
                        }

                        continue;
                    }

                    if (current is '"' or '\'')
                    {
                        inString = true;
                        quote = current;
                        expressionBuilder.Append(current);
                        continue;
                    }

                    if (current == '{')
                    {
                        depth++;
                        expressionBuilder.Append(current);
                        continue;
                    }

                    if (current == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            break;
                        }

                        expressionBuilder.Append(current);
                        continue;
                    }

                    expressionBuilder.Append(current);
                }

                if (depth != 0)
                {
                    throw new ExpressionParseException("Unterminated ${...} interpolation.");
                }

                segments.Add(new ExpressionSegment(ExpressionParser.Parse(expressionBuilder.ToString())));
                continue;
            }

            literal.Append(arguments[index]);
        }

        FlushLiteral(segments, literal);
        return segments;
    }

    private static void FlushLiteral(ICollection<CommandSegment> segments, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        segments.Add(new TextSegment(literal.ToString()));
        literal.Clear();
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

public abstract record CommandSegment;

public sealed record TextSegment(string Text) : CommandSegment;

public sealed record ExpressionSegment(ExpressionNode Expression) : CommandSegment;

public abstract record ExpressionNode;

public sealed record StringLiteralExpression(string Value) : ExpressionNode;

public sealed record NumberLiteralExpression(int Value) : ExpressionNode;

public sealed record BooleanLiteralExpression(bool Value) : ExpressionNode;

public sealed record VariableExpression(string Name) : ExpressionNode;

public sealed record ArrayExpression(IReadOnlyList<ExpressionNode> Items) : ExpressionNode;

public sealed record FunctionCallExpression(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

public enum UnaryOperator
{
    Not,
}

public sealed record UnaryExpression(UnaryOperator Operator, ExpressionNode Operand) : ExpressionNode;

public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

public sealed record BinaryExpression(ExpressionNode Left, ComparisonOperator Operator, ExpressionNode Right) : ExpressionNode;

public enum LogicalOperator
{
    And,
    Or,
}

public sealed record LogicalExpression(ExpressionNode Left, LogicalOperator Operator, ExpressionNode Right) : ExpressionNode;

public sealed class ExpressionParseException : Exception
{
    public ExpressionParseException(string message)
        : base(message)
    {
    }
}

public static class ExpressionParser
{
    public static bool TryParse(string source, out ExpressionNode expression)
    {
        try
        {
            expression = Parse(source);
            return true;
        }
        catch (ExpressionParseException)
        {
            expression = null!;
            return false;
        }
    }

    public static ExpressionNode Parse(string source)
        => new Parser(source).Parse();

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        EqualEqual,
        BangEqual,
        Bang,
        AmpersandAmpersand,
        PipePipe,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        Comma,
        EndOfFile,
    }

    private readonly record struct Token(TokenKind Kind, string Lexeme);

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _index;

        public Parser(string source)
            => _tokens = Tokenize(source);

        public ExpressionNode Parse()
        {
            var expression = ParseExpression();
            Consume(TokenKind.EndOfFile, "Unexpected trailing expression content.");
            return expression;
        }

        private ExpressionNode ParseExpression()
            => ParseOr();

        private ExpressionNode ParseOr()
        {
            var expression = ParseAnd();

            while (Match(TokenKind.PipePipe))
            {
                expression = new LogicalExpression(expression, LogicalOperator.Or, ParseAnd());
            }

            return expression;
        }

        private ExpressionNode ParseAnd()
        {
            var expression = ParseComparison();

            while (Match(TokenKind.AmpersandAmpersand))
            {
                expression = new LogicalExpression(expression, LogicalOperator.And, ParseComparison());
            }

            return expression;
        }

        private ExpressionNode ParseComparison()
        {
            var expression = ParseUnary();

            while (TryMatchComparisonOperator(out var comparisonOperator))
            {
                expression = new BinaryExpression(expression, comparisonOperator, ParseUnary());
            }

            return expression;
        }

        private ExpressionNode ParseUnary()
        {
            if (Match(TokenKind.Bang))
            {
                return new UnaryExpression(UnaryOperator.Not, ParseUnary());
            }

            return ParsePrimary();
        }

        private ExpressionNode ParsePrimary()
        {
            if (Match(TokenKind.LeftParen))
            {
                var expression = ParseExpression();
                Consume(TokenKind.RightParen, "Expected ')' to close the grouped expression.");
                return expression;
            }

            if (Match(TokenKind.String, out var stringToken))
            {
                return new StringLiteralExpression(UnescapeString(stringToken.Lexeme));
            }

            if (Match(TokenKind.Number, out var numberToken))
            {
                return new NumberLiteralExpression(int.Parse(numberToken.Lexeme, CultureInfo.InvariantCulture));
            }

            if (Match(TokenKind.LeftBracket))
            {
                var items = new List<ExpressionNode>();
                if (!Check(TokenKind.RightBracket))
                {
                    do
                    {
                        items.Add(ParseExpression());
                    }
                    while (Match(TokenKind.Comma));
                }

                Consume(TokenKind.RightBracket, "Expected ']' to close the array.");
                return new ArrayExpression(items);
            }

            if (Match(TokenKind.Identifier, out var identifierToken))
            {
                if (string.Equals(identifierToken.Lexeme, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return new BooleanLiteralExpression(true);
                }

                if (string.Equals(identifierToken.Lexeme, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return new BooleanLiteralExpression(false);
                }

                if (!Match(TokenKind.LeftParen))
                {
                    return new VariableExpression(identifierToken.Lexeme);
                }

                var arguments = new List<ExpressionNode>();
                if (!Check(TokenKind.RightParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    }
                    while (Match(TokenKind.Comma));
                }

                Consume(TokenKind.RightParen, "Expected ')' to close the function call.");
                return new FunctionCallExpression(identifierToken.Lexeme, arguments);
            }

            throw new ExpressionParseException($"Unexpected token '{Peek().Lexeme}'.");
        }

        private bool TryMatchComparisonOperator(out ComparisonOperator comparisonOperator)
        {
            comparisonOperator = default;

            if (Match(TokenKind.EqualEqual))
            {
                comparisonOperator = ComparisonOperator.Equal;
                return true;
            }

            if (Match(TokenKind.BangEqual))
            {
                comparisonOperator = ComparisonOperator.NotEqual;
                return true;
            }

            if (Match(TokenKind.LessThanOrEqual))
            {
                comparisonOperator = ComparisonOperator.LessThanOrEqual;
                return true;
            }

            if (Match(TokenKind.LessThan))
            {
                comparisonOperator = ComparisonOperator.LessThan;
                return true;
            }

            if (Match(TokenKind.GreaterThanOrEqual))
            {
                comparisonOperator = ComparisonOperator.GreaterThanOrEqual;
                return true;
            }

            if (Match(TokenKind.GreaterThan))
            {
                comparisonOperator = ComparisonOperator.GreaterThan;
                return true;
            }

            return false;
        }

        private bool Match(TokenKind kind)
        {
            if (!Check(kind))
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool Match(TokenKind kind, out Token token)
        {
            token = Peek();
            if (!Check(kind))
            {
                return false;
            }

            token = Advance();
            return true;
        }

        private Token Consume(TokenKind kind, string message)
        {
            if (!Check(kind))
            {
                throw new ExpressionParseException(message);
            }

            return Advance();
        }

        private bool Check(TokenKind kind)
            => Peek().Kind == kind;

        private Token Advance()
            => _tokens[_index++];

        private Token Peek()
            => _tokens[_index];

        private static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            for (var index = 0; index < source.Length;)
            {
                var current = source[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                switch (current)
                {
                    case '&' when index + 1 < source.Length && source[index + 1] == '&':
                        tokens.Add(new Token(TokenKind.AmpersandAmpersand, "&&"));
                        index += 2;
                        continue;
                    case '|' when index + 1 < source.Length && source[index + 1] == '|':
                        tokens.Add(new Token(TokenKind.PipePipe, "||"));
                        index += 2;
                        continue;
                    case '=' when index + 1 < source.Length && source[index + 1] == '=':
                        tokens.Add(new Token(TokenKind.EqualEqual, "=="));
                        index += 2;
                        continue;
                    case '!' when index + 1 < source.Length && source[index + 1] == '=':
                        tokens.Add(new Token(TokenKind.BangEqual, "!="));
                        index += 2;
                        continue;
                    case '!':
                        tokens.Add(new Token(TokenKind.Bang, "!"));
                        index++;
                        continue;
                    case '<' when index + 1 < source.Length && source[index + 1] == '=':
                        tokens.Add(new Token(TokenKind.LessThanOrEqual, "<="));
                        index += 2;
                        continue;
                    case '>':
                        if (index + 1 < source.Length && source[index + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.GreaterThanOrEqual, ">="));
                            index += 2;
                            continue;
                        }

                        tokens.Add(new Token(TokenKind.GreaterThan, ">"));
                        index++;
                        continue;
                    case '<':
                        tokens.Add(new Token(TokenKind.LessThan, "<"));
                        index++;
                        continue;
                    case '(':
                        tokens.Add(new Token(TokenKind.LeftParen, "("));
                        index++;
                        continue;
                    case ')':
                        tokens.Add(new Token(TokenKind.RightParen, ")"));
                        index++;
                        continue;
                    case '[':
                        tokens.Add(new Token(TokenKind.LeftBracket, "["));
                        index++;
                        continue;
                    case ']':
                        tokens.Add(new Token(TokenKind.RightBracket, "]"));
                        index++;
                        continue;
                    case ',':
                        tokens.Add(new Token(TokenKind.Comma, ","));
                        index++;
                        continue;
                    case '"':
                    case '\'':
                    {
                        var quote = current;
                        var builder = new StringBuilder();
                        index++;
                        var closed = false;
                        while (index < source.Length)
                        {
                            current = source[index++];
                            if (current == '\\' && index < source.Length)
                            {
                                builder.Append('\\');
                                builder.Append(source[index++]);
                                continue;
                            }

                            if (current == quote)
                            {
                                closed = true;
                                break;
                            }

                            builder.Append(current);
                        }

                        if (!closed)
                        {
                            throw new ExpressionParseException("Unterminated string literal.");
                        }

                        tokens.Add(new Token(TokenKind.String, builder.ToString()));
                        continue;
                    }
                }

                if (char.IsDigit(current))
                {
                    var start = index;
                    while (index < source.Length && char.IsDigit(source[index]))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenKind.Number, source[start..index]));
                    continue;
                }

                if (char.IsLetter(current) || current == '_')
                {
                    var start = index;
                    while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_'))
                    {
                        index++;
                    }

                    tokens.Add(new Token(TokenKind.Identifier, source[start..index]));
                    continue;
                }

                throw new ExpressionParseException($"Unexpected character '{current}'.");
            }

            tokens.Add(new Token(TokenKind.EndOfFile, string.Empty));
            return tokens;
        }

        private static string UnescapeString(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (current == '\\' && index + 1 < value.Length)
                {
                    index++;
                    builder.Append(value[index] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        '\'' => '\'',
                        _ => value[index],
                    });
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }
    }
}

public abstract record ExpressionValue
{
    public abstract string ToText();
}

public sealed record StringExpressionValue(string Value) : ExpressionValue
{
    public override string ToText()
        => Value;
}

public sealed record NumberExpressionValue(int Value) : ExpressionValue
{
    public override string ToText()
        => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record BooleanExpressionValue(bool Value) : ExpressionValue
{
    public override string ToText()
        => Value ? "true" : "false";
}

public sealed record ArrayExpressionValue(IReadOnlyList<ExpressionValue> Items) : ExpressionValue
{
    public override string ToText()
        => string.Join(", ", Items.Select(item => item.ToText()));
}

public static class ExpressionEvaluator
{
    public static ExpressionValue Evaluate(
        ExpressionNode expression,
        Random random,
        IExpressionResolver? resolver = null,
        IScriptConditionResolver? conditionResolver = null)
        => expression switch
        {
            StringLiteralExpression stringLiteral => new StringExpressionValue(stringLiteral.Value),
            NumberLiteralExpression numberLiteral => new NumberExpressionValue(numberLiteral.Value),
            BooleanLiteralExpression booleanLiteral => new BooleanExpressionValue(booleanLiteral.Value),
            VariableExpression variable => EvaluateVariable(variable, resolver),
            ArrayExpression array => new ArrayExpressionValue(array.Items.Select(item => Evaluate(item, random, resolver, conditionResolver)).ToList()),
            UnaryExpression unary => EvaluateUnary(unary, random, resolver, conditionResolver),
            BinaryExpression binary => EvaluateBinary(binary, random, resolver, conditionResolver),
            LogicalExpression logical => EvaluateLogical(logical, random, resolver, conditionResolver),
            FunctionCallExpression function => EvaluateFunction(function, random, resolver, conditionResolver),
            _ => throw new InvalidOperationException("Unsupported expression."),
        };

    private static ExpressionValue EvaluateVariable(VariableExpression variable, IExpressionResolver? resolver)
    {
        if (resolver is null)
        {
            throw new InvalidOperationException($"Variables are not available here: '{variable.Name}'.");
        }

        if (!resolver.TryResolveVariable(variable.Name, out var value))
        {
            throw new InvalidOperationException($"Unknown variable '{variable.Name}'.");
        }

        return value;
    }

    private static ExpressionValue EvaluateFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        if (TryEvaluateBuiltInFunction(function, random, resolver, conditionResolver, out var builtInValue))
        {
            return builtInValue;
        }

        var evaluatedArguments = function.Arguments
            .Select(argument => Evaluate(argument, random, resolver, conditionResolver))
            .ToList();

        if (resolver is IExpressionFunctionResolver functionResolver
         && functionResolver.TryEvaluateFunction(function.Name, evaluatedArguments, out var value))
        {
            return value;
        }

        if (conditionResolver is not null && ScriptConditionEvaluator.TryEvaluatePredicate(function, conditionResolver, out var predicateValue))
        {
            return new BooleanExpressionValue(predicateValue);
        }

        throw new InvalidOperationException($"Unsupported function '{function.Name}'.");
    }

    private static ExpressionValue EvaluateUnary(
        UnaryExpression unary,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
        => unary.Operator switch
        {
            UnaryOperator.Not => new BooleanExpressionValue(!ToBoolean(Evaluate(unary.Operand, random, resolver, conditionResolver))),
            _ => throw new InvalidOperationException($"Unsupported unary operator '{unary.Operator}'."),
        };

    private static ExpressionValue EvaluateBinary(
        BinaryExpression binary,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        var left = Evaluate(binary.Left, random, resolver, conditionResolver);
        var right = Evaluate(binary.Right, random, resolver, conditionResolver);
        return new BooleanExpressionValue(Compare(binary.Operator, left, right));
    }

    private static ExpressionValue EvaluateLogical(
        LogicalExpression logical,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        var left = ToBoolean(Evaluate(logical.Left, random, resolver, conditionResolver));
        return logical.Operator switch
        {
            LogicalOperator.And => new BooleanExpressionValue(left && ToBoolean(Evaluate(logical.Right, random, resolver, conditionResolver))),
            LogicalOperator.Or => new BooleanExpressionValue(left || ToBoolean(Evaluate(logical.Right, random, resolver, conditionResolver))),
            _ => throw new InvalidOperationException($"Unsupported logical operator '{logical.Operator}'."),
        };
    }

    internal static bool ToBoolean(ExpressionValue value)
        => value switch
        {
            BooleanExpressionValue booleanValue => booleanValue.Value,
            NumberExpressionValue numberValue => numberValue.Value != 0,
            StringExpressionValue stringValue => !string.IsNullOrEmpty(stringValue.Value),
            ArrayExpressionValue arrayValue => arrayValue.Items.Count != 0,
            _ => throw new InvalidOperationException("Conditions must evaluate to a boolean-like value."),
        };

    private static bool TryEvaluateBuiltInFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver,
        out ExpressionValue value)
    {
        value = function.Name.ToLowerInvariant() switch
        {
            "random" => EvaluateRandomFunction(function, random, resolver, conditionResolver),
            "choose" => EvaluateChooseFunction(function, random, resolver, conditionResolver),
            "min" => EvaluateMinOrMaxFunction(function, random, resolver, conditionResolver, Math.Min),
            "max" => EvaluateMinOrMaxFunction(function, random, resolver, conditionResolver, Math.Max),
            "clamp" => EvaluateClampFunction(function, random, resolver, conditionResolver),
            "contains" => EvaluateContainsFunction(function, random, resolver, conditionResolver),
            "starts_with" => EvaluateStartsWithFunction(function, random, resolver, conditionResolver),
            "ends_with" => EvaluateEndsWithFunction(function, random, resolver, conditionResolver),
            "lower" => EvaluateCaseTransformFunction(function, random, resolver, conditionResolver, toUpper: false),
            "upper" => EvaluateCaseTransformFunction(function, random, resolver, conditionResolver, toUpper: true),
            "trim" => EvaluateTrimFunction(function, random, resolver, conditionResolver),
            "default" => EvaluateDefaultFunction(function, random, resolver, conditionResolver),
            _ => null!,
        };

        return value is not null;
    }

    private static ExpressionValue EvaluateRandomFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        var choices = EvaluateVariadicOrArrayArguments(function, random, resolver, conditionResolver);
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("random(...) requires at least one choice.");
        }

        return choices[random.Next(choices.Count)];
    }

    private static ExpressionValue EvaluateChooseFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 3);

        var condition = Evaluate(function.Arguments[0], random, resolver, conditionResolver);
        var selectedBranch = ToBoolean(condition) ? function.Arguments[1] : function.Arguments[2];
        return Evaluate(selectedBranch, random, resolver, conditionResolver);
    }

    private static ExpressionValue EvaluateMinOrMaxFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver,
        Func<int, int, int> aggregate)
    {
        var evaluatedArguments = EvaluateVariadicOrArrayArguments(function, random, resolver, conditionResolver);
        if (evaluatedArguments.Count == 0)
        {
            throw new InvalidOperationException($"{function.Name}(...) requires at least one number.");
        }

        var result = GetRequiredNumberArgument(function.Name, evaluatedArguments[0], 1);
        for (var index = 1; index < evaluatedArguments.Count; index++)
        {
            result = aggregate(result, GetRequiredNumberArgument(function.Name, evaluatedArguments[index], index + 1));
        }

        return new NumberExpressionValue(result);
    }

    private static ExpressionValue EvaluateClampFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 3);

        var value = GetRequiredNumberArgument(function.Name, Evaluate(function.Arguments[0], random, resolver, conditionResolver), 1);
        var minimum = GetRequiredNumberArgument(function.Name, Evaluate(function.Arguments[1], random, resolver, conditionResolver), 2);
        var maximum = GetRequiredNumberArgument(function.Name, Evaluate(function.Arguments[2], random, resolver, conditionResolver), 3);
        if (minimum > maximum)
        {
            throw new InvalidOperationException("clamp(...) requires the minimum to be less than or equal to the maximum.");
        }

        return new NumberExpressionValue(Math.Clamp(value, minimum, maximum));
    }

    private static ExpressionValue EvaluateContainsFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 2);

        var source = Evaluate(function.Arguments[0], random, resolver, conditionResolver).ToText();
        var value = Evaluate(function.Arguments[1], random, resolver, conditionResolver).ToText();
        return new BooleanExpressionValue(source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static ExpressionValue EvaluateStartsWithFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 2);

        var source = Evaluate(function.Arguments[0], random, resolver, conditionResolver).ToText();
        var value = Evaluate(function.Arguments[1], random, resolver, conditionResolver).ToText();
        return new BooleanExpressionValue(source.StartsWith(value, StringComparison.OrdinalIgnoreCase));
    }

    private static ExpressionValue EvaluateEndsWithFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 2);

        var source = Evaluate(function.Arguments[0], random, resolver, conditionResolver).ToText();
        var value = Evaluate(function.Arguments[1], random, resolver, conditionResolver).ToText();
        return new BooleanExpressionValue(source.EndsWith(value, StringComparison.OrdinalIgnoreCase));
    }

    private static ExpressionValue EvaluateCaseTransformFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver,
        bool toUpper)
    {
        EnsureArgumentCount(function, 1);

        var text = Evaluate(function.Arguments[0], random, resolver, conditionResolver).ToText();
        return new StringExpressionValue(toUpper ? text.ToUpperInvariant() : text.ToLowerInvariant());
    }

    private static ExpressionValue EvaluateTrimFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 1);

        var text = Evaluate(function.Arguments[0], random, resolver, conditionResolver).ToText();
        return new StringExpressionValue(text.Trim());
    }

    private static ExpressionValue EvaluateDefaultFunction(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        EnsureArgumentCount(function, 2);

        var value = Evaluate(function.Arguments[0], random, resolver, conditionResolver);
        return ToBoolean(value)
            ? value
            : Evaluate(function.Arguments[1], random, resolver, conditionResolver);
    }

    private static IReadOnlyList<ExpressionValue> EvaluateVariadicOrArrayArguments(
        FunctionCallExpression function,
        Random random,
        IExpressionResolver? resolver,
        IScriptConditionResolver? conditionResolver)
    {
        if (function.Arguments.Count != 1)
        {
            return function.Arguments
                .Select(argument => Evaluate(argument, random, resolver, conditionResolver))
                .ToList();
        }

        var evaluatedArgument = Evaluate(function.Arguments[0], random, resolver, conditionResolver);
        return evaluatedArgument is ArrayExpressionValue array
            ? array.Items
            : [evaluatedArgument];
    }

    private static int GetRequiredNumberArgument(string functionName, ExpressionValue value, int argumentIndex)
        => value is NumberExpressionValue number
            ? number.Value
            : throw new InvalidOperationException($"{functionName}(...) argument {argumentIndex.ToString(CultureInfo.InvariantCulture)} must be a number.");

    private static void EnsureArgumentCount(FunctionCallExpression function, int expectedCount)
    {
        if (function.Arguments.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"{function.Name}(...) requires exactly {expectedCount.ToString(CultureInfo.InvariantCulture)} argument{(expectedCount == 1 ? string.Empty : "s")}.");
        }
    }

    private static bool Compare(ComparisonOperator comparisonOperator, ExpressionValue left, ExpressionValue right)
    {
        if (left is NumberExpressionValue leftNumber && right is NumberExpressionValue rightNumber)
        {
            return CompareValues(leftNumber.Value, rightNumber.Value, comparisonOperator);
        }

        if (left is StringExpressionValue leftString && right is StringExpressionValue rightString)
        {
            return CompareValues(
                string.Compare(leftString.Value, rightString.Value, StringComparison.OrdinalIgnoreCase),
                0,
                comparisonOperator);
        }

        if (left is BooleanExpressionValue leftBoolean && right is BooleanExpressionValue rightBoolean)
        {
            return comparisonOperator switch
            {
                ComparisonOperator.Equal => leftBoolean.Value == rightBoolean.Value,
                ComparisonOperator.NotEqual => leftBoolean.Value != rightBoolean.Value,
                _ => throw new InvalidOperationException(
                    $"Operator '{DescribeOperator(comparisonOperator)}' only supports numbers and strings."),
            };
        }

        throw new InvalidOperationException(
            $"Cannot compare {DescribeValueType(left)} to {DescribeValueType(right)}.");
    }

    private static bool CompareValues(int left, int right, ComparisonOperator comparisonOperator)
        => comparisonOperator switch
        {
            ComparisonOperator.Equal => left == right,
            ComparisonOperator.NotEqual => left != right,
            ComparisonOperator.LessThan => left < right,
            ComparisonOperator.LessThanOrEqual => left <= right,
            ComparisonOperator.GreaterThan => left > right,
            ComparisonOperator.GreaterThanOrEqual => left >= right,
            _ => throw new InvalidOperationException($"Unsupported comparison operator '{comparisonOperator}'."),
        };

    private static string DescribeValueType(ExpressionValue value)
        => value switch
        {
            StringExpressionValue => "a string",
            NumberExpressionValue => "a number",
            BooleanExpressionValue => "a boolean",
            ArrayExpressionValue => "an array",
            _ => "a value",
        };

    private static string DescribeOperator(ComparisonOperator comparisonOperator)
        => comparisonOperator switch
        {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            _ => comparisonOperator.ToString(),
        };
}

public static class ExpressionDescriber
{
    public static string Describe(ExpressionNode expression)
        => expression switch
        {
            StringLiteralExpression stringLiteral => Quote(stringLiteral.Value),
            NumberLiteralExpression numberLiteral => numberLiteral.Value.ToString(CultureInfo.InvariantCulture),
            BooleanLiteralExpression booleanLiteral => booleanLiteral.Value ? "true" : "false",
            VariableExpression variable => ExpressionVariableDescriber.Describe(variable.Name),
            ArrayExpression array => DescribeArray(array),
            FunctionCallExpression function => DescribeFunction(function),
            UnaryExpression unary => DescribeUnary(unary),
            BinaryExpression binary => DescribeBinary(binary),
            LogicalExpression logical => DescribeLogical(logical),
            _ => "an expression",
        };

    private static string DescribeArray(ArrayExpression array)
        => array.Items.Count switch
        {
            0 => "an empty list",
            1 => $"a list containing {Describe(array.Items[0])}",
            _ => $"a list of {array.Items.Count.ToString(CultureInfo.InvariantCulture)} options",
        };

    private static string DescribeFunction(FunctionCallExpression function)
    {
        switch (function.Name.ToLowerInvariant())
        {
            case "random":
                break;
            case "choose":
                return function.Arguments.Count == 3
                    ? $"{Describe(function.Arguments[1])} when {Describe(function.Arguments[0])}, otherwise {Describe(function.Arguments[2])}"
                    : "a conditional choice";
            case "min":
                return "the smallest value";
            case "max":
                return "the largest value";
            case "clamp":
                return "the clamped value";
            case "contains":
                return function.Arguments.Count == 2
                    ? $"whether {Describe(function.Arguments[0])} contains {Describe(function.Arguments[1])}"
                    : "whether one value contains another";
            case "starts_with":
                return function.Arguments.Count == 2
                    ? $"whether {Describe(function.Arguments[0])} starts with {Describe(function.Arguments[1])}"
                    : "whether one value starts with another";
            case "ends_with":
                return function.Arguments.Count == 2
                    ? $"whether {Describe(function.Arguments[0])} ends with {Describe(function.Arguments[1])}"
                    : "whether one value ends with another";
            case "lower":
                return function.Arguments.Count == 1
                    ? $"{Describe(function.Arguments[0])} in lowercase"
                    : "a lowercase value";
            case "upper":
                return function.Arguments.Count == 1
                    ? $"{Describe(function.Arguments[0])} in uppercase"
                    : "an uppercase value";
            case "trim":
                return function.Arguments.Count == 1
                    ? $"{Describe(function.Arguments[0])} without leading or trailing spaces"
                    : "trimmed text";
            case "default":
                return function.Arguments.Count == 2
                    ? $"{Describe(function.Arguments[0])}, or {Describe(function.Arguments[1])} when empty"
                    : "a fallback value";
            case "action_unlocked":
                return DescribeSingleArgumentFunction(function, "whether", "is unlocked");
            case "action_available":
                return DescribeSingleArgumentFunction(function, "whether", "is available");
            case "action_ready":
                return DescribeSingleArgumentFunction(function, "whether", "is ready");
            case "class":
            case "job":
                return DescribeSingleArgumentFunction(function, "whether the class is", null);
            case "has_status":
            case "have_status":
                return DescribeSingleArgumentFunction(function, "whether the player has", null);
            case "target_has_status":
                return DescribeSingleArgumentFunction(function, "whether the current target has", null);
            case "target_is_enemy":
                return "whether the current target is an enemy";
            case "distance_to_target":
                return "the distance to the current target";
            default:
                return $"the result of {function.Name}(...)";
        }

        var choices = FlattenRandomChoices(function);
        if (choices is null || choices.Count == 0)
        {
            return "a random choice";
        }

        var renderedChoices = choices.Select(Describe).ToList();
        var totalLength = renderedChoices.Sum(choice => choice.Length);
        if (choices.Count > 3 || totalLength > 60)
        {
            return $"a random choice from {choices.Count.ToString(CultureInfo.InvariantCulture)} options";
        }

        return $"a random choice from {JoinHumanReadable(renderedChoices)}";
    }

    private static IReadOnlyList<ExpressionNode>? FlattenRandomChoices(FunctionCallExpression function)
    {
        if (function.Arguments.Count == 1 && function.Arguments[0] is ArrayExpression array)
        {
            return array.Items;
        }

        return function.Arguments;
    }

    private static string JoinHumanReadable(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        if (values.Count == 2)
        {
            return $"{values[0]} or {values[1]}";
        }

        return $"{string.Join(", ", values.Take(values.Count - 1))}, or {values[^1]}";
    }

    private static string DescribeSingleArgumentFunction(FunctionCallExpression function, string prefix, string? suffix)
    {
        var argument = function.Arguments.Count == 1
            ? Describe(function.Arguments[0])
            : "...";
        return suffix is null
            ? $"{prefix} {argument}"
            : $"{prefix} {argument} {suffix}";
    }

    private static string DescribeUnary(UnaryExpression unary)
        => unary.Operator switch
        {
            UnaryOperator.Not => $"!{Describe(unary.Operand)}",
            _ => "an expression",
        };

    private static string DescribeBinary(BinaryExpression binary)
        => $"{Describe(binary.Left)} {DescribeOperator(binary.Operator)} {Describe(binary.Right)}";

    private static string DescribeLogical(LogicalExpression logical)
        => $"{Describe(logical.Left)} {DescribeOperator(logical.Operator)} {Describe(logical.Right)}";

    private static string DescribeOperator(ComparisonOperator comparisonOperator)
        => comparisonOperator switch
        {
            ComparisonOperator.Equal => "==",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            _ => comparisonOperator.ToString(),
        };

    private static string DescribeOperator(LogicalOperator logicalOperator)
        => logicalOperator switch
        {
            LogicalOperator.And => "&&",
            LogicalOperator.Or => "||",
            _ => logicalOperator.ToString(),
        };

    private static string Quote(string value)
        => $"\"{value}\"";
}
