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
    [InlineData("filter(VDC)")]
    [InlineData("filtfilt(VDC)")]
    public void Unknown_and_tf_functions_fail_parse(string source)
    {
        var ex = Assert.Throws<AuthoringWorkspaceException>(() => FormulaParser.Parse(source));
        Assert.Contains(AuthoringCompileCodes.FormulaParse, ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown function", ex.Message, StringComparison.OrdinalIgnoreCase);
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
}
