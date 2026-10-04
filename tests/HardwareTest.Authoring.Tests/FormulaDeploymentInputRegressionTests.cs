using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaDeploymentInputRegressionTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("missing")]
    [InlineData("forward")]
    [InlineData("other-scope")]
    [InlineData("excluded")]
    public void Filter_status_and_save_reject_unavailable_execution_scope_producer(string source)
    {
        var input = Input();
        var formula = Formula();
        IReadOnlyList<MeasureNode> nodes = source switch
        {
            "missing" => [formula],
            "forward" => [formula, input],
            "other-scope" => [new RepeatNode(2, [input]), formula],
            _ => [input with { Metric = input.Metric with { Source = new ExpressionAlgorithm(["other"], "std(other)") } }, formula]
        };
        var draft = AuthoringRecipeCatalog.CreateProgram("scope-filter") with { Measure = nodes };
        if (source == "excluded") draft.AuthoringState.FormulaIntent[input.NodeId] = FormulaDeploymentIntent.Explore;
        var status = FormulaDeploymentClassifier.Classify(formula.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan")));
        Assert.Equal(status.Message, error.Message);
        Assert.Contains("MISSING_CHANNEL", error.Message);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(10, 8)]
    [InlineData(5, 1)]
    public void Declared_source_grid_failures_match_status_save_and_runtime_grid(int intervalMs, int count)
    {
        var input = Input(intervalMs, count);
        var formula = Formula();
        var draft = AuthoringRecipeCatalog.CreateProgram("grid-filter") with { Measure = [input, formula] };
        var status = FormulaDeploymentClassifier.Classify(formula.Metric, draft);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan")));
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        Assert.Equal(status.Message, error.Message);
        var elapsed = Enumerable.Range(0, count).Select(index => (double)index * intervalMs).ToArray();
        var runtimeError = Assert.Throws<InvalidOperationException>(() => TransferFunctionGrid.RequireUniform(elapsed, FormulaLowerer.DefaultTsSeconds));
        Assert.Equal(runtimeError.Message, error.Message);
    }

    [Fact]
    public void Known_raw_sample_publishers_use_actual_channel_and_clock_in_status_and_compiled_plan()
    {
        var first = Raw(new PublishTimedSampleStep { Channel = "input", ElapsedMs = 0, Value = 1 });
        var second = Raw(new PublishTimedSampleStep { Channel = "input", ElapsedMs = 5, Value = 2 });
        var formula = Formula();
        var draft = AuthoringRecipeCatalog.CreateProgram("raw-filter") with { Measure = [first, second, formula] };
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(formula.Metric, draft).Kind);
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == formula.NodeId && issue.Code is "MISSING_CHANNEL" or "FORMULA_DEPLOYMENT");
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan");
        new PlanCompiler().Save(draft, path);
        Assert.Contains("ApplyTransferFunctionStep", File.ReadAllText(path));
        // A raw publisher with one missing clock fails even though it has valid channel metadata.
        draft = draft with { Measure = [first, Raw(new PublishTimedSampleStep { Channel = "input", ElapsedMs = null, Value = 2 }), formula] };
        var status = FormulaDeploymentClassifier.Classify(formula.Metric, draft);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Equal(status.Message, error.Message);
        Assert.Contains(AuthoringCompileCodes.TfMissingElapsed, error.Message);
    }

    [Fact]
    public void Supported_mean_and_raw_sample_publisher_share_actual_compiler_acceptance()
    {
        var raw = Raw(new PublishTimedSampleStep { Channel = "input", Value = 1 });
        var formula = Formula() with { Metric = Formula().Metric with { Limits = new LimitSpec(null, null, 0), Source = new ExpressionAlgorithm(["input"], "mean(input)") } };
        var draft = AuthoringRecipeCatalog.CreateProgram("raw-mean") with { Measure = [raw, formula] };
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(formula.Metric, draft).Kind);
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == formula.NodeId && issue.Code is "MISSING_CHANNEL" or "FORMULA_DEPLOYMENT");
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan");
        new PlanCompiler().Save(draft, path);
        Assert.Contains(raw.NodeId.ToString(), File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    private static RawStepNode Raw(ITestStep step)
    {
        AuthoringPluginSearch.Search();
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "raw.TapPlan");
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }

    private static MetricNode Input(int interval = 5, int count = 8) => new(new MetricDraft("Input", "input", "timeseries", "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["IntervalMs"] = interval.ToString(), ["SampleCount"] = count.ToString() })));
    private static MetricNode Formula() => new(new MetricDraft("Filter", "filtered", "timeseries", "V", null, null,
        new ExpressionAlgorithm(["input"], "filter([1],[1],input)")));
}
