using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class NativeFormulaIncompleteIssueTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false, "MetricSetting:IntervalMs", true)]
    [InlineData(false, "MetricSetting:SampleCount", true)]
    [InlineData(true, "MetricSetting:IntervalMs", true)]
    [InlineData(true, "MetricSetting:SampleCount", true)]
    [InlineData(true, "Threshold", false)]
    public void Native_consumer_issue_matches_preview_for_actual_incomplete_fields_and_compilation_stays_blocked(bool average, string field, bool upstream)
    {
        var input = Input(); var consumer = Consumer(average);
        var draft = Draft([input, consumer]); var id = upstream ? input.NodeId : consumer.NodeId;
        var key = AuthoringDocumentState.FieldKey(id, field);
        draft.AuthoringState.IncompleteNumericText[key] = "abc";
        var issue = Assert.Single(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == consumer.NodeId);
        Assert.Contains("BUILD_INCOMPLETE", issue.Message); Assert.Contains(field, issue.Message);
        Assert.Equal(issue.Message, MetricPreviewBuilder.From(consumer.Metric, sourceContext: draft).Note);
        var path = Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, path));
        Assert.Contains("BUILD_INCOMPLETE", error.Message);
        Assert.False(File.Exists(path)); Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
        Assert.Equal("abc", draft.AuthoringState.IncompleteNumericText[key]);
        draft.AuthoringState.IncompleteNumericText.Clear();
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == consumer.NodeId);
        Assert.Null(MetricPreviewBuilder.From(consumer.Metric, sourceContext: draft).Note);
        new PlanCompiler().Save(draft, path); Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unrelated_incomplete_sibling_does_not_create_a_native_consumer_issue(bool average, bool siblingFirst)
    {
        var input = Input(); var consumer = Consumer(average);
        var sibling = Input() with { Metric = Input().Metric with { ChannelKey = "unrelated" } };
        var draft = Draft(siblingFirst ? [sibling, input, consumer] : [input, consumer, sibling]);
        draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(sibling.NodeId, "MetricSetting:SampleCount")] = "abc";
        Assert.DoesNotContain(AuthoringIssueService.GetIssues(draft), issue => issue.NodeId == consumer.NodeId);
        Assert.Null(MetricPreviewBuilder.From(consumer.Metric, sourceContext: draft).Note);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft,
            Path.Combine(AuthoringBuildSnapshotTests.Temp(), draft.PlanId + ".TapPlan")));
        Assert.Contains("BUILD_INCOMPLETE", error.Message);
    }

    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> nodes) => AuthoringRecipeCatalog.CreateProgram("native-incomplete") with { Measure = nodes };
    private static MetricNode Input() => new(new MetricDraft("Input", "input", "timeseries", "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>() { ["IntervalMs"] = "5", ["SampleCount"] = "8" })));
    private static MetricNode Consumer(bool average) => new(new MetricDraft("Consumer", "result", average ? "scalar" : "timeseries", "V",
        average ? new LimitSpec(null, null, 0) : null, null, average
            ? new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["input"], new Dictionary<string, string>())
            : new TransferFunctionAlgorithm("input", [1], [1], 0.005, "filter")));
}
