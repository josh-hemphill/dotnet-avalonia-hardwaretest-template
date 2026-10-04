using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaScalarExamplePreviewTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Known_scalar_example_preview_does_not_claim_a_deployable_sample_recipe(bool raw)
    {
        MeasureNode scalar = raw ? Raw(new PublishBandScalarStep { MetricName = "input", Value = 1.25 }) : Scalar();
        var formula = Formula(); var draft = Draft([scalar, formula]);
        var status = FormulaDeploymentClassifier.Classify(formula.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        Assert.Contains("MISSING_CHANNEL", status.Message);
        var preview = MetricPreviewBuilder.From(formula.Metric, sourceContext: draft);
        Assert.Equal(1.25, Assert.Single(preview.CannedSamples));
        Assert.Contains("Preview only", preview.Note);
        Assert.Contains("Scalar publisher", preview.Note);
        Assert.Contains("Sample publisher", preview.Note);
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Equal(status.Message, error.Message);
        Assert.False(File.Exists(path)); Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("other-scope")]
    [InlineData("ambiguous")]
    [InlineData("unknown-scalar")]
    public void Scalar_example_policy_preserves_actual_source_order_scope_and_capability_safety(string mode)
    {
        var scalar = Scalar(); var formula = Formula();
        var draft = Draft(mode switch
        {
            "future" => [formula, scalar],
            "other-scope" => [new RepeatNode(2, [scalar]), formula],
            "ambiguous" => [scalar, Scalar(), formula],
            _ => [UnknownScalar(), formula],
        });
        var preview = MetricPreviewBuilder.From(formula.Metric, sourceContext: draft);
        Assert.Empty(preview.CannedSamples); Assert.Contains("MISSING_CHANNEL", preview.Note);
        Assert.Equal(FormulaDeploymentClassifier.Classify(formula.Metric, draft).Message, preview.Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scalar_example_checks_only_relevant_incomplete_numeric_fields(bool relevant)
    {
        var scalar = Scalar(); var unrelated = Scalar() with { Metric = Scalar().Metric with { ChannelKey = "unrelated" } };
        var formula = Formula(); var draft = Draft([scalar, unrelated, formula]);
        var node = relevant ? scalar : unrelated;
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "MetricSetting:Value")] = "abc";
        var preview = MetricPreviewBuilder.From(formula.Metric, sourceContext: draft);
        if (relevant) { Assert.Empty(preview.CannedSamples); Assert.Contains("BUILD_INCOMPLETE", preview.Note); }
        else { Assert.NotEmpty(preview.CannedSamples); Assert.Contains("Preview only", preview.Note); }
    }

    [Fact]
    public void Runtime_channel_average_recipe_remains_strict_for_scalar_input()
    {
        var formula = Formula() with { Metric = Formula().Metric with { Source = new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["input"], new Dictionary<string, string>()) } };
        var draft = Draft([Scalar(), formula]);
        var preview = MetricPreviewBuilder.From(formula.Metric, sourceContext: draft);
        Assert.Empty(preview.CannedSamples); Assert.Contains("MISSING_CHANNEL", preview.Note);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan")));
        Assert.Equal(preview.Note, error.Message);
    }

    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> nodes) => AuthoringRecipeCatalog.CreateProgram("scalar-preview") with { Measure = nodes };
    private static MetricNode Scalar() => new(new MetricDraft("Scalar", "input", "passband", "V", new LimitSpec(1, 2, null), null,
        new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string> { ["MetricName"] = "input", ["Value"] = "1.25" })));
    private static MetricNode Formula() => new(new MetricDraft("Formula", "result", "scalar", "V", new LimitSpec(null, null, 0), null, new ExpressionAlgorithm(["input"], "mean(input)")));
    private static RawStepNode UnknownScalar()
    {
        AuthoringPluginSearch.Search();
        var step = new MeanGteStep { Threshold = 0 };
        PresentationAttach.Apply(step, Scalar().Metric);
        return Raw(step);
    }
    private static RawStepNode Raw(ITestStep step)
    {
        AuthoringPluginSearch.Search(); var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "raw.TapPlan");
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }
}
