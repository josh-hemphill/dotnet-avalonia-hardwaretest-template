using System.Globalization;

namespace HardwareTest.Authoring;

/// Parses the MATLAB-flavored formula subset. Unknown names fail closed.
public static class FormulaParser
{
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.Ordinal)
    {
        "abs", "sqrt", "min", "max", "mean", "sum", "std", "diff", "length", "median",
        "rise_time", "inband_pct",
    };

    private static readonly HashSet<string> ReservedUnknown = new(StringComparer.Ordinal)
    {
        "filter", "filtfilt", "fft", "tf", "plot", "eval",
    };

    /// Fail closed on unknown syntax or functions (including filter / filtfilt / fft).
    public static FormulaAst Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaParse}: formula is empty.");
        }

        var tokens = Tokenize(source);
        var reader = new TokenReader(tokens);
        var expr = ParseExpression(reader);
        if (!reader.AtEnd)
        {
            throw Fail($"unexpected '{reader.Current.Text}'");
        }

        return new FormulaAst(expr);
    }

    private static FormulaExpr ParseExpression(TokenReader reader)
        => ParseAdd(reader);

    private static FormulaExpr ParseAdd(TokenReader reader)
    {
        var left = ParseMul(reader);
        while (reader.Match("+") || reader.Match("-"))
        {
            var op = reader.Previous.Text;
            left = new BinaryExpr(op, left, ParseMul(reader));
        }

        return left;
    }

    private static FormulaExpr ParseMul(TokenReader reader)
    {
        var left = ParseUnary(reader);
        while (reader.Match("*") || reader.Match("/") || reader.Match(".*") || reader.Match("./"))
        {
            var op = reader.Previous.Text;
            left = new BinaryExpr(op, left, ParseUnary(reader));
        }

        return left;
    }

    private static FormulaExpr ParseUnary(TokenReader reader)
    {
        if (reader.Match("+") || reader.Match("-"))
        {
            return new UnaryExpr(reader.Previous.Text, ParseUnary(reader));
        }

        return ParsePower(reader);
    }

    private static FormulaExpr ParsePower(TokenReader reader)
    {
        var left = ParsePrimary(reader);
        if (reader.Match(".^") || reader.Match("^"))
        {
            return new BinaryExpr(reader.Previous.Text, left, ParseUnary(reader));
        }

        return left;
    }

    private static FormulaExpr ParsePrimary(TokenReader reader)
    {
        if (reader.MatchNumber(out var number))
        {
            return new NumberExpr(number);
        }

        if (reader.MatchIdent(out var ident))
        {
            if (reader.Match("("))
            {
                return ParseCall(ident, reader);
            }

            return new IdentExpr(ident);
        }

        if (reader.Match("("))
        {
            var inner = ParseExpression(reader);
            if (!reader.Match(")"))
            {
                throw Fail("expected ')'");
            }

            return inner;
        }

        throw Fail($"unexpected '{reader.Current.Text}'");
    }

    private static CallExpr ParseCall(string name, TokenReader reader)
    {
        if (ReservedUnknown.Contains(name) || !AllowedFunctions.Contains(name))
        {
            throw Fail($"unknown function '{name}'");
        }

        var args = new List<FormulaExpr>();
        if (!reader.Check(")"))
        {
            args.Add(ParseExpression(reader));
            while (reader.Match(","))
            {
                args.Add(ParseExpression(reader));
            }
        }

        if (!reader.Match(")"))
        {
            throw Fail("expected ')' after arguments");
        }

        return new CallExpr(name, args);
    }

    private static AuthoringWorkspaceException Fail(string message)
        => new($"{AuthoringCompileCodes.FormulaParse}: {message}.");

    private static IReadOnlyList<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '.' && i + 1 < source.Length && source[i + 1] is '*' or '/' or '^')
            {
                tokens.Add(new Token(TokenKind.Op, source.Substring(i, 2)));
                i += 2;
                continue;
            }

            if (c is '+' or '-' or '*' or '/' or '^' or '(' or ')' or ',')
            {
                tokens.Add(new Token(TokenKind.Op, c.ToString()));
                i++;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                var start = i;
                i++;
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.'))
                {
                    i++;
                }

                if (i < source.Length && (source[i] is 'e' or 'E'))
                {
                    i++;
                    if (i < source.Length && source[i] is '+' or '-')
                    {
                        i++;
                    }

                    while (i < source.Length && char.IsDigit(source[i]))
                    {
                        i++;
                    }
                }

                var text = source[start..i];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw Fail($"invalid number '{text}'");
                }

                tokens.Add(new Token(TokenKind.Number, text));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < source.Length)
                {
                    var ch = source[i];
                    if (char.IsLetterOrDigit(ch) || ch == '_')
                    {
                        i++;
                        continue;
                    }

                    if (ch == '.' && i + 1 < source.Length && (char.IsLetter(source[i + 1]) || source[i + 1] == '_'))
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                tokens.Add(new Token(TokenKind.Ident, source[start..i]));
                continue;
            }

            throw Fail($"unexpected character '{c}'");
        }

        tokens.Add(new Token(TokenKind.End, string.Empty));
        return tokens;
    }

    private enum TokenKind
    {
        Number,
        Ident,
        Op,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class TokenReader
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _index;

        public TokenReader(IReadOnlyList<Token> tokens) => _tokens = tokens;

        public Token Current => _tokens[_index];
        public Token Previous => _tokens[_index - 1];
        public bool AtEnd => Current.Kind == TokenKind.End;

        public bool Check(string text)
            => Current.Kind != TokenKind.End && string.Equals(Current.Text, text, StringComparison.Ordinal);

        public bool Match(string text)
        {
            if (!Check(text))
            {
                return false;
            }

            _index++;
            return true;
        }

        public bool MatchIdent(out string ident)
        {
            ident = string.Empty;
            if (Current.Kind != TokenKind.Ident)
            {
                return false;
            }

            ident = Current.Text;
            _index++;
            return true;
        }

        public bool MatchNumber(out double value)
        {
            value = 0;
            if (Current.Kind != TokenKind.Number)
            {
                return false;
            }

            value = double.Parse(Current.Text, CultureInfo.InvariantCulture);
            _index++;
            return true;
        }
    }
}
