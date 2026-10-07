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
public sealed class BoardPreviewIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-board-identity-" + Guid.NewGuid().ToString("N"));
    public BoardPreviewIdentityTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Actual_nonmonotonic_publication_grid_stays_invalid_in_recording_filter_and_mean()
    {
        RawStepNode Raw(double elapsed)
        {
            var step = new PublishTimedSampleStep { Channel = "input", Value = elapsed + 1, ElapsedMs = elapsed };
            var plan = new global::OpenTap.TestPlan();
            plan.ChildTestSteps.Add(step);
            var path = Path.Combine(_root, "raw.TapPlan");
            plan.Save(path);
            return new(step.GetType().FullName!, XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep").ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        var filter = new MetricNode(new MetricDraft("Filter", "filtered", PresentationRoles.Timeseries, "V", null, null,
            new TransferFunctionAlgorithm("input", [0.5, 0.5], [1], 0.005, "filter")));
        var mean = new MetricNode(new MetricDraft("Mean", "mean", PresentationRoles.Scalar, "V", new LimitSpec(null, null, 0), null,
            new ExpressionAlgorithm(["filtered"], "mean(filtered)")));
        var middle = Raw(5);
        var last = Raw(10);
        var draft = Draft([Raw(0), middle, last, filter, mean]);
        var path = Path.Combine(_root, "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        // Runtime edits can invalidate a compiler-accepted grid; the recording preserves what ran.
        Assert.True(session.TrySetParameter(OpenTapParameterInfo.FormatStepMemberKey(middle.NodeId.ToString(), "ElapsedMs"), "10"));
        Assert.True(session.TrySetParameter(OpenTapParameterInfo.FormatStepMemberKey(last.NodeId.ToString(), "ElapsedMs"), "5"));
        var summary = await session.RunAsync();
        Assert.DoesNotContain(summary.Samples, sample => sample.ProducerStepId == filter.NodeId || sample.ProducerStepId == mean.NodeId);
        var recording = JsonSerializer.Deserialize(JsonSerializer.Serialize(new TestRunRecord { PlanId = "board", Samples = summary.Samples, Events = summary.Events }, AppJsonContext.Default.TestRunRecord), AppJsonContext.Default.TestRunRecord)!;
        Assert.Equal(new double?[] { 0, 10, 5 }, recording.Samples.Select(sample => sample.ElapsedMs));
        var board = BoardPreviewBuilder.Build(draft, recording);
        var filtered = Assert.Single(board, tile => tile.NodeId == filter.NodeId);
        Assert.Empty(filtered.Preview.CannedSamples);
        Assert.Contains("TF_GRID", filtered.Preview.Note!);
        Assert.Empty(Assert.Single(board, tile => tile.NodeId == mean.NodeId).Preview.CannedSamples);
        Assert.Equal(new double?[] { 0, 10, 5 }, RunDatasetBinder.SeriesByMetric(recording)["input"].Select(sample => sample.ElapsedMs));
        // A synthetic single-execution binder fixture also checks row order; these IDs are not runtime evidence.
        var producer = Guid.NewGuid();
        var execution = Guid.NewGuid();
        foreach (var sample in recording.Samples)
        {
            sample.ProducerStepId = producer;
            sample.StepRunId = execution;
            sample.StepPath = "one publisher";
        }
        Assert.Equal(new double?[] { 0, 10, 5 }, Assert.Single(RunDatasetBinder.SeriesForProducer(recording, "input", producer)).Select(sample => sample.ElapsedMs));
    }

    [Fact]
    public void Same_channel_in_distinct_repeat_scopes_resolves_the_selected_metric_identity()
    {
        var first = Publisher("first", "V", 0, 4);
        var second = Publisher("second", "mV", 100, 200);
        var draft = Draft([new RepeatNode(2, [first]), new RepeatNode(3, [second])]);
        var expected = BoardPreviewBuilder.Build(draft).Single(tile => tile.NodeId == first.NodeId).Preview;
        var direct = MetricPreviewBuilder.From(first.Metric, [first.Metric, second.Metric], sourceContext: draft);
        AssertPreview(expected, direct);
        Assert.Equal("V", direct.YUnit);
        Assert.Equal(4, direct.LimitHigh);
        AssertPreview(expected, MetricPreviewBuilder.From(first.Metric with { }, [first.Metric, second.Metric], sourceContext: draft, nodeId: first.NodeId));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        new AuthoringDocumentStore(_root).Save(AuthoringDocumentDto.FromDraft(draft));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.StopRecovery();
        try
        {
            foreach (var node in new[] { first, second })
            {
                vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == node.NodeId));
                var tile = vm.BoardTiles.Single(tile => tile.NodeId == node.NodeId);
                AssertPreview(tile.Preview, vm.Preview);
                Assert.Equal(node.Metric.YUnit, vm.Preview.YUnit);
                Assert.Equal(node.Metric.Limits!.High, vm.Preview.LimitHigh);
            }
            var recording = new TestRunRecord
            {
                PlanId = "board",
                Samples = [
                new StoredSample { MetricKey = "same", ProducerStepId = first.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 1, Unit = "V" },
                new StoredSample { MetricKey = "same", ProducerStepId = first.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 2, Value = 3, Unit = "V" },
                new StoredSample { MetricKey = "same", ProducerStepId = second.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 150, Unit = "mV" }]
            };
            var source = Path.Combine(_root, "scoped.json");
            File.WriteAllText(source, JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord));
            vm.ImportRecording(source, "scoped");
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == first.NodeId));
            Assert.Equal(3, vm.Preview.CannedValue);
            Assert.Equal("V", vm.Preview.YUnit);
            Assert.Equal(2, vm.BoardTiles.Count(tile => tile.NodeId == first.NodeId));
            Assert.Equal(4, vm.Preview.LimitHigh);
            var recorded = RunDatasetBinder.SeriesByMetric(recording);
            Assert.Equal(3, MetricPreviewBuilder.From(first.Metric, [first.Metric, second.Metric], recorded, draft).CannedValue);
        }
        finally { vm.StopRecovery(); }
    }

    private static void AssertPreview(MetricPreview expected, MetricPreview actual)
    {
        Assert.Equal(expected.YUnit, actual.YUnit);
        Assert.Equal(expected.CannedValue, actual.CannedValue);
        Assert.Equal(expected.CannedSamples, actual.CannedSamples);
        Assert.Equal(expected.SampleElapsedMs, actual.SampleElapsedMs);
        Assert.Equal(expected.LimitLow, actual.LimitLow);
        Assert.Equal(expected.LimitHigh, actual.LimitHigh);
        Assert.Equal(expected.Note, actual.Note);
        Assert.Equal(expected.Passed, actual.Passed);
    }

    private static MetricNode Publisher(string name, string unit, double low, double high) => new(new MetricDraft(name, "same", PresentationRoles.Timeseries, unit,
        new LimitSpec(low, high, null), null, new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> nodes) => new("board", new ProgramSidecar { DisplayName = "Board" },
        [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")], [], nodes, new CleanupPolicy(false, "DMM"));
}
