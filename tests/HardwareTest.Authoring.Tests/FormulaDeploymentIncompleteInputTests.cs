using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class FormulaDeploymentIncompleteInputTests
{
    [Theory]
    [InlineData("Threshold", false)]
    [InlineData("MetricSetting:IntervalMs", true)]
    [InlineData("MetricSetting:SampleCount", true)]
    public void Selected_formula_and_actual_upstream_numeric_text_block_readiness_without_using_retained_values(string field, bool upstream)
    {
        var input = Input(); var formula = Formula(upstream ? "filter([1],[1],input)" : "mean(input)");
        var draft = MockDmmDraftFixture.Create("incomplete") with { Measure = [input, formula] };
        var id = upstream ? input.NodeId : formula.NodeId;
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(id, field)] = "abc";
        AssertIncomplete(draft, formula, field);
        var reopened = AuthoringDocumentDto.FromDraft(draft).ToDraft();
        AssertIncomplete(reopened, formula, field);
        Assert.Equal("abc", draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(id, field)]);
        var path = Path.Combine(Path.GetTempPath(), "ht-formula-incomplete-" + Guid.NewGuid().ToString("N"), "incomplete.TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains("BUILD_INCOMPLETE", error.Message);
        Assert.False(File.Exists(path));
        draft.AuthoringState.IncompleteNumericText.Clear();
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(formula.Metric, draft).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unrelated_incomplete_sibling_does_not_contaminate_selected_consumer_or_preview(bool siblingFirst)
    {
        var input = Input(); var formula = Formula("filter([1],[1],input)");
        var sibling = Input() with { Metric = Input().Metric with { ChannelKey = "unrelated" } };
        var draft = MockDmmDraftFixture.Create("siblings") with { Measure = siblingFirst ? [sibling, input, formula] : [input, formula, sibling] };
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(sibling.NodeId, "MetricSetting:IntervalMs")] = "abc";
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(formula.Metric, draft).Kind);
        Assert.Null(MetricPreviewBuilder.From(formula.Metric, sourceContext: draft).Note);
    }

    [Fact]
    public void Incomplete_numeric_text_on_transitive_publisher_or_enclosing_repeat_is_required_by_the_consumer()
    {
        var input = Input(); var upstream = Formula("filter([1],[1],input)");
        upstream = upstream with { Metric = upstream.Metric with { ChannelKey = "upstream" } };
        var dependent = Formula("filter([1],[1],upstream)");
        var loop = new RepeatNode(2, [input, upstream, dependent]);
        var draft = MockDmmDraftFixture.Create("nested") with { Measure = [loop] };
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(input.NodeId, "MetricSetting:SampleCount")] = "abc";
        AssertIncomplete(draft, dependent, "MetricSetting:SampleCount");
        draft.AuthoringState.IncompleteNumericText.Clear();
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(loop.NodeId, "RepeatCount")] = "abc";
        AssertIncomplete(draft, dependent, "RepeatCount");
    }

    private static void AssertIncomplete(ProgramDraft draft, MetricNode formula, string field)
    {
        var status = FormulaDeploymentClassifier.Classify(formula.Metric, draft);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, status.Kind);
        Assert.Contains("BUILD_INCOMPLETE", status.Message);
        Assert.Contains(field, status.Message);
        Assert.Equal(status.Message, MetricPreviewBuilder.From(formula.Metric, sourceContext: draft).Note);
    }
    private static MetricNode Input() => new(new MetricDraft("Input", "input", "timeseries", "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
    private static MetricNode Formula(string source) => new(new MetricDraft("Formula", "result", "timeseries", "V", new LimitSpec(null, null, 0), null,
        new ExpressionAlgorithm([], source)));
}
