using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringHardwareDefinitionTests
{
    [AvaloniaFact]
    public void Visible_adapter_binding_review_cancel_apply_history_and_save_reopen_preserve_type_address_and_step_ids()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("binding"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        var ids = vm.SelectedProgram!.Measure.Select(n => n.NodeId).ToArray();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        Click(fixture, "Load selected binding");
        var adapter = fixture.Control<ComboBox>("Hardware adapter type");
        Assert.Equal(vm.SelectedInstrument!.TypeId, Assert.IsType<AuthoringInstrumentAdapter>(adapter.SelectedItem).TypeId);
        fixture.Type(fixture.Control<TextBox>("Hardware adapter address"), "MOCK::BENCH");
        Click(fixture, "Review binding change");
        var dialog = Assert.Single(window.OwnedWindows);
        var details = string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("MOCK::BENCH", details); Assert.Contains("Measure[0]", details);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel destructive operation", dialog));
        Assert.NotEqual("MOCK::BENCH", vm.SelectedInstrument.VisaAddress);
        Click(fixture, "Review binding change"); dialog = Assert.Single(window.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("Apply reviewed binding", dialog));
        Assert.Equal("MOCK::BENCH", vm.SelectedInstrument.VisaAddress);
        vm.Undo(); Assert.NotEqual("MOCK::BENCH", vm.SelectedInstrument.VisaAddress);
        vm.Redo(); Assert.Equal("MOCK::BENCH", vm.SelectedInstrument.VisaAddress);
        Assert.Equal(ids, vm.SelectedProgram!.Measure.Select(n => n.NodeId));
        Assert.True(vm.SaveAll().Succeeded);
        var reopened = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences); reopened.Open(fixture.WorkspaceRoot); reopened.SelectProgram("binding");
        Assert.Equal(vm.SelectedInstrument.TypeId, reopened.SelectedInstrument!.TypeId);
        Assert.Equal("MOCK::BENCH", reopened.SelectedInstrument.VisaAddress);
        Assert.Equal(ids, reopened.SelectedProgram!.Measure.Select(n => n.NodeId));
        var table = Assert.Single(reopened.HardwareRows);
        Assert.Contains("Measure[0]", table.Usage); Assert.Contains("HardwareTest Basic", table.PackageStatus); Assert.Contains("Safe shutdown", table.Cleanup);
    }

    [AvaloniaFact]
    public void Global_definition_roundtrip_stable_identity_explicit_membership_isolation_and_reviewed_removal_history()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace(); var vm = fixture.ViewModel;
        vm.CreateDemoProgram("one"); vm.CreateDemoProgram("two"); vm.SelectProgram("one");
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 6; AuthoringUiFixture.Drain();
        fixture.Type(fixture.Control<TextBox>("New hardware definition name"), "BENCH");
        fixture.Type(fixture.Control<TextBox>("Hardware adapter address"), "MOCK::GLOBAL");
        Click(fixture, "Add workspace hardware definition");
        var definition = Assert.Single(vm.HardwareDefinitions); Assert.NotEqual(Guid.Empty, definition.Id);
        Assert.DoesNotContain(vm.Programs.SelectMany(p => p.Instruments), i => i.SlotName == "BENCH");
        Click(fixture, "Include definition in selected program");
        Assert.Contains(vm.SelectedProgram!.Instruments, i => i.SlotName == "BENCH" && i.VisaAddress == "MOCK::GLOBAL");
        Assert.DoesNotContain(vm.Programs.Single(p => p.PlanId == "two").Instruments, i => i.SlotName == "BENCH");
        Click(fixture, "Load workspace hardware definition");
        fixture.Type(fixture.Control<TextBox>("Hardware adapter address"), "MOCK::UPDATED");
        Click(fixture, "Update workspace hardware definition");
        Assert.Equal(definition.Id, Assert.Single(vm.HardwareDefinitions).Id);
        Assert.Equal("MOCK::GLOBAL", vm.SelectedInstrument!.VisaAddress);
        vm.UndoWorkspace(); Assert.Equal("MOCK::GLOBAL", Assert.Single(vm.HardwareDefinitions).Address);
        vm.RedoWorkspace(); Assert.Equal("MOCK::UPDATED", Assert.Single(vm.HardwareDefinitions).Address);
        Click(fixture, "Review definition removal"); var dialog = Assert.Single(window.OwnedWindows);
        Assert.Contains("one", string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove hardware definition", dialog));
        Assert.Empty(vm.HardwareDefinitions); Assert.Contains(vm.SelectedProgram.Instruments, i => i.SlotName == "BENCH");
        vm.UndoWorkspace(); Assert.Equal(definition.Id, Assert.Single(vm.HardwareDefinitions).Id);
        Assert.True(vm.SaveAll().Succeeded);
        var reopened = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences); reopened.Open(fixture.WorkspaceRoot);
        Assert.Equal(definition.Id, Assert.Single(reopened.HardwareDefinitions).Id);
        Assert.Equal("MOCK::UPDATED", Assert.Single(reopened.HardwareDefinitions).Address);
        Assert.Contains(reopened.Programs.Single(p => p.PlanId == "one").Instruments, i => i.SlotName == "BENCH" && i.VisaAddress == "MOCK::GLOBAL");
        Assert.DoesNotContain(reopened.Programs.Single(p => p.PlanId == "two").Instruments, i => i.SlotName == "BENCH");
    }

    [AvaloniaFact]
    public void Adapter_configuration_rejection_and_workspace_catalog_creation_do_not_change_program_membership()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true); var window = fixture.Show(); fixture.OpenRememberedWorkspace(); var vm = fixture.ViewModel;
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 6; AuthoringUiFixture.Drain();
        var before = vm.SelectedProgram;
        fixture.Type(fixture.Control<TextBox>("New required field"), "fixtureId"); Click(fixture, "Add required field");
        Assert.Same(before, vm.SelectedProgram); Assert.Contains("fixtureId", vm.RequiredFieldOptions);
        fixture.Type(fixture.Control<TextBox>("New hardware definition name"), "BAD");
        fixture.Type(fixture.Control<TextBox>("Hardware adapter address"), "MOCK::BAD");
        fixture.Type(fixture.Control<TextBox>("Hardware adapter timeout"), "100");
        Click(fixture, "Add workspace hardware definition");
        Assert.Contains("does not support", vm.Error); Assert.Empty(vm.HardwareDefinitions); Assert.Same(before, vm.SelectedProgram);
    }

    [AvaloniaFact]
    public void Changing_visible_adapter_to_current_library_roundtrips_lifecycle_type_address_and_configuration()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var manifest = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot).Manifest;
        manifest.Dependencies.Add(new AuthoringPackageDependency { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "0.1.1" });
        File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "authoring.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest));
        var window = fixture.Show(); fixture.OpenRememberedWorkspace(); var vm = fixture.ViewModel;
        CurrentHardwareUiFixture.Prepare(fixture);
        vm.InitializePlan(new("physical") { Instruments = [new("DMM", typeof(HardwareTest.OpenTap.Plugins.Basic.MockDmmInstrument).FullName!, "MOCK::0")], IdentityInstrumentSlot = "DMM" });
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        Click(fixture, "Load selected binding");
        var selected = AuthoringInstrumentCatalog.All.Single(a => a.TypeId == CurrentHardwareUiFixture.TypeId);
        fixture.Control<ComboBox>("Hardware adapter type").SelectedItem = selected; AuthoringUiFixture.Drain();
        fixture.Type(fixture.Control<TextBox>("Hardware adapter address"), "TCPIP::192.0.2.1::INSTR");
        fixture.Type(fixture.Control<TextBox>("Hardware adapter timeout"), "1234");
        Click(fixture, "Review binding change"); var dialog = Assert.Single(window.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("Apply reviewed binding", dialog));
        Assert.Equal(selected.TypeId, vm.SelectedInstrument!.TypeId);
        Assert.Equal("1234", vm.SelectedInstrument.Settings["IoTimeoutMilliseconds"]);
        Assert.True(vm.SaveAll().Succeeded);
        var reopened = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences); reopened.Open(fixture.WorkspaceRoot); reopened.OpenTapHomeOverride = vm.OpenTapHomeOverride; reopened.SelectProgram("physical");
        Assert.Equal(selected.TypeId, reopened.SelectedInstrument!.TypeId);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", reopened.SelectedInstrument.VisaAddress);
        Assert.Equal("1234", reopened.SelectedInstrument.Settings["IoTimeoutMilliseconds"]);
        Assert.Equal("DMM", reopened.InstrumentSlots.Single());
    }

    [AvaloniaFact]
    public void Bound_hardware_package_status_refreshes_with_selected_home_and_same_home_is_noop()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        WritePackagePayload(Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath),
            AuthoringInstrumentCatalog.All.Single(a => a.RequiredPackage == "HardwareTest Basic"), includePayload: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        var vm = fixture.ViewModel;
        var program = vm.SelectedProgram;
        var table = fixture.Control<ItemsControl>("Hardware binding table");
        var availableRows = table.ItemsSource;
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "HardwareTest Basic: available");

        var missingHome = Path.Combine(fixture.WorkspaceRoot, "missing-home");
        vm.OpenTapHomeOverride = missingHome; AuthoringUiFixture.Drain();
        var missingRows = table.ItemsSource;
        Assert.NotSame(availableRows, missingRows);
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("package metadata", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "HardwareTest Basic: available");

        vm.OpenTapHomeOverride = " " + missingHome + " "; AuthoringUiFixture.Drain();
        Assert.Same(missingRows, table.ItemsSource);
        vm.OpenTapHomeOverride = string.Empty; AuthoringUiFixture.Drain();
        Assert.NotSame(missingRows, table.ItemsSource);
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "HardwareTest Basic: available");
        Assert.Same(program, vm.SelectedProgram);
        Assert.False(vm.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData("HardwareTest Basic", "missing metadata")]
    [InlineData("HardwareTest Basic", "missing payload")]
    [InlineData("HardwareTest Basic", "valid")]
    [InlineData("InstrumentComponents.OpenTap", "missing metadata")]
    [InlineData("InstrumentComponents.OpenTap", "missing payload")]
    [InlineData("InstrumentComponents.OpenTap", "valid")]
    public void Bound_hardware_table_inspects_workspace_default_home_when_override_is_empty(string package, string state)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        if (package == AuthoringInstrumentCatalog.LibraryPackage) CurrentHardwareUiFixture.Discover(fixture);
        var adapter = AuthoringInstrumentCatalog.All.Single(a => package == AuthoringInstrumentCatalog.LibraryPackage ? a.TypeId == CurrentHardwareUiFixture.TypeId : a.RequiredPackage == package);
        var defaultHome = Path.Combine(fixture.WorkspaceRoot, OpenTapHomeBootstrapper.DefaultHomeRelativePath);
        if (state != "missing metadata") WritePackagePayload(defaultHome, adapter, includePayload: state == "valid");
        if (package == AuthoringInstrumentCatalog.LibraryPackage)
        {
            var manifest = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot).Manifest;
            manifest.Dependencies.Add(new AuthoringPackageDependency { Package = package, Version = "0.1.1" });
            File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "authoring.json"),
                System.Text.Json.JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest));
        }
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        if (package == AuthoringInstrumentCatalog.LibraryPackage)
        {
            // A discovered library adapter remains visible while this home reports missing payloads.
            vm.NewInstrumentSlot = "SUPPLY"; vm.SelectedNewInstrumentType = adapter; vm.NewInstrumentVisa = "TCPIP::192.0.2.1::INSTR";
            vm.ReplaceSelected(vm.SelectedProgram! with { Instruments = [new("SUPPLY", adapter.TypeId, "TCPIP::192.0.2.1::INSTR")] });
        }
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        Assert.Equal(string.Empty, vm.OpenTapHomeOverride);
        var table = fixture.Control<ItemsControl>("Hardware binding table");
        var status = Assert.Single(table.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text?.StartsWith(package + ":", StringComparison.Ordinal) == true).Text!;
        if (state == "valid") Assert.Equal(package + ": available", status);
        else
        {
            Assert.Contains(Path.GetFullPath(defaultHome), status);
            Assert.Contains(state == "missing metadata" ? "package metadata is missing" : "missing", status);
            Assert.NotEqual(package + ": available", status);
        }
    }

    [AvaloniaFact]
    public void Bound_hardware_table_prefers_valid_override_to_missing_workspace_default_home()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var adapter = AuthoringInstrumentCatalog.All.Single(a => a.RequiredPackage == "HardwareTest Basic");
        var overrideHome = Path.Combine(fixture.WorkspaceRoot, "selected-home");
        WritePackagePayload(overrideHome, adapter, includePayload: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        var table = fixture.Control<ItemsControl>("Hardware binding table");
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("package metadata is missing", StringComparison.Ordinal) == true);
        fixture.ViewModel.OpenTapHomeOverride = overrideHome; AuthoringUiFixture.Drain();
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "HardwareTest Basic: available");
        fixture.ViewModel.OpenTapHomeOverride = string.Empty; AuthoringUiFixture.Drain();
        Assert.Contains(table.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("package metadata is missing", StringComparison.Ordinal) == true);
    }

    private static void WritePackagePayload(string home, AuthoringInstrumentAdapter adapter, bool includePayload)
    {
        if (AuthoringInstrumentCatalog.IsLibrary(adapter.TypeId))
        {
            CurrentHardwareUiFixture.WritePackage(home, includePayload);
            return;
        }
        var directory = Path.Combine(home, "Packages", adapter.RequiredPackage);
        Directory.CreateDirectory(directory);
        var files = new[] { adapter.AssemblyFile }.Concat(adapter.RequiredPayloadFiles).Distinct().ToArray();
        new System.Xml.Linq.XElement("Package", new System.Xml.Linq.XAttribute("Name", adapter.RequiredPackage),
            new System.Xml.Linq.XElement("Files", files.Select(file => new System.Xml.Linq.XElement("File", new System.Xml.Linq.XAttribute("Path", file)))))
            .Save(Path.Combine(directory, "package.xml"));
        if (includePayload)
            foreach (var file in files) File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(directory, file));
    }

    private static void Click(AuthoringUiFixture fixture, string name)
    {
        var button = fixture.Control<Button>(name); button.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(button);
    }
}
