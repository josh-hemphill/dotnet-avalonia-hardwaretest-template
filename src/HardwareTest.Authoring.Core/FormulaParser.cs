using Parlot.Fluent;

using static Parlot.Fluent.Parsers;

namespace HardwareTest.Authoring;

/// Parses the MATLAB-flavored formula subset. Unknown names fail closed.
public static class FormulaParser
{
    private static readonly HashSet<string> AllowedFunctions = new(FormulaCatalog.AllowedFunctions, StringComparer.Ordinal);
    private static readonly HashSet<string> ReservedUnknown = new(FormulaCatalog.ReservedUnknown, StringComparer.OrdinalIgnoreCase);
    private static readonly Parser<FormulaExpr> ExpressionParser = BuildParser().Compile();

    /// Fail closed on unknown syntax or functions (including fft). filter/filtfilt are registered.
    public static FormulaAst Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.FormulaParse}: formula is empty.");
        }

        source = source.Trim();

        try
        {
            if (!ExpressionParser.TryParse(source, out var expression, out var error))
            {
                throw Fail($"invalid syntax at position {error?.Position.Offset ?? source.Length}");
            }

            return new FormulaAst(expression);
        }
        catch (AuthoringWorkspaceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Fail(ex.Message);
        }
    }

    /// Parse without throwing. On failure, `errorMessage` is the FORMULA_PARSE text.
    public static bool TryParse(string source, out FormulaAst? ast, out string? errorMessage)
    {
        errorMessage = null;
        ast = null;
        try
        {
            ast = Parse(source);
            return true;
        }
        catch (AuthoringWorkspaceException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static Parser<FormulaExpr> BuildParser()
    {
        var expression = Deferred<FormulaExpr>();
        var unary = Deferred<FormulaExpr>();

        var segment = Literals.Identifier(
            static c => char.IsLetter(c) || c == '_',
            static c => char.IsLetterOrDigit(c) || c == '_');
        var identifier = SkipWhiteSpace(segment
            .And(ZeroOrMany(Literals.Char('.').SkipAnd(segment)))
            .Then(static value => string.Join('.', new[] { value.Item1.ToString() }.Concat(value.Item2.Select(x => x.ToString())))));

        const NumberOptions UnsignedFloat = NumberOptions.AllowDecimalSeparator | NumberOptions.AllowExponent;
        var unsignedNumber = SkipWhiteSpace(
            Literals.Number<double>(UnsignedFloat)
                .WhenNotFollowedBy(Literals.Char('.')));
        var number = unsignedNumber
            .Then<FormulaExpr>(static value => new NumberExpr(value));

        var sign = Terms.Char('-').Then(-1d)
            .Or(Terms.Char('+').Then(1d))
            .ZeroOrOne(1d);
        var signedNumber = sign.And(unsignedNumber)
            .Then(static value => value.Item1 * value.Item2);
        var commas = ZeroOrMany(Terms.Char(','));
        var vectorValues = OneOrMany(commas.SkipAnd(signedNumber)).AndSkip(commas);
        var vector = Between(Terms.Char('['), vectorValues, Terms.Char(']'))
            .Then<FormulaExpr>(static values => new VectorExpr(values));

        var arguments = Separated(Terms.Char(','), expression)
            .ZeroOrOne(Array.Empty<FormulaExpr>());
        var call = identifier
            .AndSkip(Terms.Char('('))
            .And(arguments)
            .AndSkip(Terms.Char(')'))
            .Then(static value => BuildCall(value.Item1, value.Item2));

        var parenthesized = Between(Terms.Char('('), expression, Terms.Char(')'));
        var identExpression = identifier.Then<FormulaExpr>(static name => new IdentExpr(name));
        var primary = OneOf(call, number, identExpression, parenthesized, vector);

        var powerOperator = Terms.Text(".^").Or(Terms.Text("^"));
        var power = primary
            .And(powerOperator)
            .And(unary)
            .Then<FormulaExpr>(static value => new BinaryExpr(value.Item2, value.Item1, value.Item3))
            .Or(primary);

        var unaryOperator = Terms.Char('+').Then("+").Or(Terms.Char('-').Then("-"));
        unary.Parser = unaryOperator
            .And(unary)
            .Then<FormulaExpr>(static value => new UnaryExpr(value.Item1, value.Item2))
            .Or(power);

        var multiply = unary.LeftAssociative(
            (Terms.Text(".*"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr(".*", left, right)),
            (Terms.Text("./"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr("./", left, right)),
            (Terms.Text("*"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr("*", left, right)),
            (Terms.Text("/"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr("/", left, right)));
        var add = multiply.LeftAssociative(
            (Terms.Text("+"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr("+", left, right)),
            (Terms.Text("-"), static (FormulaExpr left, FormulaExpr right) => new BinaryExpr("-", left, right)));

        expression.Parser = add;
        return add.Eof();
    }

    private static FormulaExpr BuildCall(string name, IReadOnlyList<FormulaExpr> args)
    {
        if (ReservedUnknown.Contains(name) || !AllowedFunctions.Contains(name))
        {
            throw Fail($"unknown function '{name}'");
        }

        if (name is "filter" or "filtfilt")
        {
            return BuildFilterCall(name, args);
        }

        return new CallExpr(name, args);
    }

    private static FilterCallExpr BuildFilterCall(string method, IReadOnlyList<FormulaExpr> args)
    {
        if (args.Count != 3)
        {
            throw Fail($"{method}(b, a, channel) requires 3 arguments");
        }

        var b = VectorValues(args[0], "numerator");
        var a = VectorValues(args[1], "denominator");
        if (args[2] is not IdentExpr ident)
        {
            throw Fail($"{method} channel must be an identifier");
        }

        return new FilterCallExpr(b, a, ident.Name, method);
    }

    private static IReadOnlyList<double> VectorValues(FormulaExpr expr, string name)
        => expr switch
        {
            VectorExpr vector => vector.Values,
            NumberExpr number => [number.Value],
            _ => throw Fail($"{name} must be a numeric vector"),
        };

    private static AuthoringWorkspaceException Fail(string message)
        => new($"{AuthoringCompileCodes.FormulaParse}: {message}.");
}
