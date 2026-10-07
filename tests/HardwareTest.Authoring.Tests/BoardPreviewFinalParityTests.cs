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
public sealed class BoardPreviewFinalParityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-board-final-" + Guid.NewGuid().ToString("N"));
    public BoardPreviewFinalParityTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Actual_outer_publishers_after_first_nested_loop_keep_iteration_and_recording_parity()
    {
        var innerFirst = Raw(new PublishTimedSampleStep { Channel = "inner", Value = 7, ElapsedMs = 0, EventLabel = "inner start" });
        var innerSecond = Raw(new PublishTimedSampleStep { Channel = "inner", Value = 9, ElapsedMs = 5, EventLabel = "inner end" });
        var first = Raw(new PublishTimedSampleStep { Channel = "input", Value = 1, ElapsedMs = 0, EventLabel = "outer start" });
        var second = Raw(new PublishTimedSampleStep { Channel = "input", Value = 3, ElapsedMs = 5, EventLabel = "outer end" });
        var filter = new MetricNode(new MetricDraft("Filter", "filtered", "timeseries", "V", null, null,
            new TransferFunctionAlgorithm("input", [0.5, 0.5], [1], 0.005, "filter")));
        var mean = Mean("filtered");
        var outer = new RepeatNode(2, [new RepeatNode(2, [innerFirst, innerSecond]), first, second, filter, mean]);
        var draft = Draft([outer]);
        var path = Path.Combine(_root, "board.TapPlan");
        var compiler = new PlanCompiler();
        compiler.Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var actual = await session.RunAsync();
        var recording = JsonSerializer.Deserialize(JsonSerializer.Serialize(new TestRunRecord
        { PlanId = draft.PlanId, RunId = actual.RunId, Result = actual.Result, Samples = actual.Samples, Events = actual.Events }, AppJsonContext.Default.TestRunRecord), AppJsonContext.Default.TestRunRecord)!;
        Assert.Equal(RunResult.Passed, actual.Result);
        var recordedMeans = recording.Samples.Where(sample => sample.ProducerStepId == mean.NodeId).ToArray();
        Assert.Equal(new[] { 1.25, 1.25 }, recordedMeans.Select(sample => sample.Value));
        // Normal import changes scripted publishers to typed nodes; both forms retain true evidence.
        foreach (var representation in new[] { draft, compiler.Load(path) })
        {
            var board = BoardPreviewBuilder.Build(representation, recording);
            var means = board.Where(tile => tile.NodeId == mean.NodeId).ToArray();
            Assert.Equal(recordedMeans.Select(sample => sample.Value), means.Select(tile => tile.Preview.CannedValue));
            Assert.Equal(recordedMeans.Select(sample => sample.Unit), means.Select(tile => tile.Preview.YUnit));
            Assert.Equal(actual.Steps.Where(step => step.StepId == mean.NodeId.ToString()).Select(step => (bool?)step.Passed), means.Select(tile => tile.Preview.Passed));
            Assert.All(means, tile => { Assert.Equal("V", tile.Preview.YUnit); Assert.True(tile.Preview.Passed); });
            Assert.All(board, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        }
        var outerSamples = recording.Samples.Where(sample => sample.ProducerStepId == first.NodeId).ToArray();
        Assert.Equal(new int?[] { 1, 2 }, outerSamples.Select(sample => sample.IterationIndex));
        Assert.Single(outerSamples.Select(sample => sample.LoopRunId).Distinct());
        Assert.All(outerSamples, sample => { Assert.NotNull(sample.LoopRunId); Assert.NotNull(sample.StepRunId); });
        var innerSamples = recording.Samples.Where(sample => sample.ProducerStepId == innerFirst.NodeId).ToArray();
        Assert.Equal(new int?[] { 1, 2, 1, 2 }, innerSamples.Select(sample => sample.IterationIndex));
        Assert.Equal(2, innerSamples.Select(sample => sample.LoopRunId).Distinct().Count());
        Assert.DoesNotContain(outerSamples[0].LoopRunId, innerSamples.Select(sample => sample.LoopRunId));
        Assert.Equal(new int?[] { 1, 2 }, recording.Events.Where(mark => mark.ProducerStepId == first.NodeId).Select(mark => mark.IterationIndex));
        foreach (var sample in outerSamples.Concat(innerSamples))
        {
            var restored = OpenTapProgressFrameDto.From(new OpenTapProgress { Message = "actual sample", Sample = MeasurementSampleEvent.FromStored(sample) }).ToProgress().Sample!;
            Assert.Equal(sample.LoopRunId, restored.LoopRunId);
            Assert.Equal(sample.IterationIndex, restored.IterationIndex);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_and_typed_publishers_without_recording_evidence_retain_unavailable_tiles(bool scalar)
    {
        var raw = Raw(scalar ? new PublishBandScalarStep { MetricName = "missing", Value = 2, Unit = "V" }
            : new PublishTimedSampleStep { Channel = "missing", Value = 2, ElapsedMs = 0 });
        var typed = new MetricNode(new MetricDraft("Typed publisher", "missing", scalar ? "scalar" : "timeseries", "V", scalar ? new LimitSpec(0, 3, null) : null, null,
            new AlgorithmSource(scalar ? AuthoringFunctionIds.BasicPublishBandScalar : AuthoringFunctionIds.BasicPublishTimedSample, [], new Dictionary<string, string>())));
        var recording = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { MetricKey = "unrelated", Value = 19 }] };
        foreach (var publisher in new MeasureNode[] { raw, typed })
        {
            var tile = Assert.Single(BoardPreviewBuilder.Build(Draft([publisher]), recording));
            Assert.Equal(publisher.NodeId, tile.NodeId);
            Assert.Equal("missing", tile.Preview.ChannelKey);
            Assert.Empty(tile.Preview.CannedSamples);
            Assert.Contains("Missing recording channel 'missing'", tile.Preview.Note);
            Assert.Contains("Recording", tile.Scope);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standalone_mean_with_complete_graph_uses_input_values_without_optional_siblings(bool formula)
    {
        var input = new MetricNode(new MetricDraft("Acquire", "input", "timeseries", "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
        var mean = Mean("input");
        if (!formula) mean = mean with { Metric = mean.Metric with { Source = new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["input"], new Dictionary<string, string>()) } };
        var draft = Draft([input, mean]);
        var board = Assert.Single(BoardPreviewBuilder.Build(draft), tile => tile.NodeId == mean.NodeId).Preview;
        var standalone = MetricPreviewBuilder.From(mean.Metric, sourceContext: draft, nodeId: mean.NodeId);
        Assert.Equal(1, board.CannedValue);
        Assert.Equal(board.CannedValue, standalone.CannedValue);
        Assert.Equal(board.YUnit, standalone.YUnit);
        Assert.Equal(board.Passed, standalone.Passed);
        Assert.Equal(board.CannedSamples, standalone.CannedSamples);
    }

    private RawStepNode Raw(global::OpenTap.ITestStep step)
    {
        var plan = new global::OpenTap.TestPlan();
        plan.ChildTestSteps.Add(step);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".TapPlan");
        plan.Save(path);
        return new(step.GetType().FullName!, XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep").ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }
    private static MetricNode Mean(string input) => new(new MetricDraft("Mean", "average", "scalar", "V", new LimitSpec(null, null, 0), null, new ExpressionAlgorithm([input], $"mean({input})")));
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> measure) => new("board", new ProgramSidecar { DisplayName = "Board" },
        [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")], [], measure, new CleanupPolicy(false, "DMM"));
}
