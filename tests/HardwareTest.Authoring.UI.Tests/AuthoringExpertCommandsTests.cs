using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringExpertCommandsTests
{
    private static AuthoringUiFixture Loaded()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        return fixture;
    }

    [AvaloniaFact]
    public void Shown_dialog_selects_later_same_binding_with_distinct_settings_and_preserves_originals()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        var visa = AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "VISA DMM");
        vm.CreateProgram(new PlanInitializationRequest("first-settings") { Instruments = [new InstrumentRef("DMM", visa.TypeId, "TCPIP::127.0.0.1::INSTR") { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "111" } }] });
        vm.CreateProgram(new PlanInitializationRequest("later-settings") { Instruments = [new InstrumentRef("DMM", visa.TypeId, "TCPIP::127.0.0.1::INSTR") { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "777" } }] });
        Assert.True(vm.SaveAll().Succeeded);
        var donorBytes = new[] { "first-settings", "later-settings" }.SelectMany(id => new[]
        {
            new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath(id),
            Path.Combine(vm.Workspace!.Root, vm.Workspace.Manifest.PlansDirectory, id + ".TapPlan")
        }).ToDictionary(path => path, File.ReadAllBytes);
        var first = vm.Programs.Single(program => program.PlanId == "first-settings").Instruments[0];
        var later = vm.Programs.Single(program => program.PlanId == "later-settings").Instruments[0];
        fixture.Control<Button>("Save all").Focus();
        fixture.Window!.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        var id = fixture.Control<TextBox>("Stable plan ID", dialog); Assert.True(id.Focus()); id.SelectAll();
        dialog.KeyTextInput("chosen-settings"); AuthoringUiFixture.Drain(); Assert.Equal("chosen-settings", id.Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        var hardware = fixture.Control<ComboBox>("Hardware choice", dialog);
        var labels = hardware.Items.Select(item => item!.ToString()!).ToArray();
        var selected = Array.FindIndex(labels, label => label.Contains("Program later-settings", StringComparison.Ordinal));
        Assert.True(selected > 3); Assert.Contains("IoTimeoutMilliseconds=777", labels[selected]);
        Assert.Contains(labels, label => label.Contains("Program first-settings", StringComparison.Ordinal) && label.Contains("IoTimeoutMilliseconds=111", StringComparison.Ordinal));
        hardware.SelectedIndex = selected; AuthoringUiFixture.Drain();
        for (var stage = 3; stage <= 5; stage++)
        {
            var beforeStage = fixture.Control<TextBlock>("Initialization stage", dialog).Text;
            AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
            Assert.NotEqual(beforeStage, fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        }
        Assert.True(fixture.Control<Button>("Create test plan", dialog).IsVisible,
            fixture.Control<TextBlock>("Initialization error", dialog).Text + " | " + fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Equal("chosen-settings", vm.SelectedProgram!.PlanId);
        Assert.Equal("777", Assert.Single(vm.SelectedProgram.Instruments).Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(later.VisaAddress, vm.SelectedProgram.Instruments[0].VisaAddress);
        vm.SaveProgram("chosen-settings");
        Assert.Equal("111", first.Settings["IoTimeoutMilliseconds"]); Assert.Equal("777", later.Settings["IoTimeoutMilliseconds"]);
        Assert.True(vm.SaveAll().Succeeded);
        foreach (var donor in donorBytes) Assert.Equal(donor.Value, File.ReadAllBytes(donor.Key));
        vm.Open(fixture.WorkspaceRoot); vm.SelectProgram("chosen-settings");
        Assert.Equal("777", Assert.Single(vm.SelectedProgram!.Instruments).Settings["IoTimeoutMilliseconds"]);
        Assert.Equal(later.VisaAddress, vm.SelectedProgram.Instruments[0].VisaAddress);
        Assert.Equal("111", vm.Programs.Single(program => program.PlanId == "first-settings").Instruments[0].Settings["IoTimeoutMilliseconds"]);
        Assert.Equal("777", vm.Programs.Single(program => program.PlanId == "later-settings").Instruments[0].Settings["IoTimeoutMilliseconds"]);
    }

    [AvaloniaFact]
    public async Task External_process_return_marks_conflict_and_keeps_dirty_draft_and_history()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel; vm.Apply();
        var path = vm.Workspace!.TapPlanPaths.Single(candidate => Path.GetFileNameWithoutExtension(candidate) == "sample");
        vm.DisplayName = "dirty draft survives TUI";
        var before = vm.SelectedDocument!.Revision;
        await fixture.Window!.RefreshAfterExternalTuiAsync(async () =>
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture")) { UseShellExecute = false };
            start.ArgumentList.Add("--external-edit"); start.ArgumentList.Add(path);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, process.ExitCode);
        });
        Assert.Contains("sample", vm.CompiledConflictProgramIds);
        Assert.Equal("dirty draft survives TUI", vm.DisplayName); Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(before, vm.SelectedDocument.Revision); Assert.True(vm.CanUndo);
        vm.ReconcileCompiled("sample", false);
        Assert.Empty(vm.CompiledConflictProgramIds); Assert.Equal("dirty draft survives TUI", vm.DisplayName);
        Assert.True(vm.HasUncompiledSources);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Compiled_only_external_return_preserves_dirty_history_and_bytes_until_explicit_choice(bool import, bool refresh)
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("sample").Exists);
        var plan = vm.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == "sample");
        vm.DisplayName = "compiled-only unsaved sentinel";
        var revision = vm.SelectedDocument!.Revision;
        async Task ExternalEdit()
        {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture")) { UseShellExecute = false };
            start.ArgumentList.Add("--external-edit"); start.ArgumentList.Add(plan);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, process.ExitCode);
        }
        if (refresh) await fixture.Window!.RefreshAfterExternalTuiAsync(ExternalEdit);
        else await ExternalEdit();
        var external = File.ReadAllBytes(plan);
        var sidecar = File.ReadAllBytes(PlanCompiler.SidecarPath(plan));
        if (refresh) Assert.Contains("sample", vm.CompiledConflictProgramIds);
        Assert.Equal("compiled-only unsaved sentinel", vm.DisplayName);
        Assert.Equal(revision, vm.SelectedDocument.Revision); Assert.True(vm.CanUndo); Assert.False(vm.CanRedo);
        Assert.True(vm.HasUnsavedChanges);
        vm.Apply();
        Assert.Contains("sample", vm.CompiledConflictProgramIds);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        Assert.Contains("reconcile", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sample", vm.CompiledConflictProgramIds);
        Assert.Equal(external, File.ReadAllBytes(plan)); Assert.Equal(sidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(plan)));
        vm.ReconcileCompiled("sample", import);
        Assert.Empty(vm.CompiledConflictProgramIds);
        Assert.Equal(external, File.ReadAllBytes(plan)); Assert.Equal(sidecar, File.ReadAllBytes(PlanCompiler.SidecarPath(plan)));
        if (import)
        {
            Assert.Contains(vm.SelectedProgram!.Setup.OfType<OperatorPromptSetup>(), prompt => prompt.Name == "External TUI return sentinel");
            Assert.True(vm.SelectedDocument.Revision > revision); Assert.True(vm.CanUndo);
            vm.Undo(); Assert.Equal("compiled-only unsaved sentinel", vm.DisplayName);
        }
        else { Assert.Equal("compiled-only unsaved sentinel", vm.DisplayName); Assert.True(vm.HasUncompiledSources); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_external_process_failure_does_not_report_into_hidden_or_replaced_owner(bool replace)
    {
        using var fixture = Loaded();
        var pending = new TaskCompletionSource();
        var returning = fixture.Window!.RefreshAfterExternalTuiAsync(() => pending.Task);
        if (replace) fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true);
        else fixture.Window.Hide();
        var error = fixture.ViewModel.Error;
        pending.SetException(new IOException("old TUI failed")); await returning;
        Assert.Equal(error, fixture.ViewModel.Error);
        if (!replace) fixture.Window.Show();
    }

    [AvaloniaTheory]
    [InlineData(false, UnsavedChangesChoice.SaveAll)]
    [InlineData(false, UnsavedChangesChoice.Discard)]
    [InlineData(true, UnsavedChangesChoice.SaveAll)]
    [InlineData(true, UnsavedChangesChoice.Discard)]
    public async Task Late_workspace_choice_cannot_save_or_discard_hidden_or_replaced_owner(bool replace, UnsavedChangesChoice choice)
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "retained dirty edit";
        var pending = new TaskCompletionSource<UnsavedChangesChoice>(); fixture.Interaction.Pending = pending;
        var opening = fixture.Window!.ReopenWorkspaceAsync();
        if (replace) { fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true); fixture.ViewModel.DisplayName = "replacement edit"; }
        else fixture.Window.Hide();
        var selected = fixture.ViewModel.SelectedProgram;
        pending.SetResult(choice);
        Assert.False(await opening);
        Assert.Same(selected, fixture.ViewModel.SelectedProgram); Assert.True(fixture.ViewModel.HasUnsavedChanges);
        fixture.Interaction.Pending = null;
        if (!replace) fixture.Window.Show();
    }

    [AvaloniaTheory]
    [InlineData(false, UnsavedChangesChoice.SaveAll)]
    [InlineData(false, UnsavedChangesChoice.Discard)]
    [InlineData(true, UnsavedChangesChoice.SaveAll)]
    [InlineData(true, UnsavedChangesChoice.Discard)]
    public void Late_close_choice_cannot_close_or_save_replaced_hidden_owner(bool replace, UnsavedChangesChoice choice)
    {
        using var fixture = Loaded(); fixture.ViewModel.DisplayName = "dirty close";
        var pending = new TaskCompletionSource<UnsavedChangesChoice>(); fixture.Interaction.Pending = pending;
        fixture.Window!.Close();
        if (replace) { fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true); fixture.ViewModel.DisplayName = "replacement close"; }
        else fixture.Window.Hide();
        var closed = false; fixture.Window.Closed += (_, _) => closed = true;
        pending.SetResult(choice); AuthoringUiFixture.Drain();
        Assert.False(closed); Assert.True(fixture.ViewModel.HasUnsavedChanges);
        fixture.Interaction.Pending = null; if (!replace) fixture.Window.Show();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_workspace_picker_cannot_open_hidden_or_replaced_session(bool replace)
    {
        using var fixture = Loaded(); fixture.ViewModel.DisplayName = "picker edit";
        var pending = new TaskCompletionSource<string?>(); fixture.Picker.Pending = pending;
        var opening = fixture.Window!.OpenWorkspaceAsync();
        if (replace) fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true);
        else fixture.Window.Hide();
        var workspace = fixture.ViewModel.Workspace;
        pending.SetResult(fixture.WorkspaceRoot);
        Assert.False(await opening); Assert.Same(workspace, fixture.ViewModel.Workspace); Assert.Equal(0, fixture.Interaction.Calls);
        fixture.Picker.Pending = null; if (!replace) fixture.Window.Show();
    }

    [AvaloniaFact]
    public async Task Layout_persists_locally_clamps_dock_on_small_viewport_and_resets()
    {
        using var fixture = Loaded();
        Assert.True(await fixture.Window!.ExecuteCommandAsync("rail"));
        Assert.True(await fixture.Window.ExecuteCommandAsync("issues"));
        fixture.Preferences.Load();
        Assert.True(fixture.Preferences.Current.ProgramsRailCollapsed); Assert.True(fixture.Preferences.Current.IssuesDrawerOpen);
        fixture.Window.Width = 960; fixture.Window.Height = 600; AuthoringUiFixture.Drain();
        Assert.False(fixture.Window.FindControl<ContentControl>("DockedPreview")!.IsVisible);
        Assert.False(fixture.Window.FindControl<Control>("ProgramsRail")!.IsVisible);
        Assert.True(await fixture.Window.ExecuteCommandAsync("reset")); AuthoringUiFixture.Drain();
        Assert.True(fixture.Window.FindControl<Control>("ProgramsRail")!.IsVisible);
        Assert.False(fixture.Window.FindControl<Control>("IssuesDrawer")!.IsVisible);
        Assert.True(fixture.ViewModel.DockPreview);
    }

    [AvaloniaFact]
    public async Task Future_layout_preferences_remain_byte_preserved()
    {
        using var fixture = Loaded();
        var bytes = "{\n  \"schemaVersion\": 999, \"programsRailCollapsed\": true, \"future\": [1,2]\n}";
        File.WriteAllText(fixture.Preferences.FilePath, bytes); fixture.Preferences.Load();
        Assert.False(await fixture.Window!.ExecuteCommandAsync("reset"));
        Assert.Equal(bytes, File.ReadAllText(fixture.Preferences.FilePath));
    }

    [AvaloniaFact]
    public async Task Palette_search_explains_prerequisites_and_keyboard_opens_new_plan()
    {
        using var fixture = Loaded();
        fixture.Window!.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        var palette = Assert.Single(fixture.Window!.OwnedWindows);
        var search = fixture.Control<TextBox>("Search commands", palette); search.Text = "Rename"; AuthoringUiFixture.Drain();
        Assert.Single(fixture.Control<ListBox>("Authoring commands", palette).Items.Cast<object>());
        AuthoringUiFixture.Click(palette.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel")));
        fixture.Control<Button>("Save all").Focus(); fixture.Window!.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
        Assert.Empty(new AuthoringDocumentStore(fixture.WorkspaceRoot).ListDocumentIds());
        Assert.True(await fixture.Window.ExecuteCommandAsync("save-all"));
    }

    [AvaloniaFact]
    public async Task Keyboard_step_transactions_use_existing_history_and_next_issue_navigation()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel; vm.SelectMeasure(0);
        var original = vm.SelectedProgram!.Measure.Count;
        fixture.Control<Button>("Save all").Focus();
        fixture.Window!.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        var rename = Assert.Single(fixture.Window!.OwnedWindows);
        var name = fixture.Control<TextBox>("New step name", rename);
        name.Focus(); name.SelectAll(); rename.KeyTextInput("Keyboard renamed step");
        fixture.Control<Button>("Rename", rename).Focus(); rename.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Equal("Keyboard renamed step", vm.SequenceRename);
        fixture.Control<Button>("Save all").Focus();
        fixture.Window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Equal(original + 1, vm.SelectedProgram!.Measure.Count);
        fixture.Window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Equal(original, vm.SelectedProgram!.Measure.Count);
        Assert.True(vm.CanRedo);
        fixture.Window.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Equal(original + 1, vm.SelectedProgram!.Measure.Count);
        vm.CreateDemoProgram("issue-navigation"); vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ChannelKey = "";
        var issue = vm.EditingIssues.First();
        vm.SelectProgram(issue.PlanId == "sample" ? "issue-navigation" : "sample");
        Assert.True(await fixture.Window.ExecuteCommandAsync("issue")); AuthoringUiFixture.Drain();
        Assert.Equal(issue.PlanId, vm.SelectedProgram!.PlanId);
    }

    [AvaloniaFact]
    public void Text_undo_redo_remains_in_focused_control()
    {
        using var fixture = Loaded();
        fixture.ViewModel.SelectMeasure(0); fixture.ViewModel.ChannelKey = "unrelated-history-sentinel";
        var node = fixture.ViewModel.SelectedSequence!.NodeId;
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        var box = fixture.Control<TextBox>("Display name");
        box.Focus(); box.SelectAll(); fixture.Window!.KeyTextInput("text undo");
        fixture.Window!.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.NotEqual("text undo", box.Text); Assert.False(fixture.ViewModel.CanRedo);
        Assert.Equal("unrelated-history-sentinel", fixture.ViewModel.ChannelKey); Assert.Equal(node, fixture.ViewModel.SelectedSequence!.NodeId);
        fixture.Window!.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Equal("text undo", box.Text); Assert.False(fixture.ViewModel.CanRedo);
        Assert.Equal("unrelated-history-sentinel", fixture.ViewModel.ChannelKey); Assert.Equal(node, fixture.ViewModel.SelectedSequence!.NodeId);
    }
}
