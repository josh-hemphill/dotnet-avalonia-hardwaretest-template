using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class BoardPreviewScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-board-scope-" + Guid.NewGuid().ToString("N"));
    public BoardPreviewScopeTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("typed")]
    [InlineData("raw-typed")]
    [InlineData("typed-raw")]
    public async Task Actual_compiled_recording_matches_normally_decompiled_typed_and_mixed_timed_publishers(string sourceShape)
    {
        RawStepNode Raw(double value, double elapsed)
        {
            var step = new PublishTimedSampleStep { Channel = "input", Value = value, ElapsedMs = elapsed };
            var plan = new global::OpenTap.TestPlan();
            plan.ChildTestSteps.Add(step);
            var path = Path.Combine(_root, "publisher.TapPlan");
            plan.Save(path);
            return new(step.GetType().FullName!, XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep").ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        var first = Raw(1, 0);
        var second = Raw(3, 5);
        var filter = new MetricNode(new MetricDraft("Filter", "filtered", PresentationRoles.Timeseries, "V", null, null,
            new TransferFunctionAlgorithm("input", [0.5, 0.5], [1], 0.005, "filter")));
        var mean = Mean("filtered", "average");
        var source = Draft([new RepeatNode(2, [first, second, filter, mean])]);
        var path = Path.Combine(_root, "board.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(source, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var actual = await session.RunAsync();
        var recording = Roundtrip(new TestRunRecord { PlanId = "board", RunId = actual.RunId, Result = actual.Result, Samples = actual.Samples, Events = actual.Events });
        // Ordinary import decompiles catalogue timed publishers to typed nodes. Saving duplicate
        // typed channels remains subject to the existing compiler guard; this exercises import.
        var imported = compiler.Load(path);
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(imported.Measure));
        Assert.IsType<MetricNode>(repeat.Children[0]);
        Assert.IsType<MetricNode>(repeat.Children[1]);
        imported = imported with { Measure = [repeat with { Children = [sourceShape == "raw-typed" ? first : repeat.Children[0], sourceShape == "typed-raw" ? second : repeat.Children[1], .. repeat.Children.Skip(2)] }] };
        var board = BoardPreviewBuilder.Build(imported, recording);
        var runtimeMeans = actual.Samples.Where(sample => sample.ProducerStepId == mean.NodeId).Select(sample => sample.Value).ToArray();
        Assert.Equal(new[] { 1.25, 1.25 }, runtimeMeans);
        Assert.Equal(runtimeMeans, board.Where(tile => tile.NodeId == mean.NodeId).Select(tile => tile.Preview.CannedValue));
        Assert.All(board, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        foreach (var producer in new[] { first.NodeId, second.NodeId })
        {
            Assert.Equal(2, board.Count(tile => tile.NodeId == producer));
            Assert.All(board.Where(tile => tile.NodeId == producer), tile => Assert.Single(tile.Preview.CannedSamples));
            Assert.Equal(2, recording.Samples.Where(sample => sample.ProducerStepId == producer).Select(sample => sample.StepRunId).Distinct().Count());
        }
        Assert.All(board.Where(tile => tile.NodeId == filter.NodeId), tile =>
        {
            Assert.Equal(new double?[] { 0, 5 }, tile.Preview.SampleElapsedMs);
            Assert.Contains("Computed offline", tile.Scope);
        });
        Assert.All(BoardPreviewBuilder.Build(imported), tile => Assert.NotEmpty(tile.Preview.CannedSamples));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Same_output_formula_in_another_scope_cannot_poison_healthy_dependency_or_scalar_example(bool scalar, bool incomplete)
    {
        var healthyInput = scalar ? new MetricNode(new MetricDraft("Scalar", "healthy", "passband", "V", new LimitSpec(1, 2, null), null,
            new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string> { ["MetricName"] = "healthy", ["Value"] = "1.25" }))) : Input("healthy");
        var healthy = Mean("healthy", "result");
        var brokenInput = Input("broken");
        var broken = Mean(incomplete ? "broken" : "missing", "result");
        var draft = Draft([new RepeatNode(2, [healthyInput, healthy]), new RepeatNode(2, [brokenInput, broken])]);
        if (incomplete)
        {
            draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(brokenInput.NodeId, "MetricSetting:SampleCount")] = "abc";
            var source = Assert.IsType<MeasureSource>(brokenInput.Metric.Source);
            Assert.IsType<Dictionary<string, string>>(source.Settings)["SampleCount"] = "abc";
        }
        var board = BoardPreviewBuilder.Build(draft);
        var preview = Assert.Single(board, tile => tile.NodeId == healthy.NodeId).Preview;
        Assert.NotEmpty(preview.CannedSamples);
        Assert.Equal(scalar ? 1.25 : 1, preview.CannedValue);
        if (scalar) Assert.Contains("Preview only", preview.Note!);
        Assert.Empty(Assert.Single(board, tile => tile.NodeId == broken.NodeId).Preview.CannedSamples);
        Assert.NotEmpty(MetricPreviewBuilder.From(healthy.Metric, sourceContext: draft, nodeId: healthy.NodeId).CannedSamples);
        if (!scalar)
        {
            Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(healthy.Metric, draft).Kind);
            var issues = AuthoringIssueService.GetIssues(draft);
            Assert.Contains(issues, issue => issue.NodeId == healthy.NodeId && issue.Code == "DUPLICATE_CHANNEL");
            Assert.DoesNotContain(issues, issue => issue.NodeId == healthy.NodeId && (issue.Code is "FORMULA_DEPLOYMENT" or "MISSING_CHANNEL" or "BUILD_INCOMPLETE"));
            // Synthetic recording values exercise per-node validation; they are not runtime evidence.
            var recording = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { MetricKey = "healthy", ProducerStepId = healthyInput.NodeId, Value = 1 }, new StoredSample { MetricKey = "healthy", ProducerStepId = healthyInput.NodeId, Value = 3 }] };
            Assert.Equal(2, BoardPreviewBuilder.Build(draft, recording).Single(tile => tile.NodeId == healthy.NodeId).Preview.CannedValue);
        }
        else Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, FormulaDeploymentClassifier.Classify(healthy.Metric, draft).Kind);
        Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(_root, "board.TapPlan")));
    }

    [Fact]
    public void Selected_formula_recording_evidence_counts_only_its_own_executions_and_issues()
    {
        var firstInput = Input("first");
        var secondInput = Input("second");
        var first = Mean("first", "result");
        var second = Mean("second", "result");
        var draft = Draft([new RepeatNode(2, [firstInput, first]), new RepeatNode(1, [secondInput, second])]);
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        new AuthoringDocumentStore(_root).Save(AuthoringDocumentDto.FromDraft(draft));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.StopRecovery();
        try
        {
            // Synthetic scoped fixture for editor evidence selection, not a deployable duplicate-channel plan.
            var recording = new TestRunRecord
            {
                PlanId = "board",
                Samples = [
                new StoredSample { MetricKey = "first", ProducerStepId = firstInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 2 },
                new StoredSample { MetricKey = "first", ProducerStepId = firstInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 2, Value = 4 },
                new StoredSample { MetricKey = "second", ProducerStepId = secondInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 15 }]
            };
            var path = Path.Combine(_root, "scoped.json");
            File.WriteAllText(path, JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord));
            vm.ImportRecording(path, "scoped");
            void Select(Guid id) => vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == id));
            Select(first.NodeId);
            Assert.Contains("2 source execution(s)", vm.FormulaRecordingEvidence);
            Select(second.NodeId);
            Assert.Contains("1 source execution(s)", vm.FormulaRecordingEvidence);
            vm.FormulaSource = "mean(missing)";
            Assert.Contains("0 source execution(s)", vm.FormulaRecordingEvidence);
            Assert.Contains("missing", vm.FormulaRecordingEvidence);
            Select(first.NodeId);
            Assert.Contains("2 source execution(s)", vm.FormulaRecordingEvidence);
            Assert.DoesNotContain("missing", vm.FormulaRecordingEvidence);
            Assert.Equal(4, vm.Preview.CannedValue);
        }
        finally { vm.StopRecovery(); }
    }

    private static TestRunRecord Roundtrip(TestRunRecord recording) => JsonSerializer.Deserialize(JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord), AppJsonContext.Default.TestRunRecord)!;
    private static MetricNode Input(string key) => new(new MetricDraft(key, key, PresentationRoles.Timeseries, "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
    private static MetricNode Mean(string input, string output) => new(new MetricDraft(output, output, PresentationRoles.Scalar, "V", new LimitSpec(null, null, 0), null, new ExpressionAlgorithm([input], $"mean({input})")));
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> measure) => new("board", new ProgramSidecar { DisplayName = "Board" },
        [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")], [], measure, new CleanupPolicy(false, "DMM"));
}
