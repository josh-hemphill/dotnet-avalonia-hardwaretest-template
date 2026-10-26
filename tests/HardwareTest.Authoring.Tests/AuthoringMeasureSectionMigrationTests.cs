using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringMeasureSectionMigrationTests : IDisposable
{
    [Fact]
    public void Layout_intent_uses_a_new_schema_so_older_writers_cannot_silently_drop_it()
    {
        var draft = LegacyDraft("layout-version", false);
        _store.Save(AuthoringDocumentDto.FromDraft(draft));
        using var saved = JsonDocument.Parse(File.ReadAllBytes(_store.GetDocumentPath(draft.PlanId)));
        Assert.True(saved.RootElement.GetProperty("schemaVersion").GetInt32() > 2);
        Assert.Equal(AuthoringDocumentDto.CurrentSchemaVersion, saved.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(saved.RootElement.GetProperty("hasConfigurableMeasureSections").GetBoolean());
        var current = Open();
        Assert.IsNotType<SectionNode>(Assert.Single(current.SelectedProgram!.Measure));
        Assert.False(current.Workspace!.IsReadOnly);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Existing_source_versions_remain_readable_and_only_upgrade_on_save(int version)
    {
        var draft = LegacyDraft("older-source", false);
        var source = AuthoringDocumentDto.FromDraft(draft) with { SchemaVersion = version };
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(source, AuthoringDocumentJsonContext.Default.AuthoringDocumentDto))!.AsObject();
        json.Remove("hasConfigurableMeasureSections");
        var path = _store.GetDocumentPath(draft.PlanId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString());
        var before = File.ReadAllBytes(path);
        var vm = Open();
        Assert.IsType<SectionNode>(Assert.Single(vm.SelectedProgram!.Measure));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(vm.SaveAll().Succeeded);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(AuthoringDocumentDto.CurrentSchemaVersion, saved.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(saved.RootElement.GetProperty("hasConfigurableMeasureSections").GetBoolean());
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-measure-migration-" + Guid.NewGuid().ToString("N"));
    private readonly List<AuthoringWorkspaceViewModel> _models = [];
    private readonly AuthoringDocumentStore _store;

    public AuthoringMeasureSectionMigrationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plans"));
        File.WriteAllText(Path.Combine(_root, "authoring.json"), "{\"schemaVersion\":2,\"displayName\":\"Measure migration\"}");
        _store = new(_root);
    }

    public void Dispose()
    {
        foreach (var model in _models) model.StopRecovery();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_source_migrates_before_baseline_with_stable_identity_and_preserves_child_state(bool empty)
    {
        var draft = LegacyDraft("legacy", empty);
        if (!empty)
        {
            draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(draft.Measure[0].NodeId, "Threshold")] = "-";
            draft.AuthoringState.FormulaIntent[draft.Measure[0].NodeId] = FormulaDeploymentIntent.Explore;
        }
        var source = AuthoringDocumentDto.FromDraft(draft, revision: 12) with { HasConfigurableMeasureSections = false };
        _store.Save(source);
        var bytes = File.ReadAllBytes(_store.GetDocumentPath("legacy"));
        var first = Open();
        var section = Assert.IsType<SectionNode>(Assert.Single(first.SelectedProgram!.Measure));
        Assert.Equal("Measure", section.Name);
        Assert.Equal(draft.Measure.Select(node => node.NodeId), section.Children.Select(node => node.NodeId));
        Assert.Equal(draft.AuthoringState.IncompleteNumericText, first.SelectedProgram.AuthoringState.IncompleteNumericText);
        Assert.Equal(draft.AuthoringState.FormulaIntent, first.SelectedProgram.AuthoringState.FormulaIntent);
        Assert.Equal(12, first.SelectedDocument!.Revision);
        Assert.False(first.HasUnsavedChanges);
        Assert.True(first.HasUncompiledSources);
        Assert.Contains("Save All", first.PackGuardText);
        Assert.False(first.CanPack);
        var second = Open();
        Assert.Equal(section.NodeId, Assert.IsType<SectionNode>(Assert.Single(second.SelectedProgram!.Measure)).NodeId);
        Assert.Equal(bytes, File.ReadAllBytes(_store.GetDocumentPath("legacy")));
    }

    [Fact]
    public void Legacy_custom_sections_keep_their_names_roots_and_ids()
    {
        var draft = LegacyDraft("custom", false);
        var section = new SectionNode("Power subsystem", draft.Measure);
        var source = AuthoringDocumentDto.FromDraft(draft with { Measure = [section] }) with { HasConfigurableMeasureSections = false };
        _store.Save(source);
        var vm = Open();
        var reopened = Assert.IsType<SectionNode>(Assert.Single(vm.SelectedProgram!.Measure));
        Assert.Equal(section.Name, reopened.Name);
        Assert.Equal(section.NodeId, reopened.NodeId);
        Assert.Equal(section.Children.Select(node => node.NodeId), reopened.Children.Select(node => node.NodeId));
        Assert.DoesNotContain(vm.SequenceItems, row => row.Kind == SequenceRowKind.Header && row.Section == SequenceSection.Measure);
    }

    [Fact]
    public void New_plan_measure_is_an_ordinary_renameable_and_removable_persisted_section()
    {
        var draft = new AuthoringPlanInitializer().Construct(new("created") { StartingPoint = PlanStartingPoint.DemoVoltageTask }).Draft;
        var section = Assert.IsType<SectionNode>(Assert.Single(draft.Measure));
        Assert.Equal("Measure", section.Name);
        var row = Assert.Single(AuthoringSequence.Flatten(draft), item => item.NodeId == section.NodeId);
        Assert.True(row.IsSelectable);
        Assert.Equal(SequenceRowKind.Section, row.Kind);
        var renamed = AuthoringSequenceOperations.Rename(draft, row, "Communications");
        Assert.Equal("Communications", Assert.IsType<SectionNode>(Assert.Single(renamed.Measure)).Name);
        _store.Save(AuthoringDocumentDto.FromDraft(renamed));
        Assert.Equal("Communications", Assert.IsType<SectionNode>(Assert.Single(Open().SelectedProgram!.Measure)).Name);
        var renamedRow = AuthoringSequence.Flatten(renamed).Single(item => item.NodeId == section.NodeId);
        var removed = renamed with { Measure = AuthoringSequence.RemoveMeasure(renamed.Measure, renamedRow.IndexPath) };
        Assert.Equal(section.Children.Select(node => node.NodeId), removed.Measure.Select(node => node.NodeId));
        _store.Save(AuthoringDocumentDto.FromDraft(removed));
        var reopened = Open();
        Assert.DoesNotContain(reopened.SelectedProgram!.Measure, node => node is SectionNode);
        Assert.Equal(removed.Measure.Select(node => node.NodeId), reopened.SelectedProgram.Measure.Select(node => node.NodeId));
    }

    [Fact]
    public void Save_all_publishes_clean_pending_migration_and_reopen_does_not_restore_deleted_sections()
    {
        _store.Save(AuthoringDocumentDto.FromDraft(LegacyDraft("save-migration", false)) with { HasConfigurableMeasureSections = false });
        var vm = Open();
        Assert.False(vm.HasUnsavedChanges);
        var result = vm.SaveAll();
        Assert.Contains("save-migration", result.SavedProgramIds);
        Assert.True(_store.Load("save-migration").Document!.HasConfigurableMeasureSections);
        var section = Assert.IsType<SectionNode>(Assert.Single(vm.SelectedProgram!.Measure));
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = section.Children });
        vm.SaveAll();
        Assert.DoesNotContain(Open().SelectedProgram!.Measure, node => node is SectionNode);
    }

    [Fact]
    public void Compiled_layout_marker_preserves_whitespace_only_step_values()
    {
        using var original = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<TestPlan><Message>   </Message><Value xml:space=\"preserve\"> \n </Value></TestPlan>"));
        using var output = new MemoryStream();
        AuthoringMeasureSections.WriteCompiledLayout(original, output);
        output.Position = 0;
        var xml = XDocument.Load(output, LoadOptions.PreserveWhitespace);
        Assert.Equal("   ", xml.Root!.Element("Message")!.Value);
        Assert.Equal(" \n ", xml.Root.Element("Value")!.Value);
        Assert.Equal("true", (string?)xml.Root.Attribute(AuthoringMeasureSections.CompiledLayoutAttribute));
    }

    [Fact]
    public void Compiled_layout_marker_preserves_intentionally_flat_source_less_imports_and_real_sections()
    {
        var compiler = new PlanCompiler();
        var draft = LegacyDraft("compiled", false);
        var path = Path.Combine(_root, "plans", "compiled.TapPlan");
        compiler.Save(draft, path);
        Assert.True(AuthoringMeasureSections.HasCompiledLayout(path));
        Assert.IsType<MetricNode>(Assert.Single(compiler.Load(path).Measure));
        Assert.IsType<MetricNode>(Assert.Single(AuthoringSourceWorkspaceLoader.Load(_root).Programs.Single().Measure));
        var grouped = AuthoringMeasureSections.Initialize(draft);
        compiler.Save(grouped, path);
        var section = Assert.IsType<SectionNode>(Assert.Single(AuthoringSourceWorkspaceLoader.Load(_root).Programs.Single().Measure));
        Assert.Equal(Assert.Single(grouped.Measure).NodeId, section.NodeId);
        compiler.Save(draft with { Measure = [] }, path);
        Assert.Empty(AuthoringSourceWorkspaceLoader.Load(_root).Programs.Single().Measure);
    }

    [Fact]
    public void Unmarked_compiled_import_migrates_once_with_stable_ids_and_requires_explicit_save()
    {
        var path = Path.Combine(_root, "plans", "imported.TapPlan");
        new PlanCompiler().Save(LegacyDraft("imported", false), path);
        var xml = XDocument.Load(path);
        xml.Root!.Attribute(AuthoringMeasureSections.CompiledLayoutAttribute)!.Remove();
        xml.Save(path);
        var first = Open();
        var section = Assert.IsType<SectionNode>(Assert.Single(first.SelectedProgram!.Measure));
        Assert.Equal(section.NodeId, Assert.IsType<SectionNode>(Assert.Single(Open().SelectedProgram!.Measure)).NodeId);
        Assert.True(first.HasUncompiledSources);
        Assert.False(first.CanPack);
        Assert.Contains("imported", first.SaveAll().SavedProgramIds);
        Assert.True(_store.Load("imported").Document!.HasConfigurableMeasureSections);
    }

    [Fact]
    public void Legacy_matching_recovery_is_compared_after_the_same_normalization()
    {
        var source = AuthoringDocumentDto.FromDraft(LegacyDraft("recovery", false)) with
        { HasConfigurableMeasureSections = false, SavedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        _store.Save(source);
        _store.SaveAtPath(_store.GetRecoveryPath("recovery"), source with { SavedAtUtc = DateTimeOffset.UtcNow });
        Assert.Empty(Open().RecoverableProgramIds);
    }

    [Fact]
    public void Removing_a_pending_migration_does_not_leave_a_phantom_save_or_pack_guard()
    {
        _store.Save(AuthoringDocumentDto.FromDraft(LegacyDraft("removed", false)) with { HasConfigurableMeasureSections = false });
        var vm = Open();
        Assert.Contains("Save All", vm.PackGuardText);
        vm.RemoveSelectedProgram();
        var result = vm.SaveAll();
        Assert.Empty(result.SavedProgramIds);
        Assert.Empty(result.Failures);
        Assert.DoesNotContain("configurable test sections", vm.PackGuardText);
        Assert.Empty(vm.Programs);
    }

    [Fact]
    public void Deterministic_wrapper_avoids_existing_step_identity_collisions()
    {
        var draft = LegacyDraft("collision", true);
        var initial = Assert.IsType<SectionNode>(Assert.Single(AuthoringMeasureSections.Initialize(draft).Measure));
        var collision = draft with { Setup = [new OperatorPromptSetup("Prompt", "Confirm") { NodeId = initial.NodeId }] };
        var result = Assert.IsType<SectionNode>(Assert.Single(AuthoringMeasureSections.Initialize(collision).Measure));
        Assert.NotEqual(initial.NodeId, result.NodeId);
        Assert.Equal(result.NodeId, Assert.Single(AuthoringMeasureSections.Initialize(collision).Measure).NodeId);
    }

    private static ProgramDraft LegacyDraft(string id, bool empty)
    {
        var draft = MockDmmDraftFixture.Create(id);
        return empty ? draft : AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Acquire);
    }

    private AuthoringWorkspaceViewModel Open()
    {
        var vm = new AuthoringWorkspaceViewModel(new PlanCompiler());
        _models.Add(vm);
        vm.Open(_root);
        return vm;
    }
}
