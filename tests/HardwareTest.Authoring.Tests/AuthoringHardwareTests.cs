using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringHardwareTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-hardware-" + Guid.NewGuid().ToString("N"));
    private readonly AuthoringWorkspaceViewModel _vm = new();

    public AuthoringHardwareTests()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) directory = directory.Parent;
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(directory!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
        _vm.Open(_root); _vm.CreateDemoProgram("one"); _vm.CreateDemoProgram("two"); _vm.SelectProgram("one");
    }

    [Fact]
    public void Reviewed_edit_rejects_changed_document_and_preserves_unknown_usage()
    {
        _vm.ApplyRecipe(AuthoringRecipeIds.Acquire); _vm.LoadHardwareEditor(); _vm.HardwareEditAddress = "MOCK::UPDATED";
        var review = _vm.PrepareHardwareEdit(); _vm.DisplayName = "changed";
        Assert.Contains("changed since review", Assert.Throws<AuthoringWorkspaceException>(() => _vm.ApplyHardwareEdit(review)).Message);
        _vm.ReplaceSelected(_vm.SelectedProgram! with { Measure = [new RawStepNode("unknown", "<step/>")] });
        Assert.Contains("unresolved", Assert.Throws<AuthoringWorkspaceException>(() => _vm.PrepareHardwareEdit()).Message);
        Assert.DoesNotContain(_vm.SelectedProgram!.Instruments, i => i.VisaAddress == "MOCK::UPDATED");
    }

    [Fact]
    public void Adapter_type_and_configuration_rejection_preserves_atomic_manifest_and_membership()
    {
        _vm.LoadHardwareEditor(); _vm.HardwareEditType = AuthoringInstrumentCatalog.All.Single(a => a.RequiredPackage == "HardwareTest VISA");
        _vm.HardwareEditAddress = "TCPIP::bench";
        Assert.Contains("dependency", Assert.Throws<AuthoringWorkspaceException>(() => _vm.PrepareHardwareEdit()).Message);
        _vm.HardwareEditType = AuthoringInstrumentCatalog.All[0]; _vm.HardwareEditTimeout = "12";
        var before = AuthoringWorkspaceState.Capture(_vm.Workspace!.Manifest, _vm.Programs);
        _vm.NewInstrumentSlot = "BAD";
        Assert.Contains("does not support", Assert.Throws<AuthoringWorkspaceException>(() => _vm.AddHardwareDefinition()).Message);
        Assert.True(before.ContentEquals(AuthoringWorkspaceState.Capture(_vm.Workspace.Manifest, _vm.Programs)));
    }

    [Fact]
    public void Catalog_review_names_default_report_fallback_and_program_kind_reset_then_history_restores_all()
    {
        _vm.NewReportKind = "trace"; _vm.AddWorkspaceReportKind();
        Assert.DoesNotContain("trace", _vm.IncludedReportKinds);
        _vm.SetReportKindIncluded("trace", true); _vm.DefaultReportKind = "trace";
        var review = _vm.PrepareReportKindDeletion("trace");
        var affected = Assert.Single(review.AffectedPrograms);
        Assert.Equal("one", affected.PlanId); Assert.Contains(affected.Nodes, node => node.Contains("trace → status", StringComparison.Ordinal));
        _vm.ApplyCatalogDeletion(review); Assert.Equal("status", _vm.DefaultReportKind);
        _vm.UndoWorkspace(); Assert.Equal("trace", _vm.DefaultReportKind); _vm.RedoWorkspace(); Assert.Equal("status", _vm.DefaultReportKind);
        _vm.NewProgramKind = "inspection"; _vm.AddWorkspaceProgramKind(); _vm.ProgramKind = "inspection";
        var kind = _vm.PrepareProgramKindDeletion("inspection"); Assert.Contains("resets to dut", Assert.Single(kind.AffectedPrograms).Nodes[0]);
        _vm.ApplyCatalogDeletion(kind); Assert.Equal("dut", _vm.ProgramKind); _vm.UndoWorkspace(); Assert.Equal("inspection", _vm.ProgramKind);
        Assert.Null(_vm.Programs.Single(p => p.PlanId == "two").Sidecar.ProgramKind);
    }

    [Fact]
    public void Stale_workspace_definition_removal_does_not_mutate_membership_or_catalog()
    {
        _vm.NewInstrumentSlot = "BENCH"; _vm.HardwareEditAddress = "MOCK::BENCH"; _vm.AddHardwareDefinition();
        var id = Assert.Single(_vm.HardwareDefinitions).Id;
        _vm.IncludeHardwareDefinition(); var review = _vm.PrepareHardwareDefinitionRemoval();
        Assert.Contains("existing 'BENCH' membership and binding remain", Assert.Single(review.ProgramConsequences));
        _vm.SelectProgram("two"); _vm.DisplayName = "changed";
        Assert.Contains("changed since review", Assert.Throws<AuthoringWorkspaceException>(() => _vm.ApplyHardwareDefinitionRemoval(review)).Message);
        Assert.Equal(id, Assert.Single(_vm.HardwareDefinitions).Id);
        Assert.Contains(_vm.Programs.Single(p => p.PlanId == "one").Instruments, i => i.SlotName == "BENCH");
    }

    [Fact]
    public void Definition_change_invalidates_prior_review_even_when_catalog_was_already_dirty_and_same_values_are_noop()
    {
        _vm.NewInstrumentSlot = "BENCH"; _vm.HardwareEditAddress = "MOCK::BEFORE"; _vm.AddHardwareDefinition();
        Assert.True(_vm.WorkspaceCatalogDirty);
        var review = _vm.PrepareHardwareDefinitionRemoval();
        _vm.HardwareEditAddress = "MOCK::AFTER"; _vm.UpdateHardwareDefinition();
        Assert.Contains("changed since review", Assert.Throws<AuthoringWorkspaceException>(() => _vm.ApplyHardwareDefinitionRemoval(review)).Message);
        _vm.UpdateHardwareDefinition(); // Same values must not add another history entry.
        _vm.UndoWorkspace(); Assert.Equal("MOCK::BEFORE", Assert.Single(_vm.HardwareDefinitions).Address);
        _vm.UndoWorkspace(); Assert.Empty(_vm.HardwareDefinitions);
    }

    [Fact]
    public void Empty_workspace_can_administer_global_definitions_without_a_selected_program()
    {
        var root = Path.Combine(_root, "empty"); Directory.CreateDirectory(root);
        File.Copy(Path.Combine(_root, "authoring.json"), Path.Combine(root, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); Assert.Null(vm.SelectedProgram);
        vm.NewRequiredField = "fixtureId"; vm.AddWorkspaceRequiredField();
        vm.NewInstrumentSlot = "BENCH"; vm.HardwareEditAddress = "MOCK::EMPTY"; vm.AddHardwareDefinition();
        Assert.Empty(vm.Programs); Assert.Single(vm.HardwareDefinitions);
        Assert.True(vm.SaveAll().Succeeded); vm.UndoWorkspace(); Assert.Empty(vm.HardwareDefinitions);
    }

    [Fact]
    public void Definition_fingerprint_tracks_identity_type_address_and_configuration_with_canonical_key_order()
    {
        _vm.NewInstrumentSlot = "BENCH"; _vm.HardwareEditAddress = "MOCK::BENCH"; _vm.AddHardwareDefinition();
        var definition = Assert.Single(_vm.HardwareDefinitions) with { Settings = new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" } };
        _vm.Workspace!.Manifest.Catalogs!.Hardware[0] = definition;
        var review = _vm.PrepareHardwareDefinitionRemoval();
        _vm.Workspace.Manifest.Catalogs.Hardware[0] = definition with { Settings = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" } };
        Assert.Equal(review.ContentFingerprint, _vm.PrepareHardwareDefinitionRemoval().ContentFingerprint);
        _vm.Workspace.Manifest.Catalogs.Hardware[0] = definition with { Settings = new Dictionary<string, string> { ["A"] = "changed", ["B"] = "2" } };
        Assert.NotEqual(review.ContentFingerprint, _vm.PrepareHardwareDefinitionRemoval().ContentFingerprint);
        Assert.Contains("changed since review", Assert.Throws<AuthoringWorkspaceException>(() => _vm.ApplyHardwareDefinitionRemoval(review)).Message);
    }

    [Fact]
    public void Published_schema_covers_empty_and_populated_serialized_hardware_catalogs()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) directory = directory.Parent;
        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(directory!.FullName, "plans", "opentap", "authoring.schema.json")));
        var catalogs = schema.RootElement.GetProperty("properties").GetProperty("catalogs");
        var catalogProperties = catalogs.GetProperty("properties");
        Assert.False(catalogs.GetProperty("additionalProperties").GetBoolean());
        var hardware = catalogProperties.GetProperty("hardware");
        Assert.Equal("array", hardware.GetProperty("type").GetString());
        Assert.False(catalogs.TryGetProperty("required", out var catalogRequired)
            && catalogRequired.EnumerateArray().Any(p => p.GetString() == "hardware"));
        var item = hardware.GetProperty("items");
        Assert.Equal("object", item.GetProperty("type").GetString());
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "address", "id", "name", "settings", "typeId" },
            item.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
        var itemProperties = item.GetProperty("properties");
        Assert.Equal("uuid", itemProperties.GetProperty("id").GetProperty("format").GetString());
        foreach (var property in new[] { "id", "name", "typeId", "address" })
            Assert.Equal("string", itemProperties.GetProperty(property).GetProperty("type").GetString());
        var settings = itemProperties.GetProperty("settings");
        Assert.Equal("object", settings.GetProperty("type").GetString());
        Assert.Equal("string", settings.GetProperty("additionalProperties").GetProperty("type").GetString());

        var manifest = new AuthoringManifest { SchemaVersion = AuthoringSchemaVersions.Manifest, Catalogs = new() };
        foreach (var populated in new[] { false, true })
        {
            if (populated) manifest.Catalogs.Hardware.Add(new AuthoringHardwareDefinition(Guid.NewGuid(), "BENCH", "registered.Type", "TCPIP::bench",
                new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "1234" }));
            using var serialized = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
                manifest, AuthoringJsonContext.Default.AuthoringManifest));
            var serializedCatalogs = serialized.RootElement.GetProperty("catalogs");
            foreach (var property in serializedCatalogs.EnumerateObject())
                Assert.True(catalogProperties.TryGetProperty(property.Name, out _), $"Serialized catalog property {property.Name} must be declared in the closed schema.");
            var definitions = serializedCatalogs.GetProperty("hardware");
            Assert.Equal(populated ? 1 : 0, definitions.GetArrayLength());
            foreach (var definition in definitions.EnumerateArray())
            {
                foreach (var property in definition.EnumerateObject())
                    Assert.True(itemProperties.TryGetProperty(property.Name, out _), $"Serialized hardware property {property.Name} must be declared in the closed schema.");
                foreach (var required in item.GetProperty("required").EnumerateArray())
                    Assert.True(definition.TryGetProperty(required.GetString()!, out _));
                Assert.True(Guid.TryParse(definition.GetProperty("id").GetString(), out _));
                foreach (var property in new[] { "name", "typeId", "address" })
                    Assert.Equal(System.Text.Json.JsonValueKind.String, definition.GetProperty(property).ValueKind);
                foreach (var setting in definition.GetProperty("settings").EnumerateObject())
                    Assert.Equal(System.Text.Json.JsonValueKind.String, setting.Value.ValueKind);
            }
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
