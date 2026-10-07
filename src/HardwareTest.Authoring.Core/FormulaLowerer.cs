using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public enum FormulaSaveOutcomeKind
{
    None,
    PacksChannelAverage,
    PacksMeanGte = PacksChannelAverage,
    PacksTransferFunction,
    PreviewOnly,
    SaveBlocked,
}

public readonly record struct FormulaSaveOutcome(FormulaSaveOutcomeKind Kind, string Message);

/// Lowers a subset formula to a closed analyze step, or fails closed.
public static class FormulaLowerer
{
    public const double DefaultTsSeconds = 0.005;

    /// mean(x) + threshold → ChannelAverage. Top-level filter/filtfilt → TransferFunctionAlgorithm.
    public static MetricSource Lower(
        ExpressionAlgorithm expr,
        LimitSpec? limits,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series = null)
    {
        ArgumentNullException.ThrowIfNull(expr);
        var ast = FormulaParser.Parse(expr.Source);
        if (ast.Root is not FilterCallExpr && FormulaExprWalk.ContainsFilter(ast.Root))
        {
            throw new AuthoringWorkspaceException(NestedFilterMessage);
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
            if (!double.IsFinite(threshold))
                throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.MissingLimits}: threshold must be finite.");
            return new AlgorithmSource(
                AuthoringFunctionIds.BasicChannelAverage,
                [ident.Name],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["InputChannel"] = ident.Name,
                });
        }

        throw new AuthoringWorkspaceException(NoClosedRecipeMessage(expr.Source));
    }

    /// One-line pack preview. Parse failures are empty so the inspector FormulaError owns them.
    public static string DescribeSave(string source, LimitSpec? limits)
        => DescribeSaveOutcome(source, limits).Message;

    public static FormulaSaveOutcome DescribeSaveOutcome(string source, LimitSpec? limits)
    {
        if (!FormulaParser.TryParse(source, out _, out _))
        {
            return new FormulaSaveOutcome(FormulaSaveOutcomeKind.None, string.Empty);
        }

        try
        {
            var lowered = Lower(new ExpressionAlgorithm([], source), limits);
            return lowered switch
            {
                AlgorithmSource { AlgorithmId: AuthoringFunctionIds.BasicChannelAverage }
                    => new FormulaSaveOutcome(FormulaSaveOutcomeKind.PacksMeanGte, "Will save as Channel Average."),
                TransferFunctionAlgorithm
                    => new FormulaSaveOutcome(
                        FormulaSaveOutcomeKind.PacksTransferFunction,
                        "Will save as Apply Transfer Function."),
                _ => PreviewOnlyOutcome(),
            };
        }
        catch (AuthoringWorkspaceException ex) when (IsPreviewOnlyNoLower(ex))
        {
            return PreviewOnlyOutcome();
        }
        catch (AuthoringWorkspaceException ex)
        {
            return new FormulaSaveOutcome(FormulaSaveOutcomeKind.SaveBlocked, ex.Message);
        }
    }

    private static FormulaSaveOutcome PreviewOnlyOutcome()
        => new(
            FormulaSaveOutcomeKind.PreviewOnly,
            "Preview only — at save, only mean(channel) with a threshold (Channel Average) or a top-level filter/filtfilt packs into the plan.");

    public const string NestedFilterMessage =
        $"{AuthoringCompileCodes.FormulaNoLower}: nested filter/filtfilt is not allowed.";

    public static string NoClosedRecipeMessage(string source)
        => $"{AuthoringCompileCodes.FormulaNoLower}: '{source}' does not match a closed analyze recipe.";

    private static bool IsPreviewOnlyNoLower(AuthoringWorkspaceException ex)
        => ex.Message.StartsWith($"{AuthoringCompileCodes.FormulaNoLower}: '", StringComparison.Ordinal);

    private static double ResolveTsSeconds(
        string channel,
        IReadOnlyDictionary<string, IReadOnlyList<StoredSample>>? series)
    {
        if (series is null) return DefaultTsSeconds;
        if (!TryGetSeries(series, channel, out var input) || input.Count == 0)
            throw new AuthoringWorkspaceException($"{AuthoringCompileCodes.FormulaEval}: missing series '{channel}'.");

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
