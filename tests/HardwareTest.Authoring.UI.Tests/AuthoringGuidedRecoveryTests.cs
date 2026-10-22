using System.Reflection;
using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringGuidedRecoveryTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Resume_preserves_reused_resource_content_after_catalog_reorder_removal_or_edit(int mutation)
    {
        using var fixture = Loaded(library: true); var vm = fixture.ViewModel;
        CurrentHardwareUiFixture.Prepare(fixture);
        var adapter = AuthoringInstrumentCatalog.All.Single(item => item.TypeId == CurrentHardwareUiFixture.TypeId);
        await AddDefinitionAsync(vm, adapter, "A", "TCPIP::first::INSTR", "1111");
        await AddDefinitionAsync(vm, adapter, "B", "TCPIP::original::INSTR", "2222");
        await AddDefinitionAsync(vm, adapter, "C", "TCPIP::different::INSTR", "3333");
        var dialog = Start(fixture); Set(fixture, dialog, "Stable plan ID", "retained-binding");
        Next(fixture, dialog); fixture.Control<ComboBox>("Hardware choice", dialog).SelectedItem = fixture.Control<ComboBox>("Hardware choice", dialog).Items.Single(item => item!.ToString()!.StartsWith("Reuse B —", StringComparison.Ordinal));
        Set(fixture, dialog, "Instrument slot", "My bench");
        Assert.Equal("TCPIP::original::INSTR", fixture.Control<TextBox>("Instrument address", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", dialog));
        vm.SelectedHardwareDefinition = vm.HardwareDefinitions.Single(definition => definition.Name == (mutation == 0 ? "A" : "B"));
        if (mutation == 2)
        {
            vm.LoadHardwareDefinitionEditor(); vm.HardwareEditTimeout = "9999"; vm.UpdateHardwareDefinition();
        }
        else vm.ApplyHardwareDefinitionRemoval(vm.PrepareHardwareDefinitionRemoval());
        await vm.WaitForEnvironmentInspectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
        AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Assert.Equal("My bench", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        Assert.Equal("TCPIP::original::INSTR", fixture.Control<TextBox>("Instrument address", dialog).Text);
        if (mutation != 0) Assert.Contains("retained", fixture.Control<TextBlock>("Hardware readiness", dialog).Text, StringComparison.OrdinalIgnoreCase);
        Next(fixture, dialog); fixture.Control<CheckBox>("Include first measurement", dialog).IsChecked = false;
        for (var stage = 0; stage < 3; stage++) Next(fixture, dialog);
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
        AuthoringUiFixture.Click(GuidanceUiActions.Action(fixture, "Save and check draft"));
        Assert.Contains("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
        var path = compiled ? vm.Workspace!.TapPlanPaths.Single(file => Path.GetFileNameWithoutExtension(file) == "locked-guide")
            : new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("locked-guide");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(Record.Exception(() => AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"))));
            Assert.Contains("unavailable", GuidanceUiActions.Feedback(fixture).Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Restore", fixture.Control<TextBlock>("Guidance next step").Text);
            Assert.DoesNotContain("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
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
        Assert.DoesNotContain("unavailable or unsafe", GuidanceUiActions.Feedback(fixture).Text);
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
            Assert.Contains("unsafe", GuidanceUiActions.Feedback(fixture).Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Restore", fixture.Control<TextBlock>("Guidance next step").Text);
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
            Assert.Equal("First test plan", guided.Title); Assert.False(fixture.Control<Button>("Skip optional guidance", guided).IsEnabled);
            AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", guided));
        }
        else
        {
            Assert.True(window.FindControl<Border>("GuidanceHost")!.IsVisible);
            var guided = Start(fixture); Assert.Equal("First test plan", guided.Title);
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
        AuthoringUiFixture.Click(GuidanceUiActions.Action(fixture, "Save and check draft"));
        Assert.Contains("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
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
                other.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
                external.SelectSequence(external.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
                AuthoringUiFixture.Drain(); fixture.Control<TextBox>("Threshold", other).Text = "2.75";
            }
            else if (changed == "binding") external.VisaAddress = "MOCK::SECOND";
            else
            {
                other.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 7; AuthoringUiFixture.Drain();
                fixture.Control<TextBox>("Display name", other).Text = "Saved by the second instance";
            }
            AuthoringUiFixture.Drain(); Assert.True(external.HasUnsavedChanges);
            AuthoringUiFixture.Click(fixture.Control<Button>("Save plan", other));
            Assert.False(external.HasUnsavedChanges); Assert.Null(external.Error);
            var saved = new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("external-guide").Document!;
            Assert.False(saved.RequiresCompilation);
            if (changed == "threshold") Assert.Equal(2.75, Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(saved.ToDraft().Measure)).Limits!.Threshold);
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
            Assert.DoesNotContain("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
            Assert.Contains("Reopen", fixture.Control<TextBlock>("Guidance next step").Text);
            Assert.Contains("differs", GuidanceUiActions.Feedback(fixture).Text);
            Assert.Equal("Review issues", fixture.Control<Button>("Guidance next action").Content);
            Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            vm.DisplayName = "Local unsaved input"; AuthoringUiFixture.Drain();
            Assert.True(vm.HasUnsavedChanges); Assert.True(vm.CanUndo); Assert.Same(originalSession, vm.SelectedDocument);
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            Assert.DoesNotContain("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
            Assert.Equal("Local unsaved input", vm.DisplayName); vm.Undo(); AuthoringUiFixture.Drain();
            Assert.False(vm.HasUnsavedChanges); Assert.True(original.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            Assert.True(await fixture.Window!.ReopenWorkspaceAsync()); AuthoringUiFixture.Drain();
            Assert.True(replacement.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            Assert.Contains("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
        }
        finally { other.Close(); external.StopRecovery(); external.StopOperations(); AuthoringUiFixture.Drain(); }
    }

    [AvaloniaFact]
    public void Local_unsaved_sidecar_edit_offers_save_without_external_source_recovery()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        PreparePackage(fixture, AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM"));
        vm.InitializePlan(new PlanInitializationRequest("local-guide")
        { StartingPoint = PlanStartingPoint.DemoVoltageTask, IdentityInstrumentSlot = "DMM" });
        vm.Apply();
        vm.DisplayName = "My updated voltage plan";
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.Equal("Unsaved draft changes", GuidanceUiActions.Feedback(fixture).Text);
        Assert.DoesNotContain("Reopen", fixture.Control<TextBlock>("Guidance next step").Text);
        Assert.Equal("Save and check draft", fixture.Control<Button>("Guidance next action").Content);
        AuthoringUiFixture.Click(fixture.Control<Button>("Guidance next action"));
        Assert.Null(vm.Error);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal("My updated voltage plan", new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("local-guide").Document!.Sidecar.DisplayName);
        Assert.Equal("First voltage test complete", GuidanceUiActions.Feedback(fixture).Text);
    }

    [AvaloniaFact]
    public void Missing_source_guidance_separates_plan_status_next_action_and_recovery_details()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        vm.InitializePlan(new PlanInitializationRequest("missing-source-guide"));
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        File.Delete(new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("missing-source-guide"));
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.Equal(vm.SelectedProgram!.Sidecar.DisplayName, fixture.Control<TextBlock>("Guidance plan name").Text);
        var status = GuidanceUiActions.Feedback(fixture);
        Assert.Equal("Saved input is unavailable or unsafe", status.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.Contains("Restore readable authoring source", fixture.Control<TextBlock>("Guidance next step").Text);
        Assert.Equal("Review recovery issues", fixture.Control<Button>("Guidance next action").Content);
        var details = fixture.Control<Expander>("Guidance recovery details");
        Assert.True(details.IsExpanded);
        Assert.NotEmpty(fixture.Control<StackPanel>("Guidance blocker items").Children);
        foreach (var group in new[] { "Draft checks", "Preview and deployment", "Guidance preferences" })
            Assert.False(fixture.Control<Expander>(group).IsExpanded);
        var headings = fixture.Control<ScrollViewer>("Guidance viewport").GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Text is "First test plan" or "Status" or "Next action").Select(text => text.Text).ToArray();
        Assert.Equal(new[] { "First test plan", "Status", "Next action" }, headings);
        AuthoringUiFixture.Click(fixture.Control<Button>("Guidance next action"));
        Assert.Equal(2, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("save")]
    [InlineData("switch")]
    public async Task Guidance_worker_keeps_dispatcher_responsive_and_discards_obsolete_assessments(string mutation)
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        PreparePackage(fixture, AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM"));
        vm.InitializePlan(new PlanInitializationRequest("async-guide")
        { StartingPoint = PlanStartingPoint.DemoVoltageTask, IdentityInstrumentSlot = "DMM" });
        vm.Apply();
        if (mutation == "save") vm.DisplayName = "Save while guidance checks";
        var computed = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var release = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var calls = 0;
        var allOffDispatcher = true;
        Func<Action, Task> scheduler = action => Task.Run(async () =>
        {
            var index = Interlocked.Increment(ref calls) - 1;
            allOffDispatcher &= !Dispatcher.UIThread.CheckAccess();
            action();
            if (index < computed.Length)
            {
                computed[index].TrySetResult();
                await release[index].Task;
            }
        });
        typeof(MainWindow).GetField("_runGuidanceAssessment", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Window, scheduler);
        try
        {
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            await computed[0].Task.WaitAsync(TimeSpan.FromSeconds(30));
            var heartbeat = false;
            Dispatcher.UIThread.Post(() => heartbeat = true);
            AuthoringUiFixture.Drain();
            Assert.True(heartbeat);
            Assert.False(fixture.Control<Button>("Guidance next action").IsEnabled);
            Assert.Equal("Checking saved plan…", fixture.Control<TextBlock>("Guidance feedback").Text);
            if (mutation == "edit")
            {
                for (var edit = 0; edit < 12; edit++) vm.DisplayName = $"Latest draft {edit}";
                vm.Undo(); vm.Redo();
            }
            else if (mutation == "save") vm.Apply();
            else
            {
                vm.SelectProgram("sample");
                AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            }
            AuthoringUiFixture.Drain();
            Assert.Equal(1, Volatile.Read(ref calls));
            release[0].TrySetResult();
            await computed[1].Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(fixture.Control<Button>("Guidance next action").IsEnabled);
            Assert.Equal("Checking saved plan…", fixture.Control<TextBlock>("Guidance feedback").Text);
            release[1].TrySetResult();
            var feedback = GuidanceUiActions.Feedback(fixture).Text;
            Assert.True(allOffDispatcher);
            Assert.Equal(vm.SelectedProgram!.Sidecar.DisplayName, fixture.Control<TextBlock>("Guidance plan name").Text);
            if (mutation == "edit") Assert.Equal("Unsaved draft changes", feedback);
            if (mutation == "save") Assert.Equal("First voltage test complete", feedback);
            if (mutation == "switch") Assert.Equal("sample", vm.SelectedProgram.PlanId);
        }
        finally { foreach (var gate in release) gate.TrySetResult(); }
    }

    [AvaloniaFact]
    public async Task Unrelated_presentation_notifications_do_not_reassess_guidance_but_resume_does()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        await vm.WaitForEnvironmentInspectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var calls = 0;
        Func<Action, Task> scheduler = action => { calls++; return Task.Run(action); };
        typeof(MainWindow).GetField("_runGuidanceAssessment", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Window, scheduler);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        var status = GuidanceUiActions.Feedback(fixture).Text;
        var completedCalls = calls;
        var notify = typeof(AuthoringWorkspaceViewModel).GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var update = 0; update < 12; update++)
        {
            vm.ReportError($"Unrelated status update {update}");
            notify.Invoke(vm, [nameof(AuthoringWorkspaceViewModel.PreviewBusy)]);
            notify.Invoke(vm, [nameof(AuthoringWorkspaceViewModel.EnvironmentPackages)]);
        }
        AuthoringUiFixture.Drain();
        Assert.Equal(completedCalls, calls);
        Assert.Equal(status, fixture.Control<TextBlock>("Guidance feedback").Text);
        Assert.True(fixture.Control<Button>("Guidance next action").IsEnabled);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        GuidanceUiActions.WaitForAssessment(fixture);
        Assert.Equal(completedCalls + 1, calls);
    }

    [AvaloniaFact]
    public async Task Closing_guidance_does_not_allow_pending_assessment_to_reopen_it()
    {
        using var fixture = Loaded();
        var computed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? running = null;
        Func<Action, Task> scheduler = action => running = Task.Run(async () =>
        {
            action(); computed.TrySetResult(); await release.Task;
        });
        typeof(MainWindow).GetField("_runGuidanceAssessment", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fixture.Window, scheduler);
        try
        {
            AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
            await computed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            AuthoringUiFixture.Click(fixture.Control<Button>("Close guidance"));
            release.TrySetResult();
            await running!;
            AuthoringUiFixture.Drain();
            Assert.False(fixture.Window!.FindControl<Border>("GuidanceHost")!.IsVisible);
            Assert.Equal("Checking saved plan…", fixture.Control<TextBlock>("Guidance feedback").Text);
        }
        finally { release.TrySetResult(); }
    }

    private static AuthoringUiFixture Loaded(bool library = false)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        if (library)
        {
            var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
            workspace.Manifest.Dependencies.Add(new AuthoringPackageDependency { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "0.1.1" });
            AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, workspace.Manifest);
        }
        fixture.Show(); fixture.OpenRememberedWorkspace(); return fixture;
    }
    private static async Task AddDefinitionAsync(AuthoringWorkspaceViewModel vm, AuthoringInstrumentAdapter adapter, string name, string address, string timeout)
    {
        await vm.WaitForEnvironmentInspectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
        vm.HardwareEditType = adapter; vm.NewInstrumentSlot = name; vm.HardwareEditAddress = address; vm.HardwareEditTimeout = timeout;
        vm.AddHardwareDefinition(); AuthoringUiFixture.Drain();
        await vm.WaitForEnvironmentInspectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
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
