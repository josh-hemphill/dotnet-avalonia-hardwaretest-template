using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringEnabledOutputTests
{
    public static IEnumerable<object[]> Cases => new[] { AuthoringRecipeIds.MeanGte, AuthoringRecipeIds.SeriesCompliance, "sweep" }
        .SelectMany(recipe => new[] { false, true }.SelectMany(algorithm => new[] { "default", "off", "on", "on-then-off", "off-then-on" }
            .Select(mode => new object[] { recipe, algorithm, mode })));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Effective_enabled_setting_controls_actual_outputs_and_collision_history(string recipe, bool algorithm, string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ht-enabled-outputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var enabled = mode is "default" or "on" or "off-then-on";
            var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("enabled"), AuthoringRecipeIds.Acquire);
            draft = AuthoringRecipeCatalog.Apply(draft, recipe == "sweep" ? AuthoringRecipeIds.MeanGte : recipe) with { Setup = [], Cleanup = new CleanupPolicy(false, []) };
            var publisher = Assert.IsType<MetricNode>(draft.Measure[1]);
            if (recipe == "sweep") publisher = publisher with
            {
                Metric = new MetricDraft("Sweep", "sweep", "timeseries", "V", null, null,
                new AlgorithmSource(AuthoringFunctionIds.BasicBitSweepAcquire, [], new Dictionary<string, string>()) { InstrumentSlot = "DMM" })
            };
            var source = Assert.IsType<AlgorithmSource>(publisher.Metric.Source);
            var settings = new Dictionary<string, string>(source.Settings, StringComparer.Ordinal);
            if (recipe == "sweep") { settings["PublishSummaries"] = "true"; settings["BitCount"] = "2"; settings["IntervalMs"] = "5"; settings["ScriptedValues"] = "1.25,1.26"; }
            if (mode is "on-then-off" or "off-then-on") settings.Add("Enabled", mode == "on-then-off" ? "TRUE" : "false");
            if (mode != "default") settings.Add("enabled", enabled ? "TrUe" : "FALSE");
            publisher = publisher with
            {
                Metric = publisher.Metric with
                {
                    Source = algorithm
                ? source with { Settings = settings }
                : new MeasureSource(source.InstrumentSlot ?? "", source.AlgorithmId, settings)
                }
            };
            draft = draft with { Measure = [draft.Measure[0], publisher] };
            var baseline = Execute(draft, directory);
            Assert.Equal(enabled ? Aliases(recipe) : [], baseline);
            var session = new AuthoringDocumentSession(draft);
            session.ApplyEdit("checkpoint", current => AuthoringRecipeCatalog.Apply(current, AuthoringRecipeIds.Acquire));
            session.Undo();
            var before = session.Snapshot;
            var revision = session.Revision;
            var addedRecipe = recipe == "sweep" ? AuthoringRecipeIds.SeriesCompliance : recipe;
            if (enabled)
            {
                Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("duplicate", current =>
                    AuthoringSequenceOperations.Duplicate(current, Row(current, publisher.NodeId))));
                Assert.Throws<AuthoringWorkspaceException>(() => session.ApplyEdit("add", current => AuthoringRecipeCatalog.Apply(current, addedRecipe)));
                Assert.True(before.ContentEquals(session.Snapshot));
                Assert.Equal(revision, session.Revision);
                Assert.True(session.CanRedo);
            }
            else
            {
                session.ApplyEdit("duplicate", current => AuthoringSequenceOperations.Duplicate(current, Row(current, publisher.NodeId)));
                Assert.Empty(Execute(session.Draft, directory));
                session.Undo();
                Assert.True(before.ContentEquals(session.Snapshot));
                session.ApplyEdit("insert", current => AuthoringSequenceOperations.Insert(current, addedRecipe, Row(current, publisher.NodeId), false));
                Assert.Equal(Aliases(addedRecipe), Execute(session.Draft, directory));
                session.Undo();
                session.ApplyEdit("legacy add", current => AuthoringRecipeCatalog.Apply(current, addedRecipe));
                Assert.Equal(Aliases(addedRecipe), Execute(session.Draft, directory));
                session.Undo();
                Assert.True(before.ContentEquals(session.Snapshot));
                Assert.True(session.CanRedo);
            }
            Assert.Equal(mode == "default" ? 0 : 1, settings.Count(pair => pair.Key == "enabled"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_output_projection_preserves_unfinished_numeric_settings(bool algorithm)
    {
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("incomplete"), AuthoringRecipeIds.MeanGte);
        var node = Assert.IsType<MetricNode>(draft.Measure[0]);
        var source = Assert.IsType<AlgorithmSource>(node.Metric.Source);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["SampleCount"] = "unfinished", ["eNaBlEd"] = "false" };
        node = node with
        {
            Metric = node.Metric with
            {
                Source = algorithm ? source with { Settings = settings }
            : new MeasureSource(source.InstrumentSlot ?? "", source.AlgorithmId, settings)
            }
        };
        draft = draft with { Measure = [node] };
        var duplicate = AuthoringSequenceOperations.Duplicate(draft, Row(draft, node.NodeId));
        var clone = Assert.IsType<MetricNode>(duplicate.Measure[1]);
        var copied = clone.Metric.Source is AlgorithmSource value ? value.Settings : Assert.IsType<MeasureSource>(clone.Metric.Source).Settings;
        Assert.Equal("unfinished", copied["SampleCount"]);
        Assert.Equal("false", copied["eNaBlEd"]);
        Assert.NotNull(AuthoringRecipeCatalog.Apply(duplicate, AuthoringRecipeIds.MeanGte));
    }

    private static string[] Aliases(string recipe) => recipe == AuthoringRecipeIds.MeanGte ? ["Mean"]
        : ["series.inband.pct", "series.excursion.max", "series.outband.ms"];
    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
    private static List<string> Execute(ProgramDraft draft, string directory)
    {
        var path = Path.Combine(directory, "enabled.TapPlan");
        new PlanCompiler().Save(draft, path);
        var plan = TestPlan.Load(path);
        var listener = new Scalars();
        Assert.Equal(Verdict.Pass, plan.Execute([listener], []).Verdict);
        return listener.Names;
    }
    private sealed class Scalars : ResultListener
    {
        public List<string> Names { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            if (result.Name != "Scalar") return;
            foreach (var name in result.Columns.Single(column => column.Name == "Name").Data) Names.Add(Convert.ToString(name) ?? "");
        }
    }
}
