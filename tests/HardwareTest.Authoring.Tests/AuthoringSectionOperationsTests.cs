using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringSectionOperationsTests
{
    [Fact]
    public void Large_plan_sections_preserve_steps_identity_and_lifecycle_anchors()
    {
        var draft = AuthoringRecipeCatalog.Apply(MockDmmDraftFixture.Create("sections"), AuthoringRecipeIds.Acquire);
        var template = Assert.IsType<MetricNode>(draft.Measure[0]);
        var steps = Enumerable.Range(0, 120).Select(i => (MeasureNode)new MetricNode(template.Metric with
        { Name = $"Check {i}", ChannelKey = $"channel_{i}" })).ToArray();
        draft = draft with { Measure = steps };
        draft = AuthoringSequenceOperations.AddSection(draft, "  Power supply  ");
        var group = Assert.IsType<SectionNode>(draft.Measure[^1]);
        Assert.Equal("Power supply", group.Name);
        var staleRow = Row(draft, steps[0].NodeId);
        draft = AuthoringSequenceOperations.MoveToSection(draft, staleRow, group.NodeId);
        draft = AuthoringSequenceOperations.AddSection(draft, "Communications");
        var destination = draft.Measure[^1].NodeId;
        draft = AuthoringSequenceOperations.MoveToSection(draft, staleRow, destination);
        Assert.Equal(120, AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).Count());
        Assert.Equal(steps[0].NodeId, Assert.Single(Assert.IsType<SectionNode>(draft.Measure[^1]).Children).NodeId);
        draft = AuthoringSequenceOperations.Rename(draft, Row(draft, destination), "Radio");
        draft = AuthoringSequenceOperations.Move(draft, Row(draft, destination), -1);
        var removal = Row(draft, destination);
        draft = draft with { Measure = AuthoringSequence.RemoveMeasure(draft.Measure, removal.IndexPath) };
        Assert.Equal(120, AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).Count());
        Assert.Equal(steps.Select(node => node.NodeId).Order(), AuthoringSequence.Flatten(draft)
            .Where(row => row.Kind == SequenceRowKind.Metric).Select(row => row.NodeId!.Value).Order());
        Assert.Equal(SequenceSection.Setup, AuthoringSequence.Flatten(draft)[0].Section);
        Assert.Equal(SequenceSection.Cleanup, AuthoringSequence.Flatten(draft)[^1].Section);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.AddSection(draft, " \t"));
    }

    [Fact]
    public void Empty_section_insertion_and_cross_section_formula_inputs_are_supported()
    {
        var draft = AuthoringSequenceOperations.AddSection(MockDmmDraftFixture.Create("sections"), "Power");
        var first = draft.Measure[0].NodeId;
        draft = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Acquire, Row(draft, first), false, "DMM");
        draft = AuthoringSequenceOperations.AddSection(draft, "Analysis");
        var second = draft.Measure[1].NodeId;
        draft = AuthoringSequenceOperations.Insert(draft, AuthoringRecipeIds.Formula, Row(draft, second), false);
        var formula = Assert.IsType<MetricNode>(Assert.Single(Assert.IsType<SectionNode>(draft.Measure[1]).Children));
        Assert.Equal(new[] { "VDC" }, Assert.IsType<ExpressionAlgorithm>(formula.Metric.Source).InputChannelKeys);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.Move(draft, Row(draft, second), -1));
        var producer = Assert.Single(Assert.IsType<SectionNode>(draft.Measure[0]).Children);
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.MoveToSection(draft, Row(draft, producer.NodeId), second));
        var moved = AuthoringSequenceOperations.MoveToSection(draft, Row(draft, formula.NodeId), first);
        Assert.Equal(2, Assert.IsType<SectionNode>(moved.Measure[0]).Children.Count);
        Assert.Empty(Assert.IsType<SectionNode>(moved.Measure[1]).Children);
    }

    [Fact]
    public void Nested_sections_cannot_cycle_or_change_repeat_scope()
    {
        var nested = new SectionNode("Nested", []);
        var outer = new SectionNode("System", [nested]);
        var loopSection = new SectionNode("Loop system", []);
        var draft = MockDmmDraftFixture.Create("sections") with { Measure = [outer, new RepeatNode(2, [loopSection])] };
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.MoveToSection(draft, Row(draft, outer.NodeId), nested.NodeId));
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.MoveToSection(draft, Row(draft, nested.NodeId), loopSection.NodeId));
        Assert.Throws<AuthoringWorkspaceException>(() => AuthoringSequenceOperations.MoveToSection(draft,
            AuthoringSequence.Flatten(draft).First(row => row.Kind == SequenceRowKind.Setup), outer.NodeId));
    }

    [Fact]
    public void Section_editing_history_collapse_and_selection_survive_undo_redo()
    {
        var root = Path.Combine(Path.GetTempPath(), "section-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Sections\",\"plansDirectory\":\".\"}");
            var vm = new AuthoringWorkspaceViewModel();
            vm.Open(root);
            vm.CreateDemoProgram("sections");
            vm.NewSectionName = "Power";
            vm.AddSection();
            var section = vm.SelectedSequence!.NodeId;
            Assert.True(vm.HasSectionEditor);
            vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
            vm.InsertSelectedRecipe();
            Assert.Null(vm.Error);
            var child = vm.SelectedSequence!.NodeId;
            Assert.NotEqual(section, child);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == section));
            vm.ToggleSelectedSection();
            Assert.DoesNotContain(vm.SequenceItems, row => row.NodeId == child);
            vm.Undo();
            Assert.True(vm.HasSectionEditor);
            vm.Redo();
            Assert.Equal(child, vm.SelectedSequence!.NodeId);
            Assert.Contains(vm.SequenceItems, row => row.NodeId == child);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == section));
            vm.SequenceRename = "Power rails";
            vm.RenameSelectedSequence();
            Assert.Equal("Power rails", vm.SelectedSequence!.Label);
            vm.RemoveSelectedSequence();
            Assert.Contains(vm.SequenceItems, row => row.NodeId == child);
            Assert.DoesNotContain(vm.SequenceItems, row => row.NodeId == section);
            vm.Undo();
            Assert.Contains(vm.SequenceItems, row => row.NodeId == section);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Section_name_editor_tracks_selection_and_history_without_discarding_typing()
    {
        var root = Path.Combine(Path.GetTempPath(), "section-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Sections\",\"plansDirectory\":\".\"}");
            var vm = new AuthoringWorkspaceViewModel();
            vm.Open(root);
            vm.CreateDemoProgram("section-names");
            vm.NewSectionName = "Power rails";
            vm.AddSection();
            var powerId = vm.SelectedSequence!.NodeId;
            Assert.Equal("Power rails", vm.SequenceRename);
            vm.NewSectionName = "Communications";
            vm.AddSection();
            Assert.Equal("Communications", vm.SequenceRename);
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == powerId));
            Assert.Equal("Power rails", vm.SequenceRename);
            vm.SequenceRename = "Power supplies";
            vm.RecipeSearch = "Acquire";
            vm.ToggleSelectedSection();
            Assert.Equal("Power supplies", vm.SequenceRename);
            vm.RenameSelectedSequence();
            Assert.Equal("Power supplies", vm.SequenceRename);
            vm.Undo();
            Assert.Equal("Power rails", vm.SequenceRename);
            vm.Redo();
            Assert.Equal("Power supplies", vm.SequenceRename);
        }
        finally { Directory.Delete(root, true); }
    }

    private static SequenceRow Row(ProgramDraft draft, Guid id) => AuthoringSequence.Flatten(draft).Single(row => row.NodeId == id);
}
