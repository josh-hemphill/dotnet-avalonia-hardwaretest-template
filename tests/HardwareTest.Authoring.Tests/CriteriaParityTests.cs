using HardwareTest.Authoring;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class CriteriaParityTests
{
    private static MetricDraft Average(double? threshold = 2) => new("Average", "mean", "scalar", "V",
        threshold is null ? null : new(null, null, threshold), null, new ExpressionAlgorithm(["input"], "mean(input)"));

    [Fact]
    public void Recorded_preview_uses_runtime_evaluator_and_inclusive_threshold()
    {
        var input = new Dictionary<string, IReadOnlyList<StoredSample>>
        {
            ["input"] = new[] { new StoredSample { Value = 1 }, new StoredSample { Value = 3 } },
        };
        var preview = MetricPreviewBuilder.From(Average(), recorded: input);
        var runtime = ChannelAverageEvaluator.Evaluate([1, 3], 2);
        Assert.Equal(runtime.Average, preview.CannedValue);
        Assert.Equal(runtime.Passed, preview.Passed);
        Assert.True(preview.Passed);
        Assert.Equal("V", AuthoringCriteria.Requirements(Average())!.Unit);
    }

    [Fact]
    public void Display_cannot_remove_requirements_or_change_limits()
    {
        var metric = Average() with { DisplayRole = "timeseries" };
        Assert.True(AuthoringCriteria.Requirements(metric)!.RequiresThreshold);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringCriteria.Validate(metric with { Limits = null }));
        var lowered = Assert.IsType<AlgorithmSource>(FormulaLowerer.Lower((ExpressionAlgorithm)metric.Source, metric.Limits));
        Assert.Equal(AuthoringFunctionIds.BasicChannelAverage, lowered.AlgorithmId);
        Assert.False(AuthoringFunctionCatalog.All.Single(f => f.Id == lowered.AlgorithmId).NeedsInstrument);
    }

    [Fact]
    public void Band_recipe_requires_ordered_finite_bounds_even_for_timeseries_display()
    {
        var metric = new MetricDraft("Band", "band", "timeseries", "V", null, null,
            new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string>()));
        Assert.True(AuthoringCriteria.Requirements(metric)!.RequiresBand);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringCriteria.Validate(metric));
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringCriteria.Validate(metric with { Limits = new(3, 1, null) }));
        AuthoringCriteria.Validate(metric with { Limits = new(1, 1, null) });
    }

    [Fact]
    public void Inspector_keeps_recipe_criteria_visible_after_display_changes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "criteria-inspector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var vm = new AuthoringWorkspaceViewModel();
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "dirs.proj"))) root = root.Parent;
            File.Copy(Path.Combine(root!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(directory, "authoring.json"));
            vm.Open(directory);
            vm.CreateProgram("criteria");
            vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
            vm.DisplayRole = "timeseries";
            Assert.True(vm.ShowThreshold);
            Assert.DoesNotContain(vm.MetricSettingRows, row => row.Key == "Threshold");
            vm.ApplyRecipe(AuthoringRecipeIds.BandScalar);
            vm.DisplayRole = "scalar";
            Assert.True(vm.ShowBandLimits);
            Assert.False(vm.ShowThreshold);
            vm.ApplyRecipe(AuthoringRecipeIds.StationHealth);
            vm.DisplayRole = "timeseries";
            Assert.True(vm.ShowBandLimits);
            Assert.DoesNotContain(vm.MetricSettingRows, row => AuthoringCriteria.IsRuntimeLimit(row.Key));
        }
        finally { vm.StopRecovery(); Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_criterion_is_rejected(double value)
    {
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringCriteria.Validate(Average(value)));
        Assert.Equal(FormulaSaveOutcomeKind.SaveBlocked, FormulaLowerer.DescribeSaveOutcome("mean(input)", new(null, null, value)).Kind);
    }

    [Fact]
    public void Station_health_requires_authoritative_offset_bounds_for_every_display()
    {
        var directory = Path.Combine(Path.GetTempPath(), "criteria-station-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var metric = new MetricDraft("Station", "cal.dc.offset", "timeseries", "V", null, null,
                new MeasureSource("", AuthoringFunctionIds.BasicReportStationHealth,
                    new Dictionary<string, string> { ["OffsetLimitLow"] = "-999", ["OffsetLimitHigh"] = "999" }));
            Assert.True(AuthoringCriteria.Requirements(metric)!.RequiresBand);
            var draft = new ProgramDraft("station", new ProgramSidecar(), [], [], [new MetricNode(metric)], new(false, ""));
            var compiler = new PlanCompiler();
            var path = Path.Combine(directory, "station.TapPlan");
            var error = Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(draft, path));
            Assert.Contains(AuthoringCompileCodes.MissingLimits, error.Message);
            Assert.False(File.Exists(path));
            draft = draft with { Measure = [new MetricNode(metric with { Limits = new(-0.01, 0.01, null) })] };
            compiler.Save(draft, path);
            var imported = Assert.IsType<MetricNode>(compiler.Load(path).Measure[0]).Metric;
            Assert.Equal(new LimitSpec(-0.01, 0.01, null), imported.Limits);
            Assert.DoesNotContain(Assert.IsType<MeasureSource>(imported.Source).Settings.Keys, AuthoringCriteria.IsRuntimeLimit);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Missing_or_nonfinite_recording_never_claims_a_verdict()
    {
        var missing = MetricPreviewBuilder.From(Average(), recorded: new Dictionary<string, IReadOnlyList<StoredSample>>());
        Assert.Null(missing.Passed);
        Assert.Empty(missing.CannedSamples);
        Assert.Contains("missing series", missing.Note);
        var invalid = MetricPreviewBuilder.From(Average(), recorded: new Dictionary<string, IReadOnlyList<StoredSample>>
        { ["input"] = new[] { new StoredSample { Value = double.NaN } } });
        Assert.Null(invalid.Passed);
        Assert.Empty(invalid.CannedSamples);
        Assert.Contains("finite samples", invalid.Note);
    }

    [Fact]
    public void Producer_without_presentation_retains_published_channel_through_import_save_and_execution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "criteria-plain-producer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var producer = new PublishTimedSampleStep { Channel = "input", Value = 2, ElapsedMs = 0 };
            var average = new ChannelAverageStep { InputChannel = "input", Channel = "average", Unit = "V", ProducerStepId = producer.Id, Threshold = 2 };
            var original = new TestPlan();
            original.ChildTestSteps.Add(producer);
            original.ChildTestSteps.Add(average);
            var before = new PublishedChannelListener();
            Assert.Equal(Verdict.Pass, original.Execute([before], []).Verdict);
            var path = Path.Combine(directory, "plain.TapPlan");
            using (var stream = File.Create(path)) original.Save(stream);
            Assert.DoesNotContain("PresentationMixin", File.ReadAllText(path), StringComparison.Ordinal);
            var compiler = new PlanCompiler();
            var imported = compiler.Load(path);
            Assert.Equal("input", Assert.IsType<MetricNode>(imported.Measure[0]).Metric.ChannelKey);
            compiler.Save(imported, path);
            var after = new PublishedChannelListener();
            Assert.Equal(Verdict.Pass, TestPlan.Load(path).Execute([after], []).Verdict);
            Assert.Equal(new[] { "input" }, before.SampleChannels);
            Assert.Equal(before.SampleChannels, after.SampleChannels);
            Assert.Equal(new[] { 2d }, after.Averages);
            Assert.Equal(new[] { "V" }, before.AverageUnits);
            Assert.Equal(before.AverageUnits, after.AverageUnits);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Plain_import_duplicate_producer_channels_are_rejected_before_compiled_file_changes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "criteria-duplicate-producer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = new PublishTimedSampleStep { Channel = "input", Value = 1 };
            var second = new PublishTimedSampleStep { Channel = "input", Value = 3 };
            var average = new ChannelAverageStep { InputChannel = "input", Channel = "average", ProducerStepId = first.Id, Threshold = 2 };
            var original = new TestPlan();
            original.ChildTestSteps.Add(first);
            original.ChildTestSteps.Add(second);
            original.ChildTestSteps.Add(average);
            var path = Path.Combine(directory, "duplicate.TapPlan");
            using (var stream = File.Create(path)) original.Save(stream);
            var bytes = File.ReadAllBytes(path);
            var compiler = new PlanCompiler();
            var error = Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(compiler.Load(path), path));
            Assert.Contains(AuthoringCompileCodes.DuplicateChannelKey, error.Message);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class PublishedChannelListener : ResultListener
    {
        public List<string> SampleChannels { get; } = [];
        public List<double> Averages { get; } = [];
        public List<string> AverageUnits { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            if (result.Name == "Sample")
                foreach (var value in result.Columns.Single(c => c.Name == "Channel").Data)
                    SampleChannels.Add(Convert.ToString(value) ?? string.Empty);
            if (result.Name == "Scalar")
                foreach (var value in result.Columns.Single(c => c.Name == "Unit").Data)
                    AverageUnits.Add(Convert.ToString(value) ?? string.Empty);
            if (result.Name == "Analyze")
                foreach (var value in result.Columns.Single(c => c.Name == "Mean").Data)
                    Averages.Add(Convert.ToDouble(value));
        }
    }

    [Fact]
    public void Compile_import_preserves_authoritative_limits_and_producer_without_instrument()
    {
        var directory = Path.Combine(Path.GetTempPath(), "criteria-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "criteria.TapPlan");
            var producer = new MetricNode(new("Input", "input", "timeseries", "V", null, null,
                new MeasureSource("", AuthoringFunctionIds.BasicPublishTimedSample,
                    new Dictionary<string, string> { ["Channel"] = "input", ["Value"] = "2", ["ElapsedMs"] = "0" })));
            var draft = new ProgramDraft("criteria", new ProgramSidecar(), [], [],
                [producer, new MetricNode(Average() with { DisplayRole = "timeseries" })], new(false, ""));
            var compiler = new PlanCompiler();
            compiler.Save(draft, path);
            var imported = compiler.Load(path);
            var average = Assert.IsType<MetricNode>(imported.Measure[1]).Metric;
            Assert.Equal(new LimitSpec(null, null, 2), average.Limits);
            var source = Assert.IsType<AlgorithmSource>(average.Source);
            Assert.Equal(AuthoringFunctionIds.BasicChannelAverage, source.AlgorithmId);
            Assert.Equal("input", Assert.Single(source.InputChannelKeys));
            Assert.Equal(producer.NodeId.ToString(), source.Settings["ProducerStepId"]);
            Assert.False(source.Settings.ContainsKey("Threshold"));
            imported = imported with { Measure = [imported.Measure[0], new MetricNode(average with
            { Source = source with { Settings = new Dictionary<string, string>(source.Settings) { ["Threshold"] = "999" } } })] };
            compiler.Save(imported, path);
            Assert.Equal(2, Assert.IsType<MetricNode>(compiler.Load(path).Measure[1]).Metric.Limits!.Threshold);
        }
        finally { Directory.Delete(directory, true); }
    }
}
