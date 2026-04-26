using System;
using System.Collections.Generic;
using System.Globalization;

namespace BigMac.Scripting;

public sealed class ScriptParser
{
    private readonly IReadOnlyList<ScriptToken> _tokens;
    private int _index;

    private ScriptParser(IReadOnlyList<ScriptToken> tokens)
        => _tokens = tokens;

    public static ScriptProgram Parse(string source)
    {
        var tokens = new ScriptLexer(source).Tokenize();
        return new ScriptParser(tokens).ParseProgram();
    }

    private ScriptProgram ParseProgram()
    {
        var program = new ScriptProgram(ParseStatements(TokenKind.EndOfFile));
        ValidateLetBindings(program);
        return program;
    }

    private IReadOnlyList<ScriptStatement> ParseStatements(TokenKind terminator)
    {
        var statements = new List<ScriptStatement>();
        while (!Check(terminator) && !Check(TokenKind.EndOfFile))
        {
            statements.Add(ParseStatement());
        }

        return statements;
    }

    private ScriptStatement ParseStatement()
    {
        if (Match(TokenKind.Chance))
        {
            return ParseChanceStatement();
        }

        if (Match(TokenKind.If))
        {
            return ParseIfStatement();
        }

        if (Match(TokenKind.Let))
        {
            return ParseLetStatement();
        }

        if (Match(TokenKind.Wait))
        {
            return ParseWaitStatement();
        }

        if (Match(TokenKind.Loop))
        {
            return ParseLoopStatement();
        }

        if (Match(TokenKind.CommandLine, out var commandToken))
        {
            return new CommandStatement(CommandTemplate.Parse(commandToken.Lexeme, commandToken.Line, commandToken.Column));
        }

        var token = Peek();
        throw new ScriptParseException($"Unexpected token '{token.Lexeme}'.", token.Line, token.Column);
    }

    private ScriptStatement ParseChanceStatement()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after chance.");
        var chance = ParseChanceCondition();
        Consume(TokenKind.RightParen, "Expected ')' after chance condition.");
        var thenBlock = ParseBlock();
        var elseBlock = Match(TokenKind.Else) ? ParseBlock() : null;
        return new ChanceStatement(chance, thenBlock, elseBlock);
    }

    private ScriptStatement ParseIfStatement()
    {
        var conditionToken = Consume(TokenKind.ConditionText, "Expected condition after if.");

        ExpressionNode condition;
        try
        {
            condition = ExpressionParser.Parse(conditionToken.Lexeme);
            ScriptConditionEvaluator.Validate(condition);
        }
        catch (ExpressionParseException exception)
        {
            throw new ScriptParseException(exception.Message, conditionToken.Line, conditionToken.Column);
        }
        catch (InvalidOperationException exception)
        {
            throw new ScriptParseException(exception.Message, conditionToken.Line, conditionToken.Column);
        }

        var thenBlock = ParseBlock();
        var elseBlock = Match(TokenKind.Else) ? ParseBlock() : null;
        return new IfStatement(condition, thenBlock, elseBlock);
    }

    private LetStatement ParseLetStatement()
    {
        var bindingToken = Consume(TokenKind.LetText, "Expected a binding after let.");
        var assignmentIndex = FindAssignmentOperator(bindingToken.Lexeme);
        if (assignmentIndex < 0)
        {
            throw new ScriptParseException("let statements must use the form 'let name = expression'.", bindingToken.Line, bindingToken.Column);
        }

        var name = bindingToken.Lexeme[..assignmentIndex].Trim();
        if (!IsValidIdentifier(name))
        {
            throw new ScriptParseException($"'{name}' is not a valid binding name.", bindingToken.Line, bindingToken.Column);
        }

        var expressionText = bindingToken.Lexeme[(assignmentIndex + 1)..].Trim();
        if (expressionText.Length == 0)
        {
            throw new ScriptParseException("let statements require an expression after '='.", bindingToken.Line, bindingToken.Column);
        }

        try
        {
            return new LetStatement(name, ExpressionParser.Parse(expressionText), bindingToken.Line, bindingToken.Column);
        }
        catch (ExpressionParseException exception)
        {
            throw new ScriptParseException(exception.Message, bindingToken.Line, bindingToken.Column);
        }
    }

    private WaitStatement ParseWaitStatement()
    {
        var waitToken = Consume(TokenKind.WaitText, "Expected wait duration.");
        if (!double.TryParse(waitToken.Lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds))
        {
            throw new ScriptParseException("wait(...) requires a numeric duration in seconds.", waitToken.Line, waitToken.Column);
        }

        if (seconds < 0)
        {
            throw new ScriptParseException("wait(...) duration must be zero or greater.", waitToken.Line, waitToken.Column);
        }

        if (seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new ScriptParseException("wait(...) duration is too large.", waitToken.Line, waitToken.Column);
        }

        return new WaitStatement(seconds);
    }

    private LoopStatement ParseLoopStatement()
        => new(ParseBlock());

    private ChanceCondition ParseChanceCondition()
    {
        var firstNumber = Consume(TokenKind.Number, "Expected a number inside chance(...).");
        var left = ParseInt(firstNumber);

        if (!Match(TokenKind.In))
        {
            if (left is < 0 or > 100)
            {
                throw new ScriptParseException("Percentage chances must be between 0 and 100.", firstNumber.Line, firstNumber.Column);
            }

            return new ChanceCondition(ChanceConditionKind.Percent, left, 100);
        }

        var secondNumber = Consume(TokenKind.Number, "Expected a second number after 'in'.");
        var right = ParseInt(secondNumber);
        if (left <= 0 || right <= 0 || left > right)
        {
            throw new ScriptParseException("Ratio chances must be positive, and the first number may not exceed the second.", firstNumber.Line, firstNumber.Column);
        }

        return new ChanceCondition(ChanceConditionKind.Ratio, left, right);
    }

    private ScriptBlock ParseBlock()
    {
        Consume(TokenKind.LeftBrace, "Expected '{' to start the block.");
        var statements = ParseStatements(TokenKind.RightBrace);
        Consume(TokenKind.RightBrace, "Expected '}' to close the block.");
        return new ScriptBlock(statements);
    }

    private int ParseInt(ScriptToken token)
        => int.TryParse(token.Lexeme, out var value)
            ? value
            : throw new ScriptParseException($"Could not parse '{token.Lexeme}' as a number.", token.Line, token.Column);

    private ScriptToken Consume(TokenKind kind, string message)
    {
        if (!Check(kind))
        {
            var token = Peek();
            throw new ScriptParseException(message, token.Line, token.Column);
        }

        return Advance();
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

    private bool Match(TokenKind kind, out ScriptToken token)
    {
        token = Peek();
        if (!Check(kind))
        {
            return false;
        }

        token = Advance();
        return true;
    }

    private bool Check(TokenKind kind)
        => Peek().Kind == kind;

    private ScriptToken Advance()
        => _tokens[_index++];

    private ScriptToken Peek()
        => _tokens[_index];

    private static int FindAssignmentOperator(string bindingText)
    {
        var parenDepth = 0;
        var bracketDepth = 0;
        var inString = false;
        var quote = '\0';
        var escape = false;

        for (var index = 0; index < bindingText.Length; index++)
        {
            var current = bindingText[index];
            if (inString)
            {
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
                quote = current;
                inString = true;
                continue;
            }

            switch (current)
            {
                case '(':
                    parenDepth++;
                    continue;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    continue;
            }

            if (parenDepth != 0 || bracketDepth != 0 || current != '=')
            {
                continue;
            }

            var previous = index > 0 ? bindingText[index - 1] : '\0';
            var next = index + 1 < bindingText.Length ? bindingText[index + 1] : '\0';
            if (next == '=' || previous is '!' or '<' or '>' or '=')
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            if (!char.IsLetterOrDigit(name[index]) && name[index] != '_')
            {
                return false;
            }
        }

        return !string.Equals(name, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateLetBindings(ScriptProgram program)
        => ValidateScope(program.Statements);

    private static void ValidateScope(IReadOnlyList<ScriptStatement> statements)
    {
        var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement binding:
                    ValidateLetBinding(binding, bindings);
                    break;
                case ChanceStatement chance:
                    ValidateScope(chance.ThenBlock.Statements);
                    if (chance.ElseBlock is not null)
                    {
                        ValidateScope(chance.ElseBlock.Statements);
                    }

                    break;
                case IfStatement conditional:
                    ValidateScope(conditional.ThenBlock.Statements);
                    if (conditional.ElseBlock is not null)
                    {
                        ValidateScope(conditional.ElseBlock.Statements);
                    }

                    break;
                case LoopStatement loop:
                    ValidateScope(loop.Block.Statements);
                    break;
            }
        }
    }

    private static void ValidateLetBinding(LetStatement binding, ISet<string> bindings)
    {
        if (ExpressionVariableDescriber.IsBuiltInVariableName(binding.Name))
        {
            throw new ScriptParseException($"'{binding.Name}' is a built-in variable name and cannot be rebound.", binding.Line, binding.Column);
        }

        if (!bindings.Add(binding.Name))
        {
            throw new ScriptParseException($"'{binding.Name}' is already declared in this block.", binding.Line, binding.Column);
        }
    }
}
