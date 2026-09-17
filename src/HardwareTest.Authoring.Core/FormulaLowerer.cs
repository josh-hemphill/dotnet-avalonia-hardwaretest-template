using System.Globalization;

namespace HardwareTest.Authoring;

/// Lowers a subset formula to a closed analyze step, or fails closed.
public static class FormulaLowerer
{
    /// mean(x) + scalar threshold → MeanGte. Other formulas do not emit Expressions in this area.
    public static MetricSource Lower(ExpressionAlgorithm expr, LimitSpec? limits)
    {
        ArgumentNullException.ThrowIfNull(expr);
        var ast = FormulaParser.Parse(expr.Source);
        if (ast.Root is CallExpr { Name: "mean", Args: [IdentExpr ident] })
        {
            var threshold = limits?.Threshold
                            ?? limits?.Low
                            ?? throw new AuthoringWorkspaceException(
                                $"{AuthoringCompileCodes.MissingLimits}: mean() requires a scalar LimitSpec threshold.");
            return new AlgorithmSource(
                AuthoringFunctionIds.BasicMeanGte,
                [ident.Name],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SampleCount"] = "8",
                    ["Threshold"] = threshold.ToString(CultureInfo.InvariantCulture),
                });
        }

        throw new AuthoringWorkspaceException(
            $"{AuthoringCompileCodes.FormulaNoLower}: '{expr.Source}' does not match a closed analyze recipe.");
    }
}
