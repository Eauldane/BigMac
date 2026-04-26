using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BigMac.Scripting;

public sealed record ScriptProgram(IReadOnlyList<ScriptStatement> Statements);

public abstract record ScriptStatement;

public sealed record CommandStatement(CommandTemplate Template) : ScriptStatement;

public sealed record LetStatement(string Name, ExpressionNode Value, int Line, int Column) : ScriptStatement;

public sealed record WaitStatement(double Seconds) : ScriptStatement;

public sealed record ChanceStatement(ChanceCondition Chance, ScriptBlock ThenBlock, ScriptBlock? ElseBlock) : ScriptStatement;

public sealed record IfStatement(ExpressionNode Condition, ScriptBlock ThenBlock, ScriptBlock? ElseBlock) : ScriptStatement;

public sealed record LoopStatement(ScriptBlock Block) : ScriptStatement;

public sealed record ScriptBlock(IReadOnlyList<ScriptStatement> Statements);

public enum ChanceConditionKind
{
    Percent,
    Ratio,
}

public readonly record struct ChanceCondition(ChanceConditionKind Kind, int Left, int Right)
{
    public double Probability
        => Kind switch
        {
            ChanceConditionKind.Percent => Left / 100d,
            ChanceConditionKind.Ratio => Right == 0 ? 0d : (double)Left / Right,
            _ => 0d,
        };

    public string DisplayText
        => Kind switch
        {
            ChanceConditionKind.Percent => $"{Left.ToString(CultureInfo.InvariantCulture)}%",
            ChanceConditionKind.Ratio => $"{Left.ToString(CultureInfo.InvariantCulture)} in {Right.ToString("N0", CultureInfo.InvariantCulture)}",
            _ => "0%",
        };
}

public enum TokenKind
{
    Chance,
    If,
    Else,
    Let,
    Wait,
    Loop,
    In,
    Number,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    ConditionText,
    LetText,
    WaitText,
    CommandLine,
    EndOfFile,
}

public readonly record struct ScriptToken(TokenKind Kind, string Lexeme, int Line, int Column);

public sealed class ScriptParseException : Exception
{
    public ScriptParseException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

public static class ScriptCompiler
{
    public static bool TryParse(string source, [NotNullWhen(true)] out ScriptProgram? program, out ScriptParseException? error)
    {
        try
        {
            program = ScriptParser.Parse(source);
            error = null;
            return true;
        }
        catch (ScriptParseException parseException)
        {
            program = null;
            error = parseException;
            return false;
        }
    }
}

public static class ScriptStructureInspector
{
    public static bool ContainsLoop(ScriptProgram program)
        => ContainsLoop(program.Statements);

    private static bool ContainsLoop(IReadOnlyList<ScriptStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is LoopStatement)
            {
                return true;
            }

            if (statement is ChanceStatement chance
             && (ContainsLoop(chance.ThenBlock.Statements)
              || chance.ElseBlock is not null && ContainsLoop(chance.ElseBlock.Statements)))
            {
                return true;
            }

            if (statement is IfStatement conditional
             && (ContainsLoop(conditional.ThenBlock.Statements)
              || conditional.ElseBlock is not null && ContainsLoop(conditional.ElseBlock.Statements)))
            {
                return true;
            }
        }

        return false;
    }
}
