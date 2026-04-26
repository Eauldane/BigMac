using System;
using System.Collections.Generic;
using System.Text;

namespace BigMac.Scripting;

public sealed class ScriptLexer
{
    private readonly string _source;
    private readonly List<ScriptToken> _tokens = [];
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public ScriptLexer(string source)
        => _source = source ?? string.Empty;

    public IReadOnlyList<ScriptToken> Tokenize()
    {
        while (!IsAtEnd())
        {
            SkipWhitespaceAndComments();
            if (IsAtEnd())
            {
                break;
            }

            var line = _line;
            var column = _column;
            var current = Peek();
            switch (current)
            {
                case '(':
                    Advance();
                    _tokens.Add(new ScriptToken(TokenKind.LeftParen, "(", line, column));
                    break;
                case ')':
                    Advance();
                    _tokens.Add(new ScriptToken(TokenKind.RightParen, ")", line, column));
                    break;
                case '{':
                    Advance();
                    _tokens.Add(new ScriptToken(TokenKind.LeftBrace, "{", line, column));
                    break;
                case '}':
                    Advance();
                    _tokens.Add(new ScriptToken(TokenKind.RightBrace, "}", line, column));
                    break;
                case '/':
                    _tokens.Add(ReadCommandLine());
                    break;
                default:
                    if (char.IsDigit(current))
                    {
                        _tokens.Add(ReadNumber());
                        break;
                    }

                    if (char.IsLetter(current))
                    {
                        var keyword = ReadKeyword();
                        _tokens.Add(keyword);
                        if (keyword.Kind == TokenKind.If)
                        {
                            SkipWhitespaceAndComments();
                            _tokens.Add(ReadConditionText());
                        }
                        else if (keyword.Kind == TokenKind.Let)
                        {
                            SkipWhitespaceAndComments();
                            _tokens.Add(ReadLetText());
                        }
                        else if (keyword.Kind == TokenKind.Wait)
                        {
                            SkipWhitespaceAndComments();
                            _tokens.Add(ReadWaitText());
                        }

                        break;
                    }

                    throw new ScriptParseException($"Unexpected character '{current}'.", line, column);
            }
        }

        _tokens.Add(new ScriptToken(TokenKind.EndOfFile, string.Empty, _line, _column));
        return _tokens;
    }

    private ScriptToken ReadNumber()
    {
        var line = _line;
        var column = _column;
        var builder = new StringBuilder();

        while (!IsAtEnd() && char.IsDigit(Peek()))
        {
            builder.Append(Advance());
        }

        return new ScriptToken(TokenKind.Number, builder.ToString(), line, column);
    }

    private ScriptToken ReadKeyword()
    {
        var line = _line;
        var column = _column;
        var builder = new StringBuilder();

        while (!IsAtEnd() && char.IsLetter(Peek()))
        {
            builder.Append(Advance());
        }

        var word = builder.ToString();
        return word switch
        {
            "chance" => new ScriptToken(TokenKind.Chance, word, line, column),
            "if" => new ScriptToken(TokenKind.If, word, line, column),
            "else" => new ScriptToken(TokenKind.Else, word, line, column),
            "let" => new ScriptToken(TokenKind.Let, word, line, column),
            "wait" => new ScriptToken(TokenKind.Wait, word, line, column),
            "loop" => new ScriptToken(TokenKind.Loop, word, line, column),
            "in" => new ScriptToken(TokenKind.In, word, line, column),
            _ => throw new ScriptParseException($"Unexpected keyword '{word}'.", line, column),
        };
    }

    private ScriptToken ReadConditionText()
    {
        var line = _line;
        var column = _column;
        if (IsAtEnd())
        {
            throw new ScriptParseException("Expected condition after if.", line, column);
        }

        return Peek() == '('
            ? ReadParenthesizedConditionText(line, column)
            : ReadBareConditionText(line, column);
    }

    private ScriptToken ReadParenthesizedConditionText(int line, int column)
    {
        Advance();

        var builder = new StringBuilder();
        var depth = 1;
        var inString = false;
        var quote = '\0';
        var escape = false;

        while (!IsAtEnd())
        {
            var current = Advance();
            if (inString)
            {
                builder.Append(current);
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
                builder.Append(current);
                continue;
            }

            if (current == '(')
            {
                depth++;
                builder.Append(current);
                continue;
            }

            if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return new ScriptToken(TokenKind.ConditionText, builder.ToString().Trim(), line, column);
                }

                builder.Append(current);
                continue;
            }

            builder.Append(current);
        }

        throw new ScriptParseException("Unterminated if(...) condition.", line, column);
    }

    private ScriptToken ReadBareConditionText(int line, int column)
    {
        var builder = new StringBuilder();
        var inString = false;
        var quote = '\0';
        var escape = false;
        var parenDepth = 0;
        var bracketDepth = 0;

        while (!IsAtEnd())
        {
            if (!inString && parenDepth == 0 && bracketDepth == 0)
            {
                if (Peek() == '{' || IsLineBreak(Peek()) || (Peek() == '/' && PeekOrDefault(1) == '/'))
                {
                    return CreateBareConditionToken(builder, line, column);
                }
            }

            var current = Advance();
            builder.Append(current);

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
                inString = true;
                quote = current;
                continue;
            }

            switch (current)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
            }
        }

        return CreateBareConditionToken(builder, line, column);
    }

    private static ScriptToken CreateBareConditionToken(StringBuilder builder, int line, int column)
    {
        var condition = builder.ToString().Trim();
        if (condition.Length == 0)
        {
            throw new ScriptParseException("Expected condition after if.", line, column);
        }

        return new ScriptToken(TokenKind.ConditionText, condition, line, column);
    }

    private ScriptToken ReadWaitText()
    {
        var line = _line;
        var column = _column;
        if (IsAtEnd() || Peek() != '(')
        {
            throw new ScriptParseException("Expected '(' after wait.", line, column);
        }

        Advance();

        var builder = new StringBuilder();
        var depth = 1;

        while (!IsAtEnd())
        {
            var current = Advance();
            if (current == '(')
            {
                depth++;
                builder.Append(current);
                continue;
            }

            if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return new ScriptToken(TokenKind.WaitText, builder.ToString().Trim(), line, column);
                }

                builder.Append(current);
                continue;
            }

            builder.Append(current);
        }

        throw new ScriptParseException("Unterminated wait(...) statement.", line, column);
    }

    private ScriptToken ReadCommandLine()
    {
        var line = _line;
        var column = _column;
        var builder = new StringBuilder();
        var inString = false;
        var quote = '\0';
        var escape = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var interpolationDepth = 0;

        while (!IsAtEnd())
        {
            if (!inString && parenDepth == 0 && bracketDepth == 0 && interpolationDepth == 0 && IsLineBreak(Peek()))
            {
                break;
            }

            var current = Advance();
            builder.Append(current);

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
                if (current == '\'' && IsEmbeddedApostrophe(builder, PeekOrDefault()))
                {
                    continue;
                }

                inString = true;
                quote = current;
                continue;
            }

            if (current == '$' && PeekOrDefault() == '{')
            {
                builder.Append(Advance());
                interpolationDepth++;
                continue;
            }

            switch (current)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case '}':
                    if (interpolationDepth > 0)
                    {
                        interpolationDepth--;
                    }

                    break;
            }
        }

        return new ScriptToken(TokenKind.CommandLine, builder.ToString().Trim(), line, column);
    }

    private ScriptToken ReadLetText()
    {
        var line = _line;
        var column = _column;
        var builder = new StringBuilder();
        var inString = false;
        var quote = '\0';
        var escape = false;
        var parenDepth = 0;
        var bracketDepth = 0;

        while (!IsAtEnd())
        {
            if (!inString && parenDepth == 0 && bracketDepth == 0 && IsLineBreak(Peek()))
            {
                break;
            }

            var current = Advance();
            builder.Append(current);

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
                if (current == '\'' && IsEmbeddedApostrophe(builder, PeekOrDefault()))
                {
                    continue;
                }

                inString = true;
                quote = current;
                continue;
            }

            switch (current)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
            }
        }

        return new ScriptToken(TokenKind.LetText, builder.ToString().Trim(), line, column);
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd())
        {
            if (char.IsWhiteSpace(Peek()))
            {
                Advance();
                continue;
            }

            if (Peek() == '/' && PeekOrDefault(1) == '/')
            {
                while (!IsAtEnd() && !IsLineBreak(Peek()))
                {
                    Advance();
                }

                continue;
            }

            break;
        }
    }

    private char Advance()
    {
        var current = _source[_index++];
        if (current == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return current;
    }

    private char Peek()
        => _source[_index];

    private char PeekOrDefault(int offset = 0)
        => _index + offset < _source.Length ? _source[_index + offset] : '\0';

    private bool IsAtEnd()
        => _index >= _source.Length;

    private static bool IsEmbeddedApostrophe(StringBuilder builder, char next)
    {
        if (builder.Length < 2)
        {
            return false;
        }

        var previous = builder[^2];
        return char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(next);
    }

    private static bool IsLineBreak(char value)
        => value is '\r' or '\n';
}
