using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBitSweepOutputTests
{
    public static IEnumerable<object[]> Cases => new[] { "measure", "algorithm", "raw" }
        .SelectMany(form => new[] { "default", "off", "on", "on-then-off", "off-then-on" }.Select(mode => new object[] { form, mode }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sweep_summary_outputs_follow_effective_boolean_setting_in_edits_and_compiled_execution(string form, string mode)
    {
        var directory = TemporaryDirectory();
        try
        {
            var enabled = mode is "on" or "off-then-on";
            var draft = Program(Sweep(form == "algorithm", Settings(mode)));
            if (form == "raw")
            {
                var path = Path.Combine(directory, "sweeps.TapPlan");
                new PlanCompiler().Save(draft, path);
                var xml = XDocument.Load(path).Descendants().Single(element => element.Name.LocalName == "TestStep" &&
                    element.Attribute("type")?.Value.Contains(nameof(BitSweepAcquireStep), StringComparison.Ordinal) == true);
                if (mode == "default") xml.Elements().Where(element => element.Name.LocalName == "PublishSummaries").Remove();
                draft = draft with
                {
                    Measure = [new RawStepNode(typeof(BitSweepAcquireStep).FullName!, xml.ToString(SaveOptions.DisableFormatting))
                    { NodeId = draft.Measure[0].NodeId }]
                };
            }
            var results = Execute(draft, directory, enabled);
            Assert.Equal(enabled ? 3 : 0, results.Scalars.Count);
            Assert.Contains("sweep", results.Samples);
            var session = new AuthoringDocumentSession(draft);
            session.ApplyEdit("add sample", current => AuthoringRecipeCatalog.Apply(current, AuthoringRecipeIds.Acquire));
            session.Undo();
            Assert.True(session.CanRedo);
            var before = session.Snapshot;
            var revision = session.Revision;
            if (enabled)
            {
                Assert.Contains("runtime output", Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("series", current =>
                    AuthoringSequenceOperations.Insert(current, AuthoringRecipeIds.SeriesCompliance, Row(current, current.Measure[0].NodeId), false))).Message, StringComparison.Ordinal);
                if (form != "raw") Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("duplicate", current =>
                    AuthoringSequenceOperations.Duplicate(current, Row(current, current.Measure[0].NodeId))));
                Assert.True(before.ContentEquals(session.Snapshot));
                Assert.Equal(revision, session.Revision);
                Assert.True(session.CanRedo);
            }
            else
            {
                if (form == "raw") session.ApplyEdit("add sample", current => AuthoringRecipeCatalog.Apply(current, AuthoringRecipeIds.Acquire));
                var sampleId = session.Draft.Measure[form == "raw" ? 1 : 0].NodeId;
                session.ApplyEdit("duplicate", current => AuthoringSequenceOperations.Duplicate(current, Row(current, sampleId)));
                var clone = Execute(session.Draft, directory, false);
                Assert.Contains(form == "raw" ? "VDC_2" : "sweep_2", clone.Samples);
                Assert.Empty(clone.Scalars);
                session.Undo();
                if (form == "raw") session.Undo();
                Assert.True(before.ContentEquals(session.Snapshot));
                session.ApplyEdit("series", current => AuthoringSequenceOperations.Insert(current, AuthoringRecipeIds.SeriesCompliance,
                    Row(current, current.Measure[0].NodeId), false));
                var combined = Execute(session.Draft, directory, false);
                Assert.Equal(new[] { "series.inband.pct", "series.excursion.max", "series.outband.ms" }, combined.Scalars);
                Assert.Contains("sweep", combined.Samples);
                session.Undo();
                Assert.True(before.ContentEquals(session.Snapshot));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sweep_boolean_projection_does_not_parse_unrelated_incomplete_numeric_settings(bool algorithm)
    {
        var settings = Settings("on-then-off");
        settings["BitCount"] = "unfinished";
        var draft = Program(Sweep(algorithm, settings));
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, draft.Measure[0].NodeId));
        var clone = Assert.IsType<MetricNode>(duplicate.Measure[1]);
        Assert.Equal("sweep_2", clone.Metric.ChannelKey);
        Assert.Equal("unfinished", (clone.Metric.Source is AlgorithmSource source ? source.Settings : Assert.IsType<MeasureSource>(clone.Metric.Source).Settings)["BitCount"]);
        Assert.Equal("unfinished", settings["BitCount"]);
        Assert.NotNull(AuthoringRecipeCatalog.Apply(duplicate, AuthoringRecipeIds.SeriesCompliance));
    }

    private static Dictionary<string, string> Settings(string mode)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["BitCount"] = "2", ["IntervalMs"] = "5", ["ScriptedValues"] = "1.25,1.26" };
        if (mode is "on-then-off" or "off-then-on") settings.Add("PublishSummaries", mode == "on-then-off" ? "TRUE" : "false");
        if (mode != "default") settings.Add("publishsummaries", mode is "on" or "off-then-on" ? "TrUe" : "FALSE");
        return settings;
    }
    private static MetricNode Sweep(bool algorithm, IReadOnlyDictionary<string, string> settings)
        => new(new MetricDraft("Sweep", "sweep", "timeseries", "V", null, null, algorithm
            ? new AlgorithmSource(AuthoringFunctionIds.BasicBitSweepAcquire, [], settings) { InstrumentSlot = "DMM" }
            : new MeasureSource("DMM", AuthoringFunctionIds.BasicBitSweepAcquire, settings)));
    private static ProgramDraft Program(MetricNode sweep) => AuthoringRecipeCatalog.CreateProgram("sweeps") with
    { Setup = [], Cleanup = new CleanupPolicy(false, []), Measure = [sweep] };
    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
    private static Outputs Execute(ProgramDraft draft, string directory, bool summaries)
    {
        var path = Path.Combine(directory, "sweeps.TapPlan");
        new PlanCompiler().Save(draft, path);
        var plan = TestPlan.Load(path);
        Assert.All(PlanCompiler.FlattenSteps(plan).OfType<BitSweepAcquireStep>(), step => Assert.Equal(summaries, step.PublishSummaries));
        var results = new Outputs();
        Assert.Equal(Verdict.Pass, plan.Execute([results], []).Verdict);
        return results;
    }
    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ht-sweep-outputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
    private sealed class Outputs : ResultListener
    {
        public List<string> Scalars { get; } = [];
        public List<string> Samples { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            var column = result.Name switch { "Scalar" => "Name", "Sample" => "Channel", _ => null };
            if (column is null) return;
            foreach (var value in result.Columns.Single(value => value.Name == column).Data)
                (result.Name == "Scalar" ? Scalars : Samples).Add(Convert.ToString(value) ?? string.Empty);
        }
    }
}
