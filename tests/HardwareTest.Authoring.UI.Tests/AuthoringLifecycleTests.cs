using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringLifecycleTests
{
    [AvaloniaFact]
    public async Task Open_commits_pending_lost_focus_and_cancel_retains_session()
    {
        using var fixture = Loaded();
        var original = fixture.ViewModel.SelectedProgram;
        PendingChannel(fixture, "pending-open-edit");
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(await fixture.Window!.OpenWorkspaceAsync(fixture.WorkspaceRoot));
        Assert.Equal(1, fixture.Interaction.Calls);
        Assert.Equal(new DirtyProgramSummary("sample", true, false), Assert.Single(fixture.Interaction.LastSummary));
        Assert.Same(original!.Sidecar, fixture.ViewModel.SelectedProgram!.Sidecar);
        Assert.Equal("pending-open-edit", fixture.ViewModel.ChannelKey);
        Assert.True(fixture.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Cancel_picker_leaves_pending_binding_and_session_untouched()
    {
        using var fixture = Loaded();
        fixture.ViewModel.SelectMeasure(0);
        var originalName = fixture.ViewModel.ChannelKey;
        var box = PendingChannel(fixture, "pending-picker-edit");
        Assert.False(await fixture.Window!.OpenWorkspaceAsync());
        Assert.Equal(0, fixture.Interaction.Calls);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal(originalName, fixture.ViewModel.ChannelKey);
        Assert.Equal("pending-picker-edit", box.Text);
    }

    [AvaloniaFact]
    public async Task Discard_invalid_load_retains_drafts_then_reopen_can_replace()
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "draft retained";
        var draft = fixture.ViewModel.SelectedProgram;
        fixture.Interaction.Choice = UnsavedChangesChoice.Discard;
        Assert.False(await fixture.Window!.OpenWorkspaceAsync(Path.Combine(fixture.WorkspaceRoot, "invalid")));
        Assert.Same(draft, fixture.ViewModel.SelectedProgram);
        Assert.Equal("draft retained", fixture.ViewModel.DisplayName);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(await fixture.Window.ReopenWorkspaceAsync());
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.NotEqual("draft retained", fixture.ViewModel.DisplayName);
        Assert.Null(fixture.ViewModel.Error);
    }

    [AvaloniaFact]
    public async Task Discard_failed_recordings_load_keeps_original_session_and_draft()
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "retained after presentation failure";
        var selected = fixture.ViewModel.SelectedProgram;
        var manifest = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot).Manifest;
        manifest.RecordingsDirectory = "../outside-workspace";
        AuthoringWorkspaceLoader.SaveManifest(fixture.WorkspaceRoot, manifest);
        fixture.Interaction.Choice = UnsavedChangesChoice.Discard;
        Assert.False(await fixture.Window!.ReopenWorkspaceAsync());
        Assert.Same(selected, fixture.ViewModel.SelectedProgram);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.Equal("retained after presentation failure", fixture.ViewModel.DisplayName);
        Assert.Equal("recordings", fixture.ViewModel.Workspace!.Manifest.RecordingsDirectory);
    }

    [AvaloniaTheory]
    [InlineData(UnsavedChangesChoice.SaveAll)]
    [InlineData(UnsavedChangesChoice.Discard)]
    public async Task Completed_close_choice_posts_one_close_after_initial_event(UnsavedChangesChoice choice)
    {
        using var fixture = Loaded();
        PendingChannel(fixture, "pending-close-edit");
        fixture.Interaction.Choice = choice;
        var closed = 0;
        fixture.Window!.Closed += (_, _) => closed++;
        fixture.Window.Close();
        Assert.True(fixture.Window.IsVisible);
        Assert.Equal("pending-close-edit", fixture.ViewModel.ChannelKey);
        Assert.Equal(1, fixture.Interaction.Calls);
        await WaitForCloseAsync(fixture.Window);
        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(1, closed);
        if (choice == UnsavedChangesChoice.SaveAll)
        {
            var loaded = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences);
            loaded.Open(fixture.WorkspaceRoot);
            loaded.SelectMeasure(0);
            Assert.Equal("pending-close-edit", loaded.ChannelKey);
            Assert.False(fixture.ViewModel.HasUnsavedChanges);
            await loaded.StopRecoveryAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData("new", false)]
    [InlineData("source-only", false)]
    [InlineData("compiled", false)]
    [InlineData("new", true)]
    [InlineData("source-only", true)]
    [InlineData("compiled", true)]
    public async Task Save_all_first_compilation_allows_requested_workspace_transition(string origin, bool close)
    {
        using var fixture = Loaded();
        using var replacement = new AuthoringUiFixture();
        var vm = fixture.ViewModel;
        if (origin != "compiled")
        {
            vm.InitializePlan(new("first-compiled") { Instruments = [] });
            if (origin == "source-only")
            {
                var document = AuthoringDocumentDto.FromDraft(vm.SelectedProgram!);
                document.RequiresCompilation = true;
                new AuthoringDocumentStore(fixture.WorkspaceRoot).Save(document);
                await vm.StopRecoveryAsync();
                vm.CommitOpen(vm.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true);
                vm.SelectProgram("first-compiled");
            }
        }
        var planId = vm.SelectedProgram!.PlanId;
        var oldWorkspace = vm.Workspace;
        var oldSession = vm.WorkspaceSessionId;
        vm.DisplayName = "saved before transition";
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(origin == "compiled", File.Exists(Path.Combine(fixture.WorkspaceRoot, planId + ".TapPlan")));
        fixture.Interaction.Choice = UnsavedChangesChoice.SaveAll;
        var closed = 0;
        fixture.Window!.Closed += (_, _) => closed++;
        if (close)
        {
            fixture.Window.Close();
            await WaitForCloseAsync(fixture.Window);
            Assert.Equal(1, closed);
            Assert.Equal(oldSession, vm.WorkspaceSessionId);
            if (origin != "compiled") Assert.NotSame(oldWorkspace, vm.Workspace);
        }
        else
        {
            Assert.True(await fixture.Window.OpenWorkspaceAsync(replacement.WorkspaceRoot));
            Assert.Equal(replacement.WorkspaceRoot, vm.Workspace!.Root);
            Assert.NotEqual(oldSession, vm.WorkspaceSessionId);
            Assert.True(fixture.Window.IsVisible);
            Assert.Equal(0, closed);
        }
        if (close) Assert.True(vm.LastSaveAllResult!.Succeeded);
        Assert.False(vm.HasUnsavedChanges);
        var saved = new AuthoringDocumentStore(fixture.WorkspaceRoot).Load(planId).Document!;
        Assert.False(saved.RequiresCompilation);
        Assert.Equal("saved before transition", saved.ToDraft().Sidecar.DisplayName);
        Assert.Equal("saved before transition", new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, planId + ".TapPlan")).Sidecar.DisplayName);
    }

    [AvaloniaTheory]
    [InlineData("timeout")]
    [InlineData("hide")]
    [InlineData("context")]
    public async Task Aborted_close_resumes_recovery_only_after_its_original_writer_drains(string abort)
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        await vm.StopRecoveryAsync();
        var originalSource = File.ReadAllBytes(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var oldNotifications = 0;
        using var recovery = new AuthoringRecoveryCheckpointService(action => action(), _ => oldNotifications++, TimeSpan.Zero,
            (path, document) =>
            {
                using (File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Fixture writer was not released.");
                }
                new AuthoringDocumentStore(fixture.WorkspaceRoot).SaveAtPath(path, document);
            });
        var field = typeof(AuthoringWorkspaceViewModel).GetField("_recovery", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(vm, recovery);
        var transition = typeof(MainWindow).GetField("_transitionInFlight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var session = vm.WorkspaceSessionId;
        var store = new AuthoringDocumentStore(fixture.WorkspaceRoot);
        try
        {
            vm.DisplayName = "old held checkpoint";
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Interaction.Choice = UnsavedChangesChoice.Discard;
            fixture.Window!.Close();
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (field.GetValue(vm) is not null) { await Task.Delay(1, stopTimeout.Token); AuthoringUiFixture.Drain(); }
            Assert.Null(field.GetValue(vm));
            if (abort == "hide") { fixture.Window.Hide(); release.Set(); }
            if (abort == "context") { fixture.Window.DataContext = new object(); release.Set(); }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            while ((bool)transition.GetValue(fixture.Window)!) { await Task.Delay(1, timeout.Token); AuthoringUiFixture.Drain(); }
            if (abort == "hide") fixture.Window.Show();
            if (abort == "context") { Assert.Null(field.GetValue(vm)); fixture.Window.DataContext = vm; }
            if (abort == "timeout") Assert.Contains("timed out", vm.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.True(fixture.Window.IsVisible);
            Assert.Equal(session, vm.WorkspaceSessionId);
            Assert.False(File.Exists(store.GetRecoveryPath("sample")));
            vm.DisplayName = "edited after aborted close";
            if (abort == "timeout")
            {
                Assert.Null(field.GetValue(vm));
                Assert.False(File.Exists(store.GetRecoveryPath("sample")));
                release.Set();
            }
            using var checkpointTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(store.GetRecoveryPath("sample"))) { await Task.Delay(1, checkpointTimeout.Token); AuthoringUiFixture.Drain(); }
            Assert.Equal("edited after aborted close", store.LoadAtPath(store.GetRecoveryPath("sample")).Document!.ToDraft().Sidecar.DisplayName);
            Assert.Equal(0, oldNotifications);
            Assert.True(vm.HasUnsavedChanges);
            Assert.Equal(originalSource, File.ReadAllBytes(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")));
        }
        finally { release.Set(); fixture.Window!.DataContext = vm; if (!fixture.Window.IsVisible) fixture.Window.Show(); await vm.StopRecoveryAsync(); }
    }

    private static async Task WaitForCloseAsync(MainWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        AuthoringUiFixture.Drain();
        while (window.IsVisible) { await Task.Delay(1, timeout.Token); AuthoringUiFixture.Drain(); }
    }

    [AvaloniaFact]
    public async Task Async_close_and_concurrent_navigation_share_one_prompt_and_close_once()
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "async draft";
        var pending = new TaskCompletionSource<UnsavedChangesChoice>();
        fixture.Interaction.Pending = pending;
        var closed = 0;
        fixture.Window!.Closed += (_, _) => closed++;
        fixture.Window.Close();
        fixture.Window.Close();
        Assert.False(await fixture.Window.ReopenWorkspaceAsync());
        Assert.Equal(1, fixture.Interaction.Calls);
        Assert.True(fixture.Window.IsVisible);
        pending.SetResult(UnsavedChangesChoice.Discard);
        await WaitForCloseAsync(fixture.Window);
        Assert.Equal(1, closed);
        Assert.False(fixture.Window.IsVisible);
        fixture.Interaction.Pending = null;
    }

    [AvaloniaFact]
    public async Task Concurrent_open_and_close_do_not_duplicate_prompt_or_close_on_cancel()
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "concurrent draft";
        var pending = new TaskCompletionSource<UnsavedChangesChoice>();
        fixture.Interaction.Pending = pending;
        var first = fixture.Window!.ReopenWorkspaceAsync();
        fixture.Window.Close();
        Assert.False(await fixture.Window.ReopenWorkspaceAsync());
        Assert.Equal(1, fixture.Interaction.Calls);
        pending.SetResult(UnsavedChangesChoice.Cancel);
        Assert.False(await first);
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Window.IsVisible);
        Assert.Equal("concurrent draft", fixture.ViewModel.DisplayName);
        fixture.Interaction.Pending = null;
    }

    [AvaloniaFact]
    public async Task Failed_save_all_keeps_window_open_and_blocks_navigation()
    {
        using var fixture = Loaded(new FailingCompiler());
        fixture.ViewModel.DisplayName = "failed save draft";
        fixture.Interaction.Choice = UnsavedChangesChoice.SaveAll;
        fixture.Window!.Close();
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Window.IsVisible);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(fixture.ViewModel.LastSaveAllResult!.Succeeded);
        Assert.False(await fixture.Window.ReopenWorkspaceAsync());
        Assert.Equal("failed save draft", fixture.ViewModel.DisplayName);
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("reopen")]
    [InlineData("picker")]
    public async Task Fixture_disposal_resolves_in_flight_requests_and_closes_window(string transition)
    {
        var fixture = Loaded();
        try
        {
            fixture.ViewModel.DisplayName = "draft awaiting fixture disposal";
            Task<bool>? request = null;
            if (transition == "picker")
            {
                fixture.Picker.Pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                request = fixture.Window!.OpenWorkspaceAsync();
            }
            else
            {
                fixture.Interaction.Pending = new TaskCompletionSource<UnsavedChangesChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (transition == "close") fixture.Window!.Close();
                else request = fixture.Window!.ReopenWorkspaceAsync();
            }
            Assert.True(fixture.Window!.IsVisible);
            fixture.Dispose();
            Assert.False(fixture.Window.IsVisible);
            Assert.Empty(fixture.Window.OwnedWindows);
            Assert.False(Directory.Exists(fixture.WorkspaceRoot));
            if (request is not null) Assert.False(await request);
        }
        finally
        {
            if (Directory.Exists(fixture.WorkspaceRoot)) fixture.Dispose();
        }
    }

    [AvaloniaFact]
    public void Save_all_feedback_reports_saved_program_and_optional_preview_problem_separately()
    {
        using var fixture = Loaded();
        fixture.ViewModel.DisplayName = "saved from UI despite preview failure";
        fixture.ViewModel.OpenTapHomeOverride = "invalid\0home";
        var selected = fixture.ViewModel.SelectedProgram;
        var programRow = fixture.Control<ComboBox>("Selected test plan").SelectedItem;
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.True(fixture.ViewModel.LastSaveAllResult!.Succeeded);
        Assert.Empty(fixture.ViewModel.LastSaveAllResult.Failures);
        Assert.Equal(["sample"], fixture.ViewModel.LastSaveAllResult.SavedProgramIds);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.Same(selected, fixture.ViewModel.SelectedProgram);
        Assert.Same(programRow, fixture.Control<ComboBox>("Selected test plan").SelectedItem);
        Assert.Equal("saved from UI despite preview failure", new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")).Sidecar.DisplayName);
        Assert.Contains("OpenTAP home setting", fixture.Control<TextBlock>("Authoring error").Text);
        Assert.Contains(fixture.Control<ItemsControl>("Save all results").GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved sample");
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    [InlineData(Key.None)]
    public void Real_modal_default_enter_escape_and_window_close_cancel_safely(Key key)
    {
        using var fixture = Loaded(realInteraction: true);
        fixture.ViewModel.DisplayName = "real modal draft";
        fixture.Window!.Close();
        AuthoringUiFixture.Drain();
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        Assert.True(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel")).IsDefault);
        if (key == Key.None) dialog.Close();
        else { dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null); }
        AuthoringUiFixture.Drain();
        Assert.Empty(fixture.Window.OwnedWindows);
        Assert.True(fixture.Window.IsVisible);
        Assert.Equal("real modal draft", fixture.ViewModel.DisplayName);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData("Save all")]
    [InlineData("Discard")]
    public async Task Real_modal_acceptance_closes_owner_once(string action)
    {
        using var fixture = Loaded(realInteraction: true);
        fixture.ViewModel.DisplayName = "modal acceptance edit";
        var closed = 0;
        fixture.Window!.Closed += (_, _) => closed++;
        fixture.Window.Close();
        AuthoringUiFixture.Drain();
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        AuthoringUiFixture.Click(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, action)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (fixture.Window.IsVisible) { await Task.Delay(1, timeout.Token); AuthoringUiFixture.Drain(); }
        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(1, closed);
        if (action == "Save all") Assert.True(fixture.ViewModel.LastSaveAllResult!.Succeeded);
    }

    [AvaloniaFact]
    public void Folder_open_button_uses_picker_and_commits_pending_edit_before_cancel_prompt()
    {
        using var fixture = Loaded();
        PendingChannel(fixture, "folder-button-edit");
        fixture.Picker.Path = fixture.WorkspaceRoot;
        AuthoringUiFixture.Click(fixture.Control<Button>("Open workspace"));
        Assert.Equal(1, fixture.Interaction.Calls);
        Assert.Equal("folder-button-edit", fixture.ViewModel.ChannelKey);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(fixture.Window!.IsVisible);
    }

    [AvaloniaFact]
    public void Save_all_commits_pending_plan_edit_and_keeps_actual_sequence_selection()
    {
        using var fixture = Loaded();
        PendingChannel(fixture, "save-all-channel");
        var vm = fixture.ViewModel;
        var sequenceIndex = vm.SelectedSequenceIndex;
        var programRow = vm.SelectedProgramRow;
        Assert.False(vm.HasUnsavedChanges);
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.LastSaveAllResult!.Succeeded);
        Assert.Equal("save-all-channel", vm.ChannelKey);
        Assert.Equal(sequenceIndex, vm.SelectedSequenceIndex);
        Assert.Equal("Acquire VDC", vm.SelectedSequence!.Label);
        Assert.Same(programRow, fixture.Control<ComboBox>("Selected test plan").SelectedItem);
        Assert.Same(vm.SelectedSequence, fixture.Control<ListBox>("Program sequence").SelectedItem);
        var saved = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences);
        saved.Open(fixture.WorkspaceRoot);
        saved.SelectMeasure(0);
        Assert.Equal("save-all-channel", saved.ChannelKey);
    }

    [AvaloniaFact]
    public void Save_all_button_and_dirty_markers_preserve_list_and_sequence_selection()
    {
        using var fixture = Loaded();
        var vm = fixture.ViewModel;
        var programs = fixture.Control<ComboBox>("Selected test plan");
        var row = programs.SelectedItem;
        var rows = programs.ItemsSource;
        var sequence = vm.SelectedSequence;
        PendingText(fixture, "save all typed edit");
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal("save all typed edit", vm.DisplayName);
        Assert.Same(row, programs.SelectedItem);
        Assert.Same(rows, programs.ItemsSource);
        Assert.Same(sequence, vm.SelectedSequence);
        Assert.Equal(string.Empty, ((AuthoringProgramRow)row!).DirtyMarker);
        Assert.Equal(["Saved sample"], vm.SaveAllResults);
        vm.DisplayName = "another edit";
        AuthoringUiFixture.Drain();
        Assert.Equal("Unsaved settings", ((AuthoringProgramRow)row).DirtyMarker);
        Assert.Contains(programs.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "another edit");
    }

    private static AuthoringUiFixture Loaded(IPlanCompiler? compiler = null, bool realInteraction = false)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true, compiler);
        fixture.Show(realInteraction: realInteraction);
        fixture.OpenRememberedWorkspace();
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 7;
        AuthoringUiFixture.Drain();
        return fixture;
    }

    private static TextBox PendingChannel(AuthoringUiFixture fixture, string text)
    {
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
        fixture.ViewModel.SelectMeasure(0);
        AuthoringUiFixture.Drain();
        var channel = fixture.Control<AutoCompleteBox>("Channel key");
        channel.BringIntoView();
        AuthoringUiFixture.Drain();
        var box = Assert.Single(channel.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        box.SelectAll();
        fixture.Window!.KeyTextInput(text);
        AuthoringUiFixture.Drain();
        Assert.Equal(text, box.Text);
        return box;
    }

    private static void PendingText(AuthoringUiFixture fixture, string text)
    {
        var box = fixture.Control<TextBox>("Display name");
        box.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(box.Focus());
        box.SelectAll();
        fixture.Window!.KeyTextInput(text);
        AuthoringUiFixture.Drain();
        Assert.Equal(text, box.Text);
    }

    private sealed class FailingCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public void Save(ProgramDraft draft, string path) => throw new IOException("save failed");
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new IOException("save failed");
        public ProgramDraft Load(string path) => _inner.Load(path);
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => _inner.LoadAll(workspace);
    }
}
