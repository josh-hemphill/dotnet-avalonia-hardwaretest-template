using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringGuidedRecoveryTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Resume_preserves_reused_resource_content_after_catalog_reorder_removal_or_edit(int mutation)
    {
        using var fixture = Loaded(visa: true); var vm = fixture.ViewModel;
        var adapter = AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "VISA DMM"); PreparePackage(fixture, adapter);
        AddDefinition(vm, adapter, "A", "TCPIP::first::INSTR", "1111");
        AddDefinition(vm, adapter, "B", "TCPIP::original::INSTR", "2222");
        AddDefinition(vm, adapter, "C", "TCPIP::different::INSTR", "3333");
        var dialog = Start(fixture); Set(fixture, dialog, "Stable plan ID", "retained-binding");
        Next(fixture, dialog); fixture.Control<ComboBox>("Hardware choice", dialog).SelectedIndex = 4;
        Set(fixture, dialog, "Instrument slot", "My bench");
        Assert.Equal("TCPIP::original::INSTR", fixture.Control<TextBox>("Instrument address", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", dialog));
        vm.SelectedHardwareDefinition = vm.HardwareDefinitions.Single(definition => definition.Name == (mutation == 0 ? "A" : "B"));
        if (mutation == 2)
        {
            vm.LoadHardwareDefinitionEditor(); vm.HardwareEditTimeout = "9999"; vm.UpdateHardwareDefinition();
        }
        else vm.ApplyHardwareDefinitionRemoval(vm.PrepareHardwareDefinitionRemoval());
        AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Assert.Equal("My bench", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        Assert.Equal("TCPIP::original::INSTR", fixture.Control<TextBox>("Instrument address", dialog).Text);
        if (mutation != 0) Assert.Contains("retained", fixture.Control<TextBlock>("Hardware readiness", dialog).Text, StringComparison.OrdinalIgnoreCase);
        for (var stage = 0; stage < 4; stage++) Next(fixture, dialog);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var resource = Assert.Single(vm.SelectedProgram!.Instruments);
        Assert.Equal("My bench", resource.SlotName); Assert.Equal(adapter.TypeId, resource.TypeId);
        Assert.Equal("TCPIP::original::INSTR", resource.VisaAddress); Assert.Equal("2222", resource.Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(["My bench"], vm.SelectedProgram.Cleanup.InstrumentSlots);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_saved_source_or_compiled_input_cannot_escape_guidance_or_document_notifications(bool compiled)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel;
        PreparePackage(fixture, AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM"));
        vm.InitializePlan(new PlanInitializationRequest("locked-guide") { StartingPoint = PlanStartingPoint.DemoVoltageTask, IdentityInstrumentSlot = "DMM" });
        AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Save and check draft"));
        Assert.Contains("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
        var path = compiled ? vm.Workspace!.TapPlanPaths.Single(file => Path.GetFileNameWithoutExtension(file) == "locked-guide")
            : new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("locked-guide");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(Record.Exception(() => AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"))));
            Assert.Contains("unavailable", fixture.Control<TextBlock>("Guidance feedback").Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Restore", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.DoesNotContain("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.Null(Record.Exception(() => vm.DisplayName = "Edit survives unavailable input"));
            Assert.Null(Record.Exception(AuthoringUiFixture.Drain));
            Assert.Null(Record.Exception(() => vm.ReportError("Ordinary error feedback"))); AuthoringUiFixture.Drain();
            var session = vm.SelectedDocument;
            Assert.Null(Record.Exception(() => AuthoringUiFixture.Click(fixture.Control<Button>("Save all"))));
            Assert.NotNull(vm.LastSaveAllResult); Assert.False(vm.LastSaveAllResult.Succeeded);
            Assert.Same(session, vm.SelectedDocument);
            Assert.Equal("Edit survives unavailable input", vm.DisplayName); Assert.True(vm.CanUndo);
        }
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.DoesNotContain("unavailable or unsafe", fixture.Control<TextBlock>("Guidance feedback").Text);
    }

    [AvaloniaFact]
    public void Unsafe_linked_source_reports_recovery_without_reading_or_modifying_target()
    {
        if (OperatingSystem.IsWindows()) return; // Symlink creation requires privileges unavailable on some Windows hosts.
        using var fixture = Loaded(); var vm = fixture.ViewModel;
        vm.InitializePlan(new PlanInitializationRequest("unsafe-guide")); AuthoringUiFixture.Drain();
        var source = new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("unsafe-guide");
        var backup = source + ".retained"; File.Move(source, backup); var bytes = File.ReadAllBytes(backup);
        try
        {
            File.CreateSymbolicLink(source, backup);
            Assert.Null(Record.Exception(() => AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"))));
            Assert.Contains("unsafe", fixture.Control<TextBlock>("Guidance feedback").Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Restore", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.Equal(bytes, File.ReadAllBytes(backup));
        }
        finally { File.Delete(source); File.Move(backup, source); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_resume_overrides_future_skip_for_current_session_without_touching_preferences(bool empty)
    {
        using var fixture = Loaded(); var window = fixture.Window!;
        if (empty)
        {
            var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "empty-guide");
            Assert.True(await window.CreateWorkspaceAsync(new(root, "Empty", "Empty guide")));
        }
        const string future = """{ "schemaVersion": 999, "skipGuidance": true, "futureOnly": "preserve exact bytes" }""";
        File.WriteAllText(fixture.Preferences.FilePath, future); fixture.Preferences.Load();
        var ordinary = Start(fixture); Assert.Equal("New test plan", ordinary.Title);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", ordinary));
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        if (empty)
        {
            var guided = Assert.IsType<PlanInitializationWindow>(Assert.Single(window.OwnedWindows));
            Assert.Equal("First voltage test", guided.Title); Assert.False(fixture.Control<Button>("Skip optional guidance", guided).IsEnabled);
            AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", guided));
        }
        else
        {
            Assert.True(window.FindControl<Border>("GuidanceHost")!.IsVisible);
            var guided = Start(fixture); Assert.Equal("First voltage test", guided.Title);
            AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", guided));
        }
        Assert.True(fixture.Preferences.IsReadOnly); Assert.True(fixture.ViewModel.SkipGuidance);
        Assert.Equal(future, File.ReadAllText(fixture.Preferences.FilePath));
        Assert.True(await window.ReopenWorkspaceAsync()); AuthoringUiFixture.Drain();
        ordinary = Start(fixture); Assert.Equal("New test plan", ordinary.Title);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", ordinary));
        Assert.Equal(future, File.ReadAllText(fixture.Preferences.FilePath));
    }

    [AvaloniaTheory]
    [InlineData("threshold")]
    [InlineData("binding")]
    [InlineData("sidecar")]
    public async Task Completion_requires_current_content_to_match_second_instance_saved_source_and_artifacts(string changed)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel;
        PreparePackage(fixture, AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM"));
        vm.InitializePlan(new PlanInitializationRequest("external-guide")
        {
            StartingPoint = PlanStartingPoint.DemoVoltageTask,
            IdentityInstrumentSlot = "DMM",
            Measurement = new(AuthoringRecipeIds.MeanGte, "DMM") { ThresholdText = "1.2" }
        });
        AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Save and check draft"));
        Assert.Contains("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
        var original = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!); var originalSession = vm.SelectedDocument;
        vm.StopRecovery();
        var external = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences);
        var other = new MainWindow(external, new TestLifecycleInteraction());
        try
        {
            other.Show(); external.Open(fixture.WorkspaceRoot); external.SelectProgram("external-guide");
            external.OpenTapHomeOverride = vm.OpenTapHomeOverride; external.StopRecovery();
            if (changed == "threshold")
            {
                external.SelectMeasure(0); AuthoringUiFixture.Drain(); fixture.Control<TextBox>("Threshold", other).Text = "2.75";
            }
            else if (changed == "binding") external.VisaAddress = "MOCK::SECOND";
            else
            {
                other.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
                fixture.Control<TextBox>("Display name", other).Text = "Saved by the second instance";
            }
            AuthoringUiFixture.Drain(); Assert.True(external.HasUnsavedChanges);
            AuthoringUiFixture.Click(fixture.Control<Button>("Save plan", other));
            Assert.False(external.HasUnsavedChanges); Assert.Null(external.Error);
            var saved = new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("external-guide").Document!;
            Assert.False(saved.RequiresCompilation);
            if (changed == "threshold") Assert.Equal(2.75, Assert.IsType<MetricNode>(Assert.Single(saved.ToDraft().Measure)).Metric.Limits!.Threshold);
            else if (changed == "binding") Assert.Equal("MOCK::SECOND", Assert.Single(saved.Instruments).VisaAddress);
            else Assert.Equal("Saved by the second instance", saved.Sidecar.DisplayName);
            var replacement = AuthoringDocumentSnapshot.Capture(saved.ToDraft()); Assert.False(original.ContentEquals(replacement));
            Assert.True(replacement.ContentEquals(AuthoringDocumentSnapshot.Capture(external.SelectedProgram!)));
            var path = external.Workspace!.TapPlanPaths.Single(file => Path.GetFileNameWithoutExtension(file) == "external-guide");
            Assert.Equal(saved.CompiledPlanHash, AuthoringDocumentStore.ComputeHash(path));
            Assert.Equal(saved.CompiledSidecarHash, AuthoringDocumentStore.ComputeHash(Path.ChangeExtension(path, ".program.json")));
            Assert.False(vm.HasUnsavedChanges); Assert.Same(originalSession, vm.SelectedDocument);
            Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            Assert.DoesNotContain("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.Contains("Reopen", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.Contains("differs", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            vm.DisplayName = "Local unsaved input"; AuthoringUiFixture.Drain();
            Assert.True(vm.HasUnsavedChanges); Assert.True(vm.CanUndo); Assert.Same(originalSession, vm.SelectedDocument);
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            Assert.DoesNotContain("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
            Assert.Equal("Local unsaved input", vm.DisplayName); vm.Undo(); AuthoringUiFixture.Drain();
            Assert.False(vm.HasUnsavedChanges); Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            Assert.True(await fixture.Window!.ReopenWorkspaceAsync()); AuthoringUiFixture.Drain();
            Assert.True(replacement.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            Assert.Contains("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
        }
        finally { other.Close(); external.StopRecovery(); external.StopOperations(); AuthoringUiFixture.Drain(); }
    }

    private static AuthoringUiFixture Loaded(bool visa = false)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        if (visa)
        {
            var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
            workspace.Manifest.Dependencies.Add(new AuthoringPackageDependency { Package = OpenTapHomeBootstrapper.VisaPackageName, Version = "^0.1.0" });
            AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        }
        fixture.Show(); fixture.OpenRememberedWorkspace(); return fixture;
    }
    private static void AddDefinition(AuthoringWorkspaceViewModel vm, AuthoringInstrumentAdapter adapter, string name, string address, string timeout)
    {
        vm.HardwareEditType = adapter; vm.NewInstrumentSlot = name; vm.HardwareEditAddress = address; vm.HardwareEditTimeout = timeout;
        vm.AddHardwareDefinition(); AuthoringUiFixture.Drain();
    }
    private static void PreparePackage(AuthoringUiFixture fixture, AuthoringInstrumentAdapter adapter)
    {
        var home = Path.Combine(fixture.ViewModel.Workspace!.Root, "available-home");
        var package = Path.Combine(home, "Packages", adapter.RequiredPackage); Directory.CreateDirectory(package);
        var files = new[] { adapter.AssemblyFile }.Concat(adapter.RequiredPayloadFiles).ToArray();
        new XDocument(new XElement("Package", new XAttribute("Name", adapter.RequiredPackage), new XElement("Files", files.Select(file => new XElement("File", new XAttribute("Path", file)))))).Save(Path.Combine(package, "package.xml"));
        foreach (var file in files) File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(package, file));
        fixture.ViewModel.OpenTapHomeOverride = home;
    }
    private static PlanInitializationWindow Start(AuthoringUiFixture fixture) { AuthoringUiFixture.Click(fixture.Control<Button>("Start guided voltage test")); return Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows)); }
    private static void Next(AuthoringUiFixture fixture, Window dialog) => AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
    private static void Set(AuthoringUiFixture fixture, Window dialog, string name, string value) { fixture.Control<TextBox>(name, dialog).Text = value; AuthoringUiFixture.Drain(); }
}
