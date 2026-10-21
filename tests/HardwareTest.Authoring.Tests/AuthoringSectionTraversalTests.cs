using System.Text;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSectionTraversalTests
{
    [Fact]
    public void Hardware_usage_cleanup_and_dependency_index_include_nested_section_children()
    {
        var input = Input();
        var nested = new SectionNode("Power rails", [input]);
        var section = new SectionNode("Power", [new RepeatNode(2, [nested])]);
        var draft = Draft([section]) with { Cleanup = new CleanupPolicy(true, [], true) };
        Assert.False(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(draft));
        Assert.Contains(AuthoringInstrumentUsage.DescribeSlotUsage(draft, "DMM"), usage => usage.Contains("Acquire"));
        Assert.Contains("DMM", AuthoringCleanup.ResolveSlots(draft));
        var index = AuthoringDependencyIndex.Build(draft);
        Assert.Contains(index.Nodes, node => node.NodeId == nested.NodeId && !node.IsOpaque);
        Assert.Contains(index.Nodes, node => node.NodeId == input.NodeId && node.InstrumentSlots.Contains("DMM"));
        var retargeted = AuthoringInstrumentUsage.RetargetSlot(draft, "DMM", "POWER");
        Assert.Equal("POWER", Assert.IsType<MeasureSource>(Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(retargeted.Measure)).Source).InstrumentSlot);
        Assert.Contains(AuthoringDependencyIndex.Build(retargeted).Nodes, node => node.NodeId == input.NodeId);
        Assert.Contains(AuthoringIssueService.GetIssues(retargeted), issue => issue.NodeId == input.NodeId && issue.Code == "MISSING_INSTRUMENT");
    }

    [Fact]
    public void Preview_and_formula_resolution_share_channels_across_nested_and_sibling_sections()
    {
        var input = Input();
        var mean = Mean();
        var draft = Draft([new SectionNode("Acquisition", [new SectionNode("Voltage", [input])]), new SectionNode("Analysis", [mean])]);
        var tiles = BoardPreviewBuilder.Build(draft);
        Assert.Equal(new[] { input.NodeId, mean.NodeId }, tiles.Select(tile => tile.NodeId));
        Assert.All(tiles, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        Assert.Equal(FormulaDeploymentStatusKind.DeployableRecipe, FormulaDeploymentClassifier.Classify(mean.Metric, draft).Kind);
    }

    [Fact]
    public void Sections_do_not_allow_formula_inputs_to_escape_a_repeat_scope()
    {
        var input = Input();
        var mean = Mean();
        var draft = Draft([new SectionNode("Acquisition", [new RepeatNode(2, [input])]), new SectionNode("Analysis", [mean])]);
        Assert.Equal(FormulaDeploymentStatusKind.MissingRequirements, FormulaDeploymentClassifier.Classify(mean.Metric, draft).Kind);
        Assert.Empty(Assert.Single(BoardPreviewBuilder.Build(draft), tile => tile.NodeId == mean.NodeId).Preview.CannedSamples);
    }

    [Fact]
    public void Exploration_projection_and_build_map_preserve_section_and_child_identity()
    {
        var input = Input();
        var exploration = Mean();
        var raw = new RawStepNode("Vendor.Step", "<TestStep />");
        var nested = new SectionNode("Analysis", [exploration]);
        var section = new SectionNode("Power", [input, raw, nested]);
        var draft = Draft([section]);
        draft.AuthoringState.FormulaIntent[exploration.NodeId] = FormulaDeploymentIntent.Explore;
        Assert.Equal(new[] { exploration.NodeId }, AuthoringFormulaDeployment.ExcludedNodes(draft));
        var projected = AuthoringFormulaDeployment.Project(draft);
        var projectedSection = Assert.IsType<SectionNode>(Assert.Single(projected.Measure));
        Assert.Equal(section.NodeId, projectedSection.NodeId);
        Assert.Empty(Assert.IsType<SectionNode>(projectedSection.Children[2]).Children);
        Assert.Equal(3, section.Children.Count);
        Assert.Single(nested.Children);
        var generated = Guid.NewGuid();
        var xml = new XDocument(new XElement("TestPlan", new[] { section.NodeId, input.NodeId, raw.NodeId, nested.NodeId, generated }
            .Select(id => new XElement("TestStep", new XAttribute("Id", id)))));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
        var map = AuthoringBuildService.CompileMap(projected, stream);
        Assert.Equal(section.NodeId, Assert.Single(map, entry => entry.StepId == section.NodeId).NodeId);
        Assert.Equal(input.NodeId, Assert.Single(map, entry => entry.StepId == input.NodeId).NodeId);
        Assert.Equal(nested.NodeId, Assert.Single(map, entry => entry.StepId == nested.NodeId).NodeId);
        Assert.Null(Assert.Single(map, entry => entry.StepId == raw.NodeId).NodeId);
        Assert.Null(Assert.Single(map, entry => entry.StepId == generated).NodeId);
    }

    [Fact]
    public void Opaque_lifecycle_steps_in_sections_still_require_reimport()
    {
        var raw = new RawStepNode("InstrumentComponents.OpenTap.SafeShutdownStep", "<TestStep />");
        var draft = Draft([new SectionNode("Hidden lifecycle", [new RepeatNode(2, [raw])])]);
        Assert.True(AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(draft));
        Assert.Contains(AuthoringIssueService.GetIssues(draft), issue => issue.Code == "LIBRARY_LIFECYCLE_REIMPORT" && issue.NodeId == raw.NodeId);
    }

    private static MetricNode Input() => new(new MetricDraft("Acquire", "input", PresentationRoles.Timeseries, "V", null, null,
        new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
    private static MetricNode Mean() => new(new MetricDraft("Average", "average", PresentationRoles.Scalar, "V", new LimitSpec(null, null, 0), null,
        new ExpressionAlgorithm(["input"], "mean(input)")));
    private static ProgramDraft Draft(IReadOnlyList<MeasureNode> nodes) => MockDmmDraftFixture.Create("sections") with { Measure = nodes };
}
