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
