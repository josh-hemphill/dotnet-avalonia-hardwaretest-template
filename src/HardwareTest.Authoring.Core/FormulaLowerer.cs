using System.Globalization;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// Lowers a subset formula to a closed analyze step, or fails closed.
public static class FormulaLowerer
{
    public const double DefaultTsSeconds = 0.005;

    /// mean(x) + threshold → MeanGte. Top-level filter/filtfilt → TransferFunctionAlgorithm.
    public static MetricSource Lower(ExpressionAlgorithm expr, LimitSpec? limits)
    {
        ArgumentNullException.ThrowIfNull(expr);
        var ast = FormulaParser.Parse(expr.Source);
        if (ast.Root is not FilterCallExpr && FormulaExprWalk.ContainsFilter(ast.Root))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.FormulaNoLower}: nested filter/filtfilt is not allowed.");
        }

        if (ast.Root is FilterCallExpr filter)
        {
            try
            {
                var (num, den) = TransferFunctionFilter.Normalize(filter.Numerator, filter.Denominator);
                return new TransferFunctionAlgorithm(
                    filter.Channel,
                    num,
                    den,
                    DefaultTsSeconds,
                    filter.Method);
            }
            catch (InvalidOperationException ex)
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringCompileCodes.TfDenLeadingZero}: {ex.Message}",
                    ex);
            }
        }

        if (ast.Root is CallExpr { Name: "mean", Args: [IdentExpr ident] })
        {
            var threshold = limits?.Threshold
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
