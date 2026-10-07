using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaEnabledProducerTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Disabled_raw_sample_publishers_cannot_supply_expression_or_native_recipe_inputs(bool native, bool average)
    {
        var consumer = Consumer(native, average);
        var first = Raw(Timed(0, false)); var second = Raw(Timed(5, false));
        var draft = Draft(average ? [first, consumer] : [first, second, consumer]);
        AssertRejected(draft, consumer, "MISSING_CHANNEL");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_clock_rows_do_not_make_a_filter_grid_appear_uniform(bool native)
    {
        var consumer = Consumer(native, false);
        var draft = Draft([Raw(Timed(0, true)), Raw(Timed(5, false)), Raw(Timed(10, true)), consumer]);
        AssertRejected(draft, consumer, "TF_GRID");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Enabled_publishers_execute_and_disabled_unrelated_rows_remain_preserved(bool native, bool average)
    {
        var consumer = Consumer(native, average); var disabled = Raw(Timed(1000, false));
        var draft = Draft(average ? [Raw(Timed(0, true)), disabled, consumer]
            : [Raw(Timed(0, true)), Raw(Timed(5, true)), disabled, consumer]);
        AssertReady(draft, consumer);
        var path = Save(draft); var plan = TestPlan.Load(path);
        Assert.False(Assert.Single(PlanCompiler.FlattenSteps(plan), step => step.Id == disabled.NodeId).Enabled);
        Assert.Equal(Verdict.Pass, plan.Execute().Verdict);
        Assert.Equal(disabled.XmlFragment, Assert.IsType<RawStepNode>(draft.Measure[^2]).XmlFragment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_imported_scope_keeps_active_descendants_inactive_and_does_not_block_unrelated_valid_recipe(bool average)
    {
        var group = new TestGroupStep { Enabled = false };
        var hiddenProducer = Timed(500, true);
        var hiddenConsumer = new ChannelAverageStep { InputChannel = "missing", Channel = "inactive.result", Threshold = 0 };
        group.ChildTestSteps.Add(hiddenProducer); group.ChildTestSteps.Add(hiddenConsumer);
        var scope = Raw(group); var consumer = Consumer(false, average);
        var draft = Draft(average ? [scope, Raw(Timed(0, true)), consumer]
            : [scope, Raw(Timed(0, true)), Raw(Timed(5, true)), consumer]);
        AssertReady(draft, consumer);
        var plan = TestPlan.Load(Save(draft));
        var imported = Assert.Single(PlanCompiler.FlattenSteps(plan), step => step.Id == scope.NodeId);
        Assert.False(imported.Enabled); Assert.All(imported.ChildTestSteps, child => Assert.True(child.Enabled));
        Assert.Equal(Verdict.Pass, plan.Execute().Verdict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_disabled_leaf_import_preserves_flags_and_cannot_be_reactivated_into_a_deployment_input(bool average)
    {
        var producer = Timed(0, false); var extra = Timed(5, false);
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "imported.TapPlan");
        var original = new TestPlan(); original.ChildTestSteps.Add(producer);
        if (!average) original.ChildTestSteps.Add(extra);
        original.Save(path);
        var compiler = new PlanCompiler(); var imported = compiler.Load(path);
        Assert.All(imported.Measure, node => Assert.IsType<RawStepNode>(node));
        compiler.Save(imported, path);
        Assert.All(PlanCompiler.FlattenSteps(TestPlan.Load(path)).OfType<PublishTimedSampleStep>(), step => Assert.False(step.Enabled));
        var consumer = Consumer(false, average);
        AssertRejected(imported with { Measure = [.. imported.Measure, consumer] }, consumer, "MISSING_CHANNEL");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_disabled_group_or_repeat_import_keeps_children_inactive_through_saved_execution(bool repeat)
    {
        TestStep scope = repeat ? new RepeatLoopStep { Count = 2 } : new TestGroupStep();
        scope.Enabled = false;
        var producer = Timed(1000, true);
        var inactive = new ChannelAverageStep { InputChannel = "missing", Channel = "inactive", Threshold = 0 };
        scope.ChildTestSteps.Add(producer); scope.ChildTestSteps.Add(inactive);
        var live = Timed(0, true); var average = new ChannelAverageStep
        { InputChannel = "input", Channel = "result", ProducerStepId = live.Id, Threshold = 0 };
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "imported-scope.TapPlan");
        var original = new TestPlan(); original.ChildTestSteps.Add(scope); original.ChildTestSteps.Add(live); original.ChildTestSteps.Add(average);
        original.Save(path);
        var compiler = new PlanCompiler(); var imported = compiler.Load(path);
        Assert.IsType<RawStepNode>(imported.Measure[0]);
        Assert.IsType<MetricNode>(imported.Measure[1]); // Enabled known leaves keep their established typed representation.
        compiler.Save(imported, path);
        var plan = TestPlan.Load(path); var savedScope = Assert.Single(PlanCompiler.FlattenSteps(plan), step => step.Id == scope.Id);
        Assert.False(savedScope.Enabled); Assert.All(savedScope.ChildTestSteps, child => Assert.True(child.Enabled));
        Assert.Equal(Verdict.Pass, plan.Execute().Verdict);
    }

    private static void AssertRejected(ProgramDraft draft, MetricNode consumer, string code)
    {
        var source = draft.Measure.OfType<RawStepNode>().Select(node => node.XmlFragment).ToArray();
        var preview = MetricPreviewBuilder.From(consumer.Metric, sourceContext: draft);
        Assert.Empty(preview.CannedSamples); Assert.Contains(code, preview.Note);
        AssertReadiness(draft, consumer, preview.Note!);
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Equal(preview.Note, error.Message); Assert.False(File.Exists(path)); Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
        Assert.Equal(source, draft.Measure.OfType<RawStepNode>().Select(node => node.XmlFragment));
    }
    private static void AssertReady(ProgramDraft draft, MetricNode consumer)
    {
        Assert.Null(MetricPreviewBuilder.From(consumer.Metric, sourceContext: draft).Note);
        AssertReadiness(draft, consumer, null);
    }
    private static void AssertReadiness(ProgramDraft draft, MetricNode consumer, string? expected)
    {
        if (consumer.Metric.Source is ExpressionAlgorithm)
        {
            var status = FormulaDeploymentClassifier.Classify(consumer.Metric, draft);
            Assert.Equal(expected is null ? FormulaDeploymentStatusKind.DeployableRecipe : FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
            if (expected is not null) Assert.Equal(expected, status.Message);
        }
        var issues = AuthoringIssueService.GetIssues(draft).Where(issue => issue.NodeId == consumer.NodeId).ToArray();
        if (expected is null) Assert.Empty(issues);
        else Assert.Equal(expected, Assert.Single(issues).Message);
    }
    private static string Save(ProgramDraft draft)
    {
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan"); new PlanCompiler().Save(draft, path); return path;
    }
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> nodes) => AuthoringRecipeCatalog.CreateProgram("enabled-inputs") with
    { Measure = nodes, Setup = [], Instruments = [], Cleanup = new CleanupPolicy(false, []) };
    private static PublishTimedSampleStep Timed(double elapsed, bool enabled) => new() { Channel = "input", ElapsedMs = elapsed, Value = 2, Enabled = enabled };
    private static MetricNode Consumer(bool native, bool average) => new(new MetricDraft("Consumer", "result", average ? "scalar" : "timeseries", "V",
        average ? new LimitSpec(null, null, 0) : null, null, native
            ? average ? new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["input"], new Dictionary<string, string>())
                : new TransferFunctionAlgorithm("input", [1], [1], 0.005, "filter")
            : new ExpressionAlgorithm(["input"], average ? "mean(input)" : "filter([1],[1],input)")));
    private static RawStepNode Raw(ITestStep step)
    {
        AuthoringPluginSearch.Search(); var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "raw.TapPlan");
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }
}
