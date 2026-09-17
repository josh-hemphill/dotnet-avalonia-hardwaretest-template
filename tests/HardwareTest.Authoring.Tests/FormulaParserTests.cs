using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
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
}
