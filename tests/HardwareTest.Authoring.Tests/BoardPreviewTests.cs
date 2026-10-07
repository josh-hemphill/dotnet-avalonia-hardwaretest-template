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
public sealed class BoardPreviewTests : IDisposable
{
    private readonly List<string> _roots = [];
    public void Dispose() { foreach (var root in _roots) if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Compiled_recording_mean_and_filter_chain_match_values_units_verdict_and_execution_scopes(double threshold)
    {
        var acquire = Acquisition();
        var first = Filter("first", "VDC");
        var second = Filter("second", "first");
        var mean = Mean("second");
        mean = mean with { Metric = mean.Metric with { Limits = new LimitSpec(null, null, threshold) } };
        var draft = Draft([new RepeatNode(2, [acquire, first, second, mean])]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        var run = new TestRunRecord { PlanId = draft.PlanId, RunId = summary.RunId, Result = summary.Result, Samples = summary.Samples };
        // Serialize the actual host recording, so this does not fabricate producer/run evidence.
        var json = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.TestRunRecord)!;
        if (Environment.GetEnvironmentVariable("HT_BOARD_EVIDENCE_DIRECTORY") is { } evidence)
        {
            var exported = Path.Combine(evidence, threshold == 0 ? "pass" : "fail");
            Directory.CreateDirectory(exported);
            File.Copy(path, Path.Combine(exported, "board.TapPlan"), true);
            File.Copy(PlanCompiler.SidecarPath(path), Path.Combine(exported, "board.program.json"), true);
            File.WriteAllText(Path.Combine(exported, "run.json"), json);
            new AuthoringDocumentStore(exported).Save(AuthoringDocumentDto.FromDraft(draft));
            File.WriteAllText(Path.Combine(exported, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        }
        var board = BoardPreviewBuilder.Build(draft, restored);
        var means = board.Where(tile => tile.NodeId == mean.NodeId).ToArray();
        Assert.Equal(2, means.Length);
        var runtime = restored.Samples.Where(sample => sample.ProducerStepId == mean.NodeId).ToArray();
        Assert.Equal(2, runtime.Length);
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(runtime[index].Value, means[index].Preview.CannedValue, 10);
            Assert.Equal(runtime[index].Unit, means[index].Preview.YUnit);
            Assert.Equal(runtime[index].Value >= threshold, means[index].Preview.Passed);
            Assert.Contains($"iteration {index + 1}", means[index].Scope);
        }
        Assert.Equal(threshold == 0 ? RunResult.Passed : RunResult.Failed, summary.Result);
        var acquisitionSamples = restored.Samples.Where(sample => sample.ProducerStepId == acquire.NodeId).ToArray();
        Assert.Equal(2, acquisitionSamples.Select(sample => sample.StepRunId).Distinct().Count());
        Assert.All(acquisitionSamples, sample => Assert.NotNull(sample.StepRunId));
        Assert.Equal(6, board.Where(tile => tile.NodeId == second.NodeId).Sum(tile => tile.Preview.CannedSamples.Count));
        var unrelatedAcquire = Acquisition();
        var unrelatedDraft = Draft([unrelatedAcquire]);
        var unrelatedPath = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(unrelatedDraft, unrelatedPath);
        var unrelatedSession = new OpenTapSession();
        await unrelatedSession.LoadPlanAsync(unrelatedPath);
        restored.Samples.AddRange((await unrelatedSession.RunAsync()).Samples);
        Assert.Equal(means.Select(tile => tile.Preview.CannedValue), BoardPreviewBuilder.Build(draft, restored).Where(tile => tile.NodeId == mean.NodeId).Select(tile => tile.Preview.CannedValue));
        var progress = new OpenTapProgress { Message = "sample", Sample = MeasurementSampleEvent.FromStored(acquisitionSamples[0]) };
        var roundtrip = OpenTapProgressFrameDto.From(progress).ToProgress().Sample!;
        Assert.Equal(acquisitionSamples[0].StepRunId, roundtrip.StepRunId);
        Assert.Equal(acquisitionSamples[0].ProducerStepId, roundtrip.ProducerStepId);
        Assert.Equal(acquisitionSamples[0].IterationIndex, roundtrip.IterationIndex);
        Assert.Equal(acquisitionSamples[0].ElapsedMs, roundtrip.ElapsedMs);
    }

    [Fact]
    public void Example_filter_filter_mean_chain_uses_shared_sample_evaluation()
    {
        var acquire = Acquisition();
        var draft = Draft([acquire, Filter("first", "VDC"), Filter("second", "first"), Mean("second")]);
        var board = BoardPreviewBuilder.Build(draft);
        Assert.Equal(4, board.Count);
        Assert.All(board, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        Assert.Equal(new double?[] { 0, 5, 10 }, board[2].Preview.SampleElapsedMs);
        Assert.Equal(board[2].Preview.CannedSamples.Average(), board[3].Preview.CannedValue, 10);
        Assert.True(board[3].Preview.Passed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Example_concrete_defaults_and_case_variant_precedence_match_actual_publisher(bool overrideValue)
    {
        var settings = new Dictionary<string, string>();
        if (overrideValue) { settings["Value"] = "2"; settings["value"] = "7"; }
        var publisher = new MetricNode(new MetricDraft("Publisher", "scalar", PresentationRoles.Scalar, "V",
            new LimitSpec(0, 5, null), null, new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], settings)));
        var draft = Draft([publisher]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        var sample = Assert.Single(summary.Samples);
        var tile = Assert.Single(BoardPreviewBuilder.Build(draft));
        Assert.Equal(overrideValue ? 7 : 0, sample.Value);
        Assert.Equal(sample.Value, tile.Preview.CannedValue);
        Assert.Equal(sample.Unit, tile.Preview.YUnit);
        Assert.Equal(sample.EffectiveMetricKey, tile.Preview.ChannelKey);
        Assert.Contains(sample.Channel, tile.Scope);
        Assert.Equal(!overrideValue, tile.Preview.Passed);
        Assert.Equal(overrideValue ? RunResult.Failed : RunResult.Passed, summary.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compiler_accepted_source_forms_use_concrete_filter_and_average_settings(bool measureSource)
    {
        MetricSource filterSource = measureSource
            ? new MeasureSource(string.Empty, AuthoringFunctionIds.BasicApplyTransferFunction, new Dictionary<string, string> { ["InputChannel"] = "VDC", ["TsSeconds"] = "0.005" })
            : new AlgorithmSource(AuthoringFunctionIds.BasicApplyTransferFunction, ["VDC"], new Dictionary<string, string> { ["inputchannel"] = "VDC", ["tsseconds"] = "0.005" });
        MetricSource averageSource = measureSource
            ? new MeasureSource(string.Empty, AuthoringFunctionIds.BasicChannelAverage, new Dictionary<string, string> { ["inputchannel"] = "filtered" })
            : new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["filtered"], new Dictionary<string, string>());
        var filter = Filter("filtered", "VDC") with { Metric = Filter("filtered", "VDC").Metric with { Source = filterSource } };
        var mean = Mean("filtered") with { Metric = Mean("filtered").Metric with { Source = averageSource } };
        var draft = Draft([Acquisition(), filter, mean]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        var recording = new TestRunRecord { PlanId = "board", Samples = summary.Samples };
        var board = BoardPreviewBuilder.Build(draft, recording);
        var runtime = Assert.Single(summary.Samples, sample => sample.ProducerStepId == mean.NodeId);
        Assert.Equal(runtime.Value, board[2].Preview.CannedValue, 10);
        Assert.Equal(runtime.Unit, board[2].Preview.YUnit);
        Assert.True(board[2].Preview.Passed);
        Assert.All(BoardPreviewBuilder.Build(draft), tile => Assert.NotEmpty(tile.Preview.CannedSamples));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_imported_timed_producers_form_separate_repeat_grids_for_filter_and_mean(bool nested)
    {
        RawStepNode Raw(double value, double elapsed)
        {
            var step = new PublishTimedSampleStep { Channel = "input", Value = value, ElapsedMs = elapsed, EventLabel = "sample" };
            var plan = new global::OpenTap.TestPlan();
            plan.ChildTestSteps.Add(step);
            var path = Path.Combine(Temp(), "publisher.TapPlan");
            plan.Save(path);
            var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
            return new(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        var filter = Filter("filtered", "input");
        var mean = Mean("filtered");
        var repeat = new RepeatNode(2, [Raw(1, 0), Raw(3, 5), filter, mean]);
        var draft = Draft([nested ? new RepeatNode(2, [repeat]) : repeat]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var progress = new CapturedProgress();
        var summary = await session.RunAsync(progress: progress);
        var run = new TestRunRecord { PlanId = "board", RunId = summary.RunId, Result = summary.Result, Samples = summary.Samples, Events = summary.Events };
        var json = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var recording = JsonSerializer.Deserialize(json, AppJsonContext.Default.TestRunRecord)!;
        var board = BoardPreviewBuilder.Build(draft, recording);
        var actualMeans = summary.Samples.Where(sample => sample.ProducerStepId == mean.NodeId).Select(sample => sample.Value).ToArray();
        Assert.Equal(nested ? 4 : 2, actualMeans.Length);
        Assert.Equal(actualMeans, board.Where(tile => tile.NodeId == mean.NodeId).Select(tile => tile.Preview.CannedValue));
        Assert.Equal(nested ? 4 : 2, board.Count(tile => tile.NodeId == filter.NodeId));
        Assert.All(board.Where(tile => tile.NodeId == filter.NodeId || tile.NodeId == mean.NodeId), tile =>
        {
            Assert.Equal(2, tile.Chrome.Events.Count);
            Assert.Single(tile.Chrome.Events.Select(mark => (mark.LoopRunId, mark.IterationIndex)).Distinct());
            Assert.All(tile.Chrome.Events, mark => Assert.NotNull(mark.LoopRunId));
        });
        var legacy = JsonSerializer.Deserialize(json, AppJsonContext.Default.TestRunRecord)!;
        foreach (var sample in legacy.Samples) sample.LoopRunId = null;
        if (nested) Assert.Empty(BoardPreviewBuilder.Build(draft, legacy).Single(tile => tile.NodeId == mean.NodeId).Preview.CannedSamples);
        Assert.Equal(nested ? 2 : 1, recording.Samples.Select(sample => sample.LoopRunId).Distinct().Count());
        Assert.All(recording.Samples, sample => Assert.NotNull(sample.LoopRunId));
        Assert.Equal(nested ? 8 : 4, recording.Events.Count);
        Assert.All(recording.Events, mark =>
        {
            Assert.Contains(recording.Samples, sample => sample.StepRunId == mark.StepRunId && sample.ProducerStepId == mark.ProducerStepId
                && sample.LoopRunId == mark.LoopRunId && sample.IterationIndex == mark.IterationIndex && sample.StepPath == mark.StepPath);
        });
        var cassettePath = Temp();
        OpenTapRunRecordingStore.WriteBeside(cassettePath, "run", progress.Frames, summary);
        var cassette = OpenTapRunRecordingStore.LoadBeside(cassettePath, "run");
        Assert.Equal(recording.Samples.Select(sample => sample.LoopRunId), cassette.Summary.Samples.Select(sample => sample.LoopRunId));
        Assert.Equal(recording.Events.Select(mark => mark.LoopRunId), cassette.Summary.Events.Select(mark => mark.LoopRunId));
        Assert.All(cassette.Progress.Where(frame => frame.Sample is not null), frame =>
            Assert.Contains(recording.Samples, sample => sample.StepRunId == frame.ToProgress().Sample!.StepRunId && sample.LoopRunId == frame.Sample!.LoopRunId));
        Assert.All(cassette.Progress.Where(frame => frame.Event is not null), frame =>
        {
            Assert.Contains(recording.Events, mark => mark.StepRunId == frame.Event!.StepRunId && mark.LoopRunId == frame.Event.LoopRunId);
            Assert.Equal(frame.Event!.ProducerStepId?.ToString(), frame.StepId);
            Assert.Equal(frame.Event.StepPath, frame.StepPath);
        });
        Assert.Contains(cassette.Progress, frame => frame.Event is not null);
        if (nested && Environment.GetEnvironmentVariable("HT_BOARD_EVIDENCE_DIRECTORY") is { } evidence)
        {
            var exported = Path.Combine(evidence, "nested-repeat");
            Directory.CreateDirectory(exported);
            File.Copy(path, Path.Combine(exported, "board.TapPlan"), true);
            File.Copy(PlanCompiler.SidecarPath(path), Path.Combine(exported, "board.program.json"), true);
            File.WriteAllText(Path.Combine(exported, "run.json"), json);
            new AuthoringDocumentStore(exported).Save(AuthoringDocumentDto.FromDraft(draft));
            File.WriteAllText(Path.Combine(exported, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
            OpenTapRunRecordingStore.WriteBeside(Path.Combine(exported, "cassette"), "run", progress.Frames, summary);
        }
        Assert.All(BoardPreviewBuilder.Build(draft), tile => Assert.NotEmpty(tile.Preview.CannedSamples));
    }

    [Theory]
    [InlineData("Enabled", "false", false)]
    [InlineData("eNaBlEd", "false", false)]
    [InlineData("Enabled", "true", true)]
    [InlineData(null, null, true)]
    [InlineData("duplicateFalse", null, false)]
    [InlineData("duplicateTrue", null, true)]
    public async Task Compiled_average_enabled_setting_matches_board(string? key, string? value, bool enabled)
    {
        var settings = key switch
        {
            "duplicateFalse" => new Dictionary<string, string> { ["Enabled"] = "true", ["enabled"] = "false" },
            "duplicateTrue" => new Dictionary<string, string> { ["Enabled"] = "false", ["enabled"] = "true" },
            null => new Dictionary<string, string>(),
            _ => new Dictionary<string, string> { [key] = value! }
        };
        var metric = Mean("VDC").Metric with { Source = new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, ["VDC"], settings) };
        var mean = new MetricNode(metric);
        var draft = Draft([Acquisition(), mean]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        Assert.Equal(enabled, summary.Samples.Any(sample => sample.ProducerStepId == mean.NodeId));
        var board = BoardPreviewBuilder.Build(draft, new TestRunRecord { PlanId = "board", Samples = summary.Samples });
        Assert.Equal(enabled, board[1].Preview.CannedSamples.Count > 0);
        Assert.Equal(enabled, BoardPreviewBuilder.Build(draft)[1].Preview.CannedSamples.Count > 0);
        if (!enabled)
        {
            Assert.Null(board[1].Preview.Passed);
            Assert.Contains("disabled", board[1].Preview.Note!);
            var dependent = Mean("mean") with { Metric = Mean("mean").Metric with { ChannelKey = "after" } };
            Assert.Empty(BoardPreviewBuilder.Build(draft with { Measure = [.. draft.Measure, dependent] }, new TestRunRecord { PlanId = "board", Samples = summary.Samples })[2].Preview.CannedSamples);
            Assert.Empty(BoardPreviewBuilder.Build(draft with { Measure = [.. draft.Measure, dependent] })[2].Preview.CannedSamples);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disabled_typed_derived_forms_produce_no_runtime_or_preview(bool measureSource, bool filter)
    {
        var id = filter ? AuthoringFunctionIds.BasicApplyTransferFunction : AuthoringFunctionIds.BasicChannelAverage;
        var settings = new Dictionary<string, string> { ["eNaBlEd"] = "false", ["InputChannel"] = "VDC" };
        var metric = (filter ? Filter("filtered", "VDC") : Mean("VDC")).Metric with
        {
            Source = measureSource ? new MeasureSource(string.Empty, id, settings) : new AlgorithmSource(id, ["VDC"], settings)
        };
        var node = new MetricNode(metric);
        var draft = Draft([Acquisition(), node]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(draft, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        Assert.DoesNotContain(summary.Samples, sample => sample.ProducerStepId == node.NodeId);
        Assert.Empty(BoardPreviewBuilder.Build(draft, new TestRunRecord { PlanId = "board", Samples = summary.Samples })[1].Preview.CannedSamples);
        Assert.Empty(BoardPreviewBuilder.Build(draft)[1].Preview.CannedSamples);
        Assert.Empty(MetricPreviewBuilder.From(metric).CannedSamples);
    }

    [Theory]
    [InlineData("samples", "null")]
    [InlineData("samples", "[null]")]
    [InlineData("events", "null")]
    [InlineData("events", "[null]")]
    public void Structurally_invalid_recordings_have_usable_errors_and_preserve_selection_and_bytes(string collection, string value)
    {
        var root = Temp();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        new PlanCompiler().Save(Draft([Acquisition()]), Path.Combine(root, "board.TapPlan"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.StopRecovery();
        try
        {
            var source = Path.Combine(Temp(), "run.json");
            File.WriteAllText(source, JsonSerializer.Serialize(new TestRunRecord { PlanId = "board", Samples = [new StoredSample { Channel = "VDC", Value = 2 }] }, AppJsonContext.Default.TestRunRecord));
            vm.ImportRecording(source, "captured");
            var selected = vm.SelectedDataset!;
            var before = File.ReadAllBytes(selected.Path);
            var recordings = Path.Combine(root, "recordings");
            var entries = Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories);
            File.WriteAllText(source, $"{{\"schemaVersion\":1,\"planId\":\"board\",\"samples\":[],\"events\":[]}}".Replace($"\"{collection}\":[]", $"\"{collection}\":{value}"));
            var invalidBytes = File.ReadAllBytes(source);
            Assert.Contains(collection, Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Load(source)).Message);
            Assert.Contains(collection, Assert.Throws<AuthoringWorkspaceException>(() => vm.ImportRecording(source, "invalid")).Message);
            Assert.Same(selected, vm.SelectedDataset);
            Assert.Equal(before, File.ReadAllBytes(selected.Path));
            Assert.Equal(invalidBytes, File.ReadAllBytes(source));
            Assert.Equal(entries, Directory.GetFileSystemEntries(recordings, "*", SearchOption.AllDirectories));
            Assert.Single(vm.Datasets);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Missing_channel_and_invalid_grid_target_only_affected_nodes()
    {
        var acquire = Acquisition();
        var draft = Draft([acquire, Filter("filtered", "VDC"), Mean("filtered")]);
        var run = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { MetricKey = "VDC", ProducerStepId = acquire.NodeId, Value = 2, ElapsedMs = 0 }, new StoredSample { MetricKey = "VDC", ProducerStepId = acquire.NodeId, Value = 4, ElapsedMs = 9 }] };
        var board = BoardPreviewBuilder.Build(draft, run);
        Assert.NotEmpty(board[0].Preview.CannedSamples);
        Assert.Empty(board[1].Preview.CannedSamples);
        Assert.Contains("GRID", board[1].Preview.Note!);
        Assert.Empty(board[2].Preview.CannedSamples);
        Assert.Contains("filtered", board[2].Preview.Note!);
        Assert.All(BoardPreviewBuilder.Build(draft, new TestRunRecord()), tile => Assert.Empty(tile.Preview.CannedSamples));
    }

    [Fact]
    public void Import_is_atomic_contained_and_failure_preserves_selected_dataset()
    {
        var root = Temp();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        var workspace = AuthoringWorkspaceLoader.Load(root);
        var source = Path.Combine(Temp(), "run.json");
        var run = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { Channel = "VDC", Value = 2, ElapsedMs = 5 }] };
        File.WriteAllText(source, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        var imported = RunDatasetCatalog.Import(workspace, source, "captured", "board");
        Assert.Equal(5, Assert.Single(imported.Run.Samples).ElapsedMs);
        var before = File.ReadAllBytes(imported.Path);
        Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Import(workspace, source, "captured", "board"));
        Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Import(workspace, source, "../escape", "board"));
        File.WriteAllText(source, "invalid");
        Assert.Throws<AuthoringWorkspaceException>(() => RunDatasetCatalog.Import(workspace, source, "invalid", "board"));
        Assert.Equal(before, File.ReadAllBytes(imported.Path));
        Assert.Single(RunDatasetCatalog.List(workspace));
        Assert.False(Directory.Exists(Path.Combine(root, "recordings", "invalid")));
    }

    [Fact]
    public void View_model_refresh_selects_success_and_retains_selection_on_invalid_or_colliding_import()
    {
        var root = Temp();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        new PlanCompiler().Save(Draft([Acquisition(), Mean("VDC")]), Path.Combine(root, "board.TapPlan"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.StopRecovery();
        try
        {
            var source = Path.Combine(Temp(), "run.json");
            var run = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { Channel = "VDC", Value = 2, ElapsedMs = 5 }] };
            File.WriteAllText(source, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
            vm.ImportRecording(source, "captured");
            var selected = vm.SelectedDataset;
            Assert.NotNull(selected);
            Assert.Single(vm.Datasets);
            Assert.Contains("5", vm.BoardTiles.First().Preview.SampleElapsedMs.Select(value => value.ToString()));
            Assert.Throws<AuthoringWorkspaceException>(() => vm.ImportRecording(source, "captured"));
            Assert.Same(selected, vm.SelectedDataset);
            run.SchemaVersion = 999;
            File.WriteAllText(source, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
            Assert.Throws<AuthoringWorkspaceException>(() => vm.ImportRecording(source, "future"));
            Assert.Same(selected, vm.SelectedDataset);
            File.WriteAllText(source, "invalid");
            Assert.Throws<AuthoringWorkspaceException>(() => vm.ImportRecording(source, "invalid"));
            Assert.Same(selected, vm.SelectedDataset);
            vm.UseExampleData();
            Assert.Null(vm.SelectedDataset);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public async Task Recording_derived_mean_chain_requires_actual_sample_publisher_capability()
    {
        var first = Mean("VDC") with { Metric = Mean("VDC").Metric with { ChannelKey = "firstMean" } };
        var second = Mean("firstMean") with { Metric = Mean("firstMean").Metric with { ChannelKey = "secondMean" } };
        var compiled = Draft([Acquisition(), first]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(compiled, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        var runtime = Assert.Single(summary.Samples, sample => sample.ProducerStepId == first.NodeId);
        var recording = RoundtripActualRecording(summary);
        var draft = compiled with { Measure = [.. compiled.Measure, second] };
        Assert.Contains("Sample", Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(draft, Path.Combine(Temp(), "board.TapPlan"))).Message);
        var board = BoardPreviewBuilder.Build(draft, recording);
        Assert.Equal(runtime.Value, board[1].Preview.CannedValue, 10);
        Assert.Empty(board[2].Preview.CannedSamples);
        Assert.Null(board[2].Preview.Passed);
        Assert.Contains("Sample", board[2].Preview.Note!);
        // Unrelated unfinished acquisition settings must not poison a valid recorded dependency.
        var unrelated = Acquisition() with { Metric = Acquisition().Metric with { ChannelKey = "unrelated", Source = new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "unfinished" }) } };
        var independent = compiled with { Measure = [unrelated, .. compiled.Measure] };
        Assert.Equal(runtime.Value, BoardPreviewBuilder.Build(independent, recording)[2].Preview.CannedValue, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recorded_band_scalar_cannot_become_runtime_mean_input(bool raw)
    {
        var publisher = new MetricNode(new MetricDraft("Scalar", "input", PresentationRoles.Scalar, "V", new LimitSpec(0, 5, null), null,
            new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string> { ["MetricName"] = "input", ["Value"] = "2" })));
        MeasureNode producer = publisher;
        if (raw)
        {
            var step = new PublishBandScalarStep { MetricName = "input", Value = 2 };
            var plan = new global::OpenTap.TestPlan();
            plan.ChildTestSteps.Add(step);
            var rawPath = Path.Combine(Temp(), "publisher.TapPlan");
            plan.Save(rawPath);
            producer = new RawStepNode(step.GetType().FullName!, XDocument.Load(rawPath).Descendants().First(element => element.Name.LocalName == "TestStep").ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
        }
        var compiled = Draft([producer]);
        var path = Path.Combine(Temp(), "board.TapPlan");
        new PlanCompiler().Save(compiled, path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync();
        Assert.Equal(2, Assert.Single(summary.Samples).Value);
        var draft = compiled with { Measure = [producer, Mean("input")] };
        var board = BoardPreviewBuilder.Build(draft, RoundtripActualRecording(summary));
        Assert.Empty(board[1].Preview.CannedSamples);
        Assert.Null(board[1].Preview.Passed);
        Assert.Contains("Sample", board[1].Preview.Note!);
        Assert.Contains("Scalar publisher", BoardPreviewBuilder.Build(draft)[1].Preview.Note!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_source_replacement_cannot_publish_another_program_or_drop_selection(bool duringCopy)
    {
        var root = Temp();
        File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"plansDirectory\":\".\"}");
        new PlanCompiler().Save(Draft([Acquisition()]), Path.Combine(root, "board.TapPlan"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.StopRecovery();
        try
        {
            var source = Path.Combine(Temp(), "run.json");
            var run = new TestRunRecord { PlanId = "board", Samples = [new StoredSample { Channel = "VDC", Value = 2 }] };
            File.WriteAllText(source, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
            vm.ImportRecording(source, "captured");
            var selected = vm.SelectedDataset!;
            var before = File.ReadAllBytes(selected.Path);
            var entries = Directory.GetFileSystemEntries(Path.Combine(root, "recordings"), "*", SearchOption.AllDirectories);
            void Replace() => File.WriteAllText(source, JsonSerializer.Serialize(WithPlan(), AppJsonContext.Default.TestRunRecord));
            TestRunRecord WithPlan() => new() { PlanId = "another", Samples = run.Samples };
            if (!duringCopy) Replace();
            Assert.Contains("plan id", Assert.Throws<AuthoringWorkspaceException>(() => vm.ImportRecording(source, "wrong", duringCopy ? Replace : null)).Message);
            Assert.Same(selected, vm.SelectedDataset);
            Assert.Equal(before, File.ReadAllBytes(selected.Path));
            Assert.Equal(entries, Directory.GetFileSystemEntries(Path.Combine(root, "recordings"), "*", SearchOption.AllDirectories));
            Assert.Single(vm.Datasets);
        }
        finally { vm.StopRecovery(); }
    }

    private static TestRunRecord RoundtripActualRecording(OpenTapRunSummary summary)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(new TestRunRecord
        {
            PlanId = "board",
            RunId = summary.RunId,
            Result = summary.Result,
            Samples = summary.Samples,
            Events = summary.Events
        }, AppJsonContext.Default.TestRunRecord), AppJsonContext.Default.TestRunRecord)!;

    private sealed class CapturedProgress : IProgress<OpenTapProgress>
    {
        public List<OpenTapProgressFrameDto> Frames { get; } = [];
        public void Report(OpenTapProgress value) => Frames.Add(OpenTapProgressFrameDto.From(value));
    }

    private static MetricNode Acquisition() => new(new MetricDraft("Acquire", "VDC", PresentationRoles.Timeseries, "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["intervalms"] = "5" })));
    private static MetricNode Filter(string output, string input) => new(new MetricDraft(output, output, PresentationRoles.Timeseries, "V", null, null,
        new TransferFunctionAlgorithm(input, [0.5, 0.5], [1], 0.005, "filter")));
    private static MetricNode Mean(string input) => new(new MetricDraft("Average", "mean", PresentationRoles.Scalar, "V", new LimitSpec(null, null, 0), null,
        new ExpressionAlgorithm([input], $"mean({input})")));
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> measure) => new("board", new ProgramSidecar { DisplayName = "Board" },
        [new InstrumentRef("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::INSTR0")], [], measure, new CleanupPolicy(false, "DMM"));
    private string Temp() { var root = Path.Combine(Path.GetTempPath(), "ht-board-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); _roots.Add(root); return root; }
}
