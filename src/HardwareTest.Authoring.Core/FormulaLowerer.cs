using System.Globalization;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// Lowers a subset formula to a closed analyze step, or fails closed.
public static class FormulaLowerer
{
    public const double DefaultTsSeconds = 0.005;

    /// mean(x) + threshold → MeanGte. Top-level filter/filtfilt → TransferFunctionAlgorithm.
    public static MetricSource Lower(
        ExpressionAlgorithm expr,
        LimitSpec? limits,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series = null)
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
                    ResolveTsSeconds(filter.Channel, series),
                    filter.Method);
            }
            catch (InvalidOperationException ex)
            {
                var code = ex.Message.StartsWith(AuthoringCompileCodes.TfDenLeadingZero, StringComparison.Ordinal)
                    ? AuthoringCompileCodes.TfDenLeadingZero
                    : AuthoringCompileCodes.TfGrid;
                var message = ex.Message.StartsWith(code, StringComparison.Ordinal)
                    ? ex.Message
                    : $"{code}: {ex.Message}";
                throw new AuthoringWorkspaceException(message, ex);
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

    private static double ResolveTsSeconds(
        string channel,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series)
    {
        if (series is null || !TryGetSeries(series, channel, out var input) || input.Count == 0)
        {
            return DefaultTsSeconds;
        }

        return TransferFunctionGrid.MedianTsSeconds(TransferFunctionTimeBase.ElapsedMs(input));
    }

    private static bool TryGetSeries(
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series,
        string key,
        out IReadOnlyList<StoredSample> samples)
    {
        if (series.TryGetValue(key, out samples!))
        {
            return true;
        }

        foreach (var (name, value) in series)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                samples = value;
                return true;
            }
        }

        samples = [];
        return false;
    }
}
