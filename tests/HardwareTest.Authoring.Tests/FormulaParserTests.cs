using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class FormulaParserTests
{
    [Fact]
    public void Mean_of_identifier_parses()
    {
        var ast = FormulaParser.Parse("mean(VDC)");
        var call = Assert.IsType<CallExpr>(ast.Root);
        Assert.Equal("mean", call.Name);
        var ident = Assert.IsType<IdentExpr>(Assert.Single(call.Args));
        Assert.Equal("VDC", ident.Name);
    }

    [Theory]
    [InlineData("fft(VDC)")]
    [InlineData("FFT(VDC)")]
    [InlineData("tf(VDC)")]
    [InlineData("plot(VDC)")]
    [InlineData("eval(VDC)")]
    public void Unknown_functions_fail_parse(string source)
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => FormulaParser.Parse(source));
        Assert.Contains(AuthoringCompileCodes.FormulaParse, ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown function", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("filter(VDC)")]
    [InlineData("filtfilt(VDC)")]
    public void Filter_arity_mismatch_fails_parse(string source)
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => FormulaParser.Parse(source));
        Assert.Contains(AuthoringCompileCodes.FormulaParse, ex.Message, StringComparison.Ordinal);
        Assert.Contains("requires 3 arguments", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_call_parses_numeric_vectors_and_channel()
    {
        var ast = FormulaParser.Parse("filter([0.5 0.5],[1 -0.5],VDC)");
        var call = Assert.IsType<FilterCallExpr>(ast.Root);
        Assert.Equal("filter", call.Method);
        Assert.Equal([0.5, 0.5], call.Numerator);
        Assert.Equal([1d, -0.5], call.Denominator);
        Assert.Equal("VDC", call.Channel);
    }

    [Fact]
    public void Evaluator_mean_of_two_point_series_matches()
    {
        var ast = FormulaParser.Parse("mean(VDC)");
        var series = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase)
        {
            ["VDC"] =
            [
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 1 },
                new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 3 },
            ],
        };
        Assert.Equal(2, FormulaEvaluator.Evaluate(ast, series));
    }

    [Theory]
    [InlineData("mean()")]
    [InlineData("abs()")]
    public void Evaluator_empty_args_fail_closed(string source)
    {
        var ast = FormulaParser.Parse(source);
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => FormulaEvaluator.Evaluate(ast, new Dictionary<string, IReadOnlyList<StoredSample>>()));
        Assert.Contains(AuthoringCompileCodes.FormulaEval, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_mean_empty_args_does_not_throw()
    {
        var metric = new MetricDraft(
            "Formula",
            "VDC.mean",
            PresentationRoles.Scalar,
            "V",
            new LimitSpec(null, null, 1.2),
            null,
            new ExpressionAlgorithm([], "mean()"));
        var preview = MetricPreviewBuilder.From(metric);
        Assert.Empty(preview.CannedSamples);
    }

    [Fact]
    public void Lower_mean_without_threshold_fails_missing_limits()
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(
            () => FormulaLowerer.Lower(
                new ExpressionAlgorithm(["VDC"], "mean(VDC)"),
                new LimitSpec(1.0, null, null)));
        Assert.Contains(AuthoringCompileCodes.MissingLimits, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Identifiers_walk_collects_channel_names()
    {
        var ast = FormulaParser.Parse("mean(rail.mean) + abs(VDC)");
        Assert.Equal(
            ["rail.mean", "VDC"],
            FormulaExprWalk.Identifiers(ast.Root));
    }

    [Fact]
    public void TryParse_reports_parse_errors_without_throwing()
    {
        Assert.True(FormulaParser.TryParse("mean(VDC.mean)", out var ast, out var okError));
        Assert.NotNull(ast);
        Assert.Null(okError);

        Assert.False(FormulaParser.TryParse("mean(", out var failed, out var error));
        Assert.Null(failed);
        Assert.Contains(AuthoringCompileCodes.FormulaParse, error, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeSave_keeps_parse_empty_and_nested_filter_blocked()
    {
        var dotted = FormulaLowerer.DescribeSaveOutcome("mean(VDC.mean)", new LimitSpec(null, null, 1.2));
        Assert.Equal(FormulaSaveOutcomeKind.PacksMeanGte, dotted.Kind);

        var nested = FormulaLowerer.DescribeSaveOutcome("mean(filter([0.5],[1],VDC))", null);
        Assert.Equal(FormulaSaveOutcomeKind.SaveBlocked, nested.Kind);
        Assert.Contains(AuthoringCompileCodes.FormulaNoLower, nested.Message, StringComparison.Ordinal);
        Assert.Contains("nested", nested.Message, StringComparison.OrdinalIgnoreCase);
    }
}
