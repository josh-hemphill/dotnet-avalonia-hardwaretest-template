using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringRawOutputAliasTests
{
    [Theory]
    [InlineData("palette")]
    [InlineData("legacy")]
    [InlineData("duplicate")]
    public void Raw_scalar_actual_alias_is_reserved_for_each_acquisition_edit(string operation)
    {
        var directory = TemporaryDirectory();
        try
        {
            var alias = operation == "duplicate" ? "VDC_2" : "VDC";
            var raw = Scalar(directory, alias);
            var draft = Program();
            if (operation == "duplicate")
            {
                draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
                draft = draft with { Measure = [draft.Measure[0], raw] };
                draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, draft.Measure[0].NodeId));
            }
            else
            {
                draft = draft with { Measure = [raw] };
                draft = operation == "palette"
                    ? AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Acquire, Row(draft, raw.NodeId), false)
                    : AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
            }
            var expected = operation == "duplicate" ? "VDC_3" : "VDC_2";
            Assert.Contains(AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure), metric => metric.ChannelKey == expected);
            var path = Path.Combine(directory, "aliases.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            Assert.Equal(alias, Assert.Single(PlanCompiler.FlattenSteps(plan).OfType<PublishBandScalarStep>()).MetricName);
            Assert.Contains(PlanCompiler.FlattenSteps(plan).OfType<AcquireVoltageStep>(), step => step.Channel == expected);
            var results = new OutputListener();
            Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
            Assert.Contains(alias, results.ScalarNames);
            Assert.Contains(expected, results.SampleChannels);
            Assert.DoesNotContain(alias, results.SampleChannels);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Inserted_and_duplicated_formula_outputs_avoid_raw_actual_scalar_alias()
    {
        var directory = TemporaryDirectory();
        try
        {
            var draft = AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.Acquire);
            var raw = Scalar(directory, "VDC.mean");
            draft = draft with { Measure = [draft.Measure[0], raw] };
            draft = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Formula, Row(draft, raw.NodeId), false);
            var formula = Assert.IsType<MetricNode>(draft.Measure[^1]);
            Assert.Equal("VDC.mean_2", formula.Metric.ChannelKey);
            draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, formula.NodeId));
            Assert.Equal("VDC.mean_2_2", Assert.IsType<MetricNode>(draft.Measure[^1]).Metric.ChannelKey);
            var path = Path.Combine(directory, "aliases.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            var checks = PlanCompiler.FlattenSteps(plan).OfType<ChannelAverageStep>().Select(step => step.Channel).ToArray();
            Assert.Equal(new[] { "VDC.mean_2", "VDC.mean_2_2" }, checks);
            var results = new OutputListener();
            Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
            Assert.Equal(new[] { "VDC.mean", "VDC.mean_2", "VDC.mean_2_2" }, results.ScalarNames);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("palette", false, "lower")]
    [InlineData("legacy", true, "lower")]
    [InlineData("duplicate", false, "duplicate")]
    [InlineData("duplicate", true, "duplicate")]
    [InlineData("duplicate", false, "default")]
    [InlineData("duplicate", true, "default")]
    public void Typed_scalar_effective_alias_is_reserved_and_executes(string operation, bool measureSource, string casing)
    {
        var directory = TemporaryDirectory();
        try
        {
            var alias = casing == "default" ? "metric" : operation == "duplicate" ? "VDC_2" : "VDC";
            var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["Value"] = "1.25" };
            if (casing == "duplicate") settings.Add("MetricName", "earlier.alias");
            if (casing != "default") settings.Add("metricname", alias);
            var scalar = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.BandScalar).Measure[0]);
            scalar = scalar with
            {
                Metric = scalar.Metric with
                {
                    ChannelKey = "advertised.scalar",
                    Source = measureSource
                    ? new MeasureSource("", AuthoringFunctionIds.BasicPublishBandScalar, settings)
                    : new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], settings)
                }
            };
            var draft = Program() with { Measure = [scalar] };
            if (operation == "duplicate")
            {
                var acquire = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.Acquire).Measure[0]);
                if (casing == "default") acquire = acquire with { Metric = acquire.Metric with { ChannelKey = "metric" } };
                draft = draft with { Measure = [acquire, scalar] };
                draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, acquire.NodeId));
            }
            else draft = operation == "palette"
                ? AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Acquire, Row(draft, scalar.NodeId), false)
                : AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
            var expected = casing == "default" ? "metric_2" : operation == "duplicate" ? "VDC_3" : "VDC_2";
            var path = Path.Combine(directory, "aliases.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            Assert.Equal(alias, Assert.Single(PlanCompiler.FlattenSteps(plan).OfType<PublishBandScalarStep>()).MetricName);
            Assert.Contains(PlanCompiler.FlattenSteps(plan).OfType<AcquireVoltageStep>(), step => step.Channel == expected);
            var results = new OutputListener();
            Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
            Assert.Contains(alias, results.ScalarNames);
            Assert.Contains(expected, results.SampleChannels);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Duplicated_default_scalar_normalizes_only_cloned_output_setting()
    {
        var directory = TemporaryDirectory();
        try
        {
            var scalar = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.BandScalar).Measure[0]);
            var source = Assert.IsType<AlgorithmSource>(scalar.Metric.Source) with
            { Settings = new Dictionary<string, string> { ["Value"] = "1.25", ["Unit"] = "V" } };
            scalar = scalar with { Metric = scalar.Metric with { Source = source, ChannelKey = "metric" } };
            var draft = Program() with { Measure = [scalar] };
            draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, scalar.NodeId));
            Assert.Same(source, Assert.IsType<MetricNode>(draft.Measure[0]).Metric.Source);
            var path = Path.Combine(directory, "aliases.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            Assert.Equal(new[] { "metric", "metric_2" }, PlanCompiler.FlattenSteps(plan).OfType<PublishBandScalarStep>().Select(step => step.MetricName));
            var results = new OutputListener();
            Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
            Assert.Equal(new[] { "metric", "metric_2" }, results.ScalarNames);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(AuthoringFunctionIds.BasicMeanGte, "Mean", false)]
    [InlineData(AuthoringFunctionIds.BasicReportStationHealth, "StationOffset", true)]
    [InlineData(AuthoringFunctionIds.BasicPublishSeriesCompliance, "series.inband.pct", false)]
    [InlineData(AuthoringFunctionIds.BasicBitSweepAcquire, "series.excursion.max", true)]
    public void Typed_fixed_publishers_reserve_aliases_without_validating_incomplete_settings(string function, string alias, bool measureSource)
    {
        if (function == AuthoringFunctionIds.BasicReportStationHealth) alias = ReportStationHealthStep.OffsetMetric;
        var template = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.BandScalar).Measure[0]);
        var settings = new Dictionary<string, string> { ["SampleCount"] = "unfinished" };
        var publisher = template with
        {
            Metric = template.Metric with
            {
                ChannelKey = "advertised.fixed",
                Source = measureSource
            ? new MeasureSource("DMM", function, settings)
            : new AlgorithmSource(function, [], settings)
            }
        };
        var acquire = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.Acquire).Measure[0]);
        acquire = acquire with { Metric = acquire.Metric with { ChannelKey = alias } };
        var draft = Program() with { Measure = [acquire, publisher] };
        draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, acquire.NodeId));
        Assert.Equal(alias + "_2", Assert.IsType<MetricNode>(draft.Measure[1]).Metric.ChannelKey);
    }

    [Fact]
    public void Typed_mean_fixed_alias_avoids_collision_in_compiled_execution()
    {
        var directory = TemporaryDirectory();
        try
        {
            var draft = AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.MeanGte);
            var acquire = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.Acquire).Measure[0]);
            acquire = acquire with { Metric = acquire.Metric with { ChannelKey = "Mean" } };
            draft = draft with { Measure = [acquire, draft.Measure[0]] };
            draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, acquire.NodeId));
            var path = Path.Combine(directory, "aliases.TapPlan");
            new PlanCompiler().Save(draft, path);
            var plan = TestPlan.Load(path);
            Assert.Contains(PlanCompiler.FlattenSteps(plan).OfType<AcquireVoltageStep>(), step => step.Channel == "Mean_2");
            var results = new OutputListener();
            Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
            Assert.Contains("Mean", results.ScalarNames);
            Assert.Contains("Mean_2", results.SampleChannels);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Default_scalar_duplicate_preserves_unfinished_numeric_text()
    {
        var scalar = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.BandScalar).Measure[0]);
        var source = Assert.IsType<AlgorithmSource>(scalar.Metric.Source) with
        { Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["Value"] = "unfinished" } };
        scalar = scalar with { Metric = scalar.Metric with { Source = source, ChannelKey = "metric" } };
        var draft = Program() with { Measure = [scalar] };
        draft = AuthoringSequenceOperations.Duplicate(draft, Row(draft, scalar.NodeId));
        var clone = Assert.IsType<AlgorithmSource>(Assert.IsType<MetricNode>(draft.Measure[1]).Metric.Source);
        Assert.Equal("unfinished", clone.Settings["Value"]);
        Assert.Equal("metric_2", clone.Settings["MetricName"]);
        Assert.False(source.Settings.ContainsKey("MetricName"));
    }

    private static RawStepNode Scalar(string directory, string alias)
    {
        AuthoringPluginSearch.Search();
        var step = new PublishBandScalarStep { MetricName = alias, Value = 2, Unit = "V" };
        var template = Assert.IsType<MetricNode>(AuthoringRecipeCatalog.Apply(Program(), AuthoringRecipeIds.BandScalar).Measure[0]).Metric;
        PresentationAttach.Apply(step, template with { ChannelKey = "advertised.scalar", Limits = new LimitSpec(1, 3, null) });
        var plan = new TestPlan(); plan.ChildTestSteps.Add(step);
        var path = Path.Combine(directory, "imported.TapPlan"); plan.Save(path);
        var xml = XDocument.Load(path).Descendants().First(element => element.Name.LocalName == "TestStep");
        Assert.Contains(xml.Descendants(), element => element.Name.LocalName == "MetricName" && element.Value == alias);
        Assert.Contains(xml.Descendants(), element => element.Name.LocalName.EndsWith("ChannelKey", StringComparison.Ordinal) && element.Value == "advertised.scalar");
        return new RawStepNode(step.GetType().FullName!, xml.ToString(SaveOptions.DisableFormatting)) { NodeId = step.Id };
    }

    private static ProgramDraft Program() => AuthoringRecipeCatalog.CreateProgram("aliases") with
    {
        Setup = [],
        Cleanup = new CleanupPolicy(false, [])
    };
    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ht-raw-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private sealed class OutputListener : ResultListener
    {
        public List<string> ScalarNames { get; } = [];
        public List<string> SampleChannels { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            var column = result.Name switch { "Scalar" => "Name", "Sample" => "Channel", _ => null };
            if (column is null) return;
            var destination = result.Name == "Scalar" ? ScalarNames : SampleChannels;
            foreach (var value in result.Columns.Single(value => value.Name == column).Data)
                destination.Add(Convert.ToString(value) ?? string.Empty);
        }
    }
}
