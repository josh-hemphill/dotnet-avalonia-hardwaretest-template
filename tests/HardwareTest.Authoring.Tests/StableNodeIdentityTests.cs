using HardwareTest.Authoring;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class StableNodeIdentityTests
{
    [Fact]
    public void Rename_insert_and_repeat_unwrap_keep_existing_node_and_row_identity()
    {
        var draft = AuthoringRecipeCatalog.Apply(
            AuthoringRecipeCatalog.CreateProgram("identity"), AuthoringRecipeIds.Acquire);
        var original = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
        var rows = AuthoringSequence.Flatten(draft);
        var renamed = AuthoringSequence.MutateMeasure(draft.Measure, [0], node =>
            new MetricNode(((MetricNode)node).Metric with { Name = "renamed", ChannelKey = "renamed" }));
        var inserted = new MetricNode(original.Metric with { ChannelKey = "inserted" });
        var repeat = new RepeatNode(2, renamed);
        var changed = draft with { Measure = [inserted, repeat] };
        var unwrapped = changed with { Measure = AuthoringSequence.RemoveMeasure(changed.Measure, [1]) };
        Assert.Equal(original.NodeId, unwrapped.Measure[1].NodeId);
        Assert.NotEqual(original.NodeId, inserted.NodeId);
        var previous = Assert.Single(rows, row => row.NodeId == original.NodeId);
        var current = Assert.Single(AuthoringSequence.Flatten(unwrapped), row => row.NodeId == original.NodeId);
        Assert.Equal(previous.Key, current.Key);
        Assert.Equal(new[] { 1 }, current.IndexPath);
        Assert.Equal(draft.Setup[0].NodeId, unwrapped.Setup[0].NodeId);
        Assert.Equal(draft.Cleanup.NodeId, unwrapped.Cleanup.NodeId);
    }

    [Fact]
    public void With_updates_preserve_setup_measure_and_cleanup_ids()
    {
        var setup = new OperatorPromptSetup("name", "message");
        var measure = new RepeatNode(2, []);
        var cleanup = new CleanupPolicy(true, "DMM");
        Assert.NotEqual(Guid.Empty, setup.NodeId);
        Assert.Equal(setup.NodeId, (setup with { Message = "changed" }).NodeId);
        Assert.Equal(measure.NodeId, (measure with { Count = 3 }).NodeId);
        Assert.Equal(cleanup.NodeId, (cleanup with { IncludeSafeShutdown = false }).NodeId);
    }

    [Fact]
    public void Compiler_maps_step_ids_and_preserves_supported_nested_nodes_after_reload()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("stable");
        foreach (var recipe in new[] { AuthoringRecipeIds.Prompt, AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Repeat })
        {
            draft = AuthoringRecipeCatalog.Apply(draft, recipe);
        }
        draft = draft with { Setup = [.. draft.Setup, new OperatorInputSetup("input", "title", "message", "serial", null)] };
        var directory = Path.Combine(Path.GetTempPath(), "ht-stable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "stable.TapPlan");
            var compiler = new PlanCompiler();
            compiler.Save(draft, path);
            var stepIds = PlanCompiler.FlattenSteps(TestPlan.Load(path)).Select(step => step.Id).ToHashSet();
            var ids = draft.Setup.Select(action => action.NodeId)
                .Concat(MeasureIds(draft.Measure)).ToArray();
            Assert.All(ids, id => Assert.Contains(id, stepIds));
            var loaded = compiler.Load(path);
            Assert.Equal(ids, loaded.Setup.Select(action => action.NodeId).Concat(MeasureIds(loaded.Measure)));
            compiler.Save(loaded, path);
            var reloaded = compiler.Load(path);
            Assert.Equal(ids, reloaded.Setup.Select(action => action.NodeId).Concat(MeasureIds(reloaded.Measure)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<Guid> MeasureIds(IEnumerable<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node.NodeId;
            if (node is RepeatNode repeat)
            {
                foreach (var id in MeasureIds(repeat.Children))
                {
                    yield return id;
                }
            }
        }
    }
}
