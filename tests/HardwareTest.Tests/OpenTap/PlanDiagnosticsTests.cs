using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class PlanDiagnosticsTests
{
    [Fact]
    public async Task PlanDiagnostics_sample_leaf_paths_are_unique()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        var leaves = Leaves(session.StepTree).ToList();
        Assert.NotEmpty(leaves);
        Assert.Equal(leaves.Count, leaves.Select(l => l.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task PlanDiagnostics_board_demo_nested_sections_expose_leaves()
    {
        var session = new OpenTapSession();
        await session.LoadBoardDemoProgramAsync();
        var leaves = Leaves(session.StepTree).ToList();
        Assert.Contains(leaves, l => l.Path.Contains("3V3 Rail", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(leaves, l => l.Path.Contains("Bus Stress", StringComparison.OrdinalIgnoreCase));
        Assert.True(leaves.Count >= 6, $"Expected several board-demo leaves, got {leaves.Count}");
    }

    [Fact]
    public async Task PlanDiagnostics_flat_leaves_has_no_groups()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        Assert.All(session.StepTree, n => Assert.Empty(n.Children));
        Assert.Equal(3, session.StepTree.Count);
    }

    [Fact]
    public async Task PlanDiagnostics_deep_nest_exposes_leaf_under_nested_path()
    {
        // Run board chrome only shows Stages → Sections → Nested; deeper groups still surface as leaves via path.
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.DeepNestName);
        var leaf = Leaves(session.StepTree).First(l => l.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Level4", leaf.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Level1", leaf.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostics_duplicate_names_use_path_qualified_selection()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.DuplicateNamesName);
        var acquires = Leaves(session.StepTree)
            .Where(l => l.Name.Equals("Acquire", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, acquires.Count);
        Assert.Equal(2, acquires.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(acquires, a => a.Path.Contains("Bank A", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(acquires, a => a.Path.Contains("Bank B", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanDiagnostics_empty_group_yields_no_step_rows_under_scope()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.EmptyGroupName);
        var empty = Flatten(session.StepTree)
            .First(n => n.Name.Equals("Empty Section", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(empty.Children);
        Assert.DoesNotContain(Leaves(session.StepTree), l => l.Path.StartsWith(empty.Path + "/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Leaves(session.StepTree), l => l.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanDiagnostics_run_selected_does_not_enable_whole_plan()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-SEL-MASK", Family: "demo"));

        var identity = Leaves(session.StepTree)
            .First(n => n.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        var summary = await session.RunSelectionAsync(identity.Path);

        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");

        var executedLeaves = summary.Steps
            .Where(s => !string.IsNullOrWhiteSpace(s.StepPath))
            .Select(s => s.StepPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Contains(executedLeaves, p => p.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            executedLeaves,
            p => p.Contains("Acquire", StringComparison.OrdinalIgnoreCase)
                 && !p.Contains("Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanDiagnostics_run_selected_without_safe_shutdown_still_completes()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.NoSafeShutdownName);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-NO-SS", Family: "demo"));

        var leaf = Leaves(session.StepTree).First(l => l.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
        var summary = await session.RunSelectionAsync(leaf.Path);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
        Assert.DoesNotContain(
            Flatten(session.StepTree),
            n => n.Name.Equals("Safe Shutdown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanShapeFixtures_SaveAllBeside_writes_tapplans()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opentap-fixtures-" + Guid.NewGuid().ToString("N"));
        try
        {
            PlanShapeFixtures.SaveAllBeside(dir);
            foreach (var (fileName, _) in PlanShapeFixtures.All)
            {
                Assert.True(File.Exists(Path.Combine(dir, fileName)), fileName);
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static IEnumerable<OpenTapStepNode> Flatten(IEnumerable<OpenTapStepNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<OpenTapStepNode> Leaves(IEnumerable<OpenTapStepNode> nodes)
        => Flatten(nodes).Where(n => n.Children.Count == 0);
}
