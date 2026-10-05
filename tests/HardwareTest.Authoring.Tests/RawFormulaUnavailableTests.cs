using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class RawFormulaUnavailableTests
{
    [Theory]
    [InlineData(false, "mean")]
    [InlineData(true, "mean")]
    [InlineData(false, "filter")]
    [InlineData(true, "filter")]
    [InlineData(false, "native-mean")]
    [InlineData(true, "native-mean")]
    [InlineData(false, "native-filter")]
    [InlineData(true, "native-filter")]
    public void Known_raw_load_failures_become_per_node_requirements_without_changing_source(bool malformed, string kind)
    {
        var xml = malformed ? "<TestStep" : "<TestStep type='Missing.Plugin.Step'><Name>Unavailable plugin</Name></TestStep>";
        var raw = new RawStepNode("Missing.Plugin.Step", xml);
        var healthy = new MetricNode(new MetricDraft("Healthy", "healthy", "timeseries", "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        MetricSource source = kind switch
        {
            "native-mean" => new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["missing"], new Dictionary<string, string>()),
            "native-filter" => new TransferFunctionAlgorithm("missing", [0.5, 0.5], [1], 0.005, "filter"),
            "filter" => new ExpressionAlgorithm(["missing"], "filter([0.5,0.5],[1],missing)"),
            _ => new ExpressionAlgorithm(["missing"], "mean(missing)")
        };
        var derived = new MetricNode(new MetricDraft("Derived", "result", kind.Contains("filter") ? "timeseries" : "scalar", "V", new LimitSpec(null, null, 0), null, source));
        var draft = AuthoringRecipeCatalog.CreateProgram("opaque-formula") with { Measure = [raw, healthy, derived] };
        if (source is ExpressionAlgorithm)
        {
            var status = FormulaDeploymentClassifier.Classify(derived.Metric, draft, nodeId: derived.NodeId);
            Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
            Assert.Contains("Missing.Plugin.Step", status.Message);
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => PlanCompiler.ValidateFormulaInput(draft.Measure, "result", draft.AuthoringState, derived.NodeId));
        Assert.Contains("Missing.Plugin.Step", error.Message);
        Assert.NotNull(error.InnerException);
        var issue = Assert.Single(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == derived.NodeId && issue.Message.Contains("Missing.Plugin.Step", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        var board = BoardPreviewBuilder.Build(draft);
        Assert.NotEmpty(Assert.Single(board, tile => tile.NodeId == healthy.NodeId).Preview.CannedSamples);
        Assert.Empty(Assert.Single(board, tile => tile.NodeId == derived.NodeId).Preview.CannedSamples);
        Assert.Equal(xml, raw.XmlFragment);
    }
}
