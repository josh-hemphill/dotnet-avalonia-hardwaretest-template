namespace HardwareTest.Authoring;

/// MATLAB-flavored subset AST. Not MATLAB Runtime.
public sealed record FormulaAst(FormulaExpr Root);

public abstract record FormulaExpr;

public sealed record NumberExpr(double Value) : FormulaExpr;

public sealed record IdentExpr(string Name) : FormulaExpr;

public sealed record UnaryExpr(string Op, FormulaExpr Operand) : FormulaExpr;

public sealed record BinaryExpr(string Op, FormulaExpr Left, FormulaExpr Right) : FormulaExpr;

public sealed record CallExpr(string Name, IReadOnlyList<FormulaExpr> Args) : FormulaExpr;

public static class FormulaExprWalk
{
    /// Channel identifiers referenced by the AST (not function names).
    public static IReadOnlyList<string> Identifiers(FormulaExpr expr)
    {
        var names = new List<string>();
        Collect(expr, names);
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Collect(FormulaExpr expr, List<string> names)
    {
        switch (expr)
        {
            case IdentExpr ident:
                names.Add(ident.Name);
                break;
            case UnaryExpr unary:
                Collect(unary.Operand, names);
                break;
            case BinaryExpr binary:
                Collect(binary.Left, names);
                Collect(binary.Right, names);
                break;
            case CallExpr call:
                foreach (var arg in call.Args)
                {
                    Collect(arg, names);
                }

                break;
        }
    }
}
