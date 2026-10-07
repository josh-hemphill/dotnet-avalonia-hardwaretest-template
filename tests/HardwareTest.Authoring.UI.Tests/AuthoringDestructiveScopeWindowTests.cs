using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringDestructiveScopeWindowTests
{
    [AvaloniaTheory]
    [InlineData("fixtureId", "Required DUT fields")]
    [InlineData("custom", "Report kinds")]
    public void Actual_membership_checkbox_changes_only_selected_program(string target, string group)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel;
        vm.CreateProgram("a"); AddCatalog(fixture, target == "fixtureId" ? CatalogDeletionKind.RequiredField : CatalogDeletionKind.ReportKind);
        vm.CreateProgram("b"); vm.SelectProgram("a"); Assert.True(vm.SaveAll().Succeeded); AuthoringUiFixture.Drain();
        var other = vm.Programs.Single(p => p.PlanId == "b"); var otherBytes = File.ReadAllBytes(Sidecar(fixture, "b"));
        var box = fixture.Control<CheckBox>($"Include {target} in selected program", fixture.Control<ItemsControl>(group));
        Assert.True(box.IsChecked); PressSpace(fixture.Window!, box);
        Assert.False(fixture.Control<CheckBox>($"Include {target} in selected program", fixture.Control<ItemsControl>(group)).IsChecked);
        Assert.Same(other, vm.Programs.Single(p => p.PlanId == "b")); Assert.Equal(otherBytes, File.ReadAllBytes(Sidecar(fixture, "b")));
        Assert.Equal(new DirtyProgramSummary("a", false, true), Assert.Single(vm.DirtyPrograms)); Assert.False(vm.WorkspaceCatalogDirty);
    }

    [AvaloniaTheory]
    [InlineData(CatalogDeletionKind.RequiredField, "cancel")]
    [InlineData(CatalogDeletionKind.RequiredField, "enter")]
    [InlineData(CatalogDeletionKind.RequiredField, "escape")]
    [InlineData(CatalogDeletionKind.RequiredField, "dismiss")]
    [InlineData(CatalogDeletionKind.ReportKind, "cancel")]
    [InlineData(CatalogDeletionKind.ReportKind, "enter")]
    [InlineData(CatalogDeletionKind.ReportKind, "escape")]
    [InlineData(CatalogDeletionKind.ReportKind, "dismiss")]
    [InlineData(CatalogDeletionKind.ProgramKind, "cancel")]
    [InlineData(CatalogDeletionKind.ProgramKind, "enter")]
    [InlineData(CatalogDeletionKind.ProgramKind, "escape")]
    [InlineData(CatalogDeletionKind.ProgramKind, "dismiss")]
    public void Real_workspace_delete_modal_names_scope_and_impact_and_all_cancellation_routes_preserve_drafts_and_files(CatalogDeletionKind kind, string route)
    {
        using var fixture = Loaded(960, 600); var target = AddCatalog(fixture, kind); Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        var draft = fixture.ViewModel.SelectedProgram; var files = Snapshot(fixture);
        OpenCatalogModal(fixture, kind, target); var dialog = Dialog(fixture);
        var text = ModalText(dialog); Assert.Contains(target, text); Assert.Contains("Workspace catalog", text); Assert.Contains("sample", text); Assert.Contains(kind switch
        {
            CatalogDeletionKind.RequiredField => "removed from program membership",
            CatalogDeletionKind.ReportKind => "Default report:",
            _ => "resets to dut",
        }, text);
        AssertInside(fixture.Control<Button>("Cancel destructive operation", dialog), dialog); AssertCancel(dialog);
        Cancel(dialog, route); Assert.Empty(fixture.Window!.OwnedWindows); Assert.True(fixture.Window!.IsVisible);
        Assert.Same(draft, fixture.ViewModel.SelectedProgram); Assert.False(fixture.ViewModel.HasUnsavedChanges); AssertFiles(files);
    }

    [AvaloniaTheory]
    [InlineData(CatalogDeletionKind.RequiredField)]
    [InlineData(CatalogDeletionKind.ReportKind)]
    [InlineData(CatalogDeletionKind.ProgramKind)]
    public void Real_workspace_delete_affirmation_stages_only_affected_programs_until_SaveAll_then_reloads(CatalogDeletionKind kind)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateProgram("a"); var target = AddCatalog(fixture, kind);
        vm.CreateProgram("b"); if (kind == CatalogDeletionKind.RequiredField) vm.SetRequiredFieldIncluded(target, false);
        if (kind == CatalogDeletionKind.ReportKind) vm.SetReportKindIncluded(target, false);
        if (kind == CatalogDeletionKind.ProgramKind) vm.ProgramKind = "dut";
        Assert.True(vm.SaveAll().Succeeded); var other = vm.SelectedProgram; vm.SelectProgram("a"); AuthoringUiFixture.Drain(); var files = Snapshot(fixture);
        OpenCatalogModal(fixture, kind, target); var dialog = Dialog(fixture); Assert.Contains("Affected programs: 1", ModalText(dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove from workspace", dialog));
        Assert.Empty(fixture.Window!.OwnedWindows); Assert.True(vm.WorkspaceCatalogDirty); Assert.Equal(new DirtyProgramSummary("a", false, true), Assert.Single(vm.DirtyPrograms));
        Assert.Same(other, vm.Programs.Single(p => p.PlanId == "b")); AssertFiles(files); Assert.Contains("Save All", fixture.Control<TextBlock>("Authoring status").Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all")); Assert.True(vm.LastSaveAllResult!.Succeeded); Assert.True(vm.LastSaveAllResult.WorkspaceCatalogSaved);
        Assert.Contains(fixture.Control<ItemsControl>("Save all results").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Saved workspace catalog");
        var reload = new AuthoringWorkspaceViewModel(); reload.Open(fixture.WorkspaceRoot);
        var options = kind switch { CatalogDeletionKind.RequiredField => reload.RequiredFieldOptions, CatalogDeletionKind.ReportKind => reload.ReportKindOptions, _ => reload.ProgramKindOptions };
        Assert.DoesNotContain(target, options);
    }

    [AvaloniaFact]
    public void Nested_settings_changed_during_real_confirmation_reject_stale_impact_without_writes()
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateDemoProgram("nested"); var target = AddCatalog(fixture, CatalogDeletionKind.RequiredField);
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.Repeat); Assert.True(vm.SaveAll().Succeeded); AuthoringUiFixture.Drain();
        OpenCatalogModal(fixture, CatalogDeletionKind.RequiredField, target);
        var nested = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure)); var metric = Assert.IsType<MetricNode>(Assert.Single(nested.Children));
        var settings = Assert.IsAssignableFrom<IDictionary<string, string>>(Assert.IsType<MeasureSource>(metric.Metric.Source).Settings); settings["Samples"] = "999";
        var draft = vm.SelectedProgram; var files = Snapshot(fixture);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove from workspace", Dialog(fixture)));
        Assert.Contains("changed since review", vm.Error); Assert.Same(draft, vm.SelectedProgram); Assert.Contains(target, RequiredFieldIds.FromSidecar(vm.SelectedProgram.Sidecar)); AssertFiles(files);
    }

    [AvaloniaTheory]
    [InlineData("selection")]
    [InlineData("session")]
    public void Catalog_confirmation_cannot_apply_to_a_changed_selection_or_reopened_session(string change)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateProgram("a"); var target = AddCatalog(fixture, CatalogDeletionKind.RequiredField); Assert.True(vm.SaveAll().Succeeded);
        OpenCatalogModal(fixture, CatalogDeletionKind.RequiredField, target);
        if (change == "selection") vm.SelectProgram("sample"); else vm.CommitOpen(vm.PrepareOpen(fixture.WorkspaceRoot));
        var before = vm.Programs; var files = Snapshot(fixture); AuthoringUiFixture.Click(fixture.Control<Button>("Remove from workspace", Dialog(fixture)));
        Assert.Same(before, vm.Programs); AssertFiles(files);
        if (change == "selection") Assert.Contains("changed during review", vm.Error); else Assert.Null(vm.Error);
        Assert.Contains(target, RequiredFieldIds.FromSidecar(vm.Programs.Single(p => p.PlanId == "a").Sidecar));
    }

    [AvaloniaFact]
    public async Task Destructive_request_blocks_concurrent_prompts_navigation_and_close_and_hidden_owner_rejects_late_choice()
    {
        using var fixture = Loaded(); var target = AddCatalog(fixture, CatalogDeletionKind.RequiredField); Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        OpenCatalogModal(fixture, CatalogDeletionKind.RequiredField, target); var dialog = Dialog(fixture);
        Assert.False(await fixture.Window!.ConfirmCatalogDeletionAsync(CatalogDeletionKind.RequiredField, target)); Assert.False(await fixture.Window!.ReopenWorkspaceAsync());
        fixture.Window!.Close(); Assert.True(fixture.Window!.IsVisible); Assert.Same(dialog, Assert.Single(fixture.Window!.OwnedWindows));
        var files = Snapshot(fixture); fixture.Window!.Hide(); dialog.Close(true); AuthoringUiFixture.Drain();
        Assert.Contains(target, fixture.ViewModel.RequiredFieldOptions); AssertFiles(files); Assert.False(fixture.ViewModel.WorkspaceCatalogDirty);
        fixture.Window!.Show(); AuthoringUiFixture.Drain();
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("enter")]
    [InlineData("escape")]
    [InlineData("dismiss")]
    public void Instrument_dialog_requires_explicit_choice_even_with_one_compatible_slot_and_cancels_without_writes(string route)
    {
        using var fixture = Loaded(); PrepareSlots(fixture); var draft = fixture.ViewModel.SelectedProgram; var files = Snapshot(fixture);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove instrument slot from selected program")); var dialog = Dialog(fixture);
        var chooser = fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog); Assert.Null(chooser.SelectedItem); Assert.Equal(-1, chooser.SelectedIndex); Assert.Single(chooser.Items);
        Assert.False(fixture.Control<Button>("Remove and replace slot", dialog).IsEnabled); Assert.Contains("Program 'slots' only", ModalText(dialog));
        Assert.Contains("Identity", ModalText(dialog)); Assert.Contains("Children", ModalText(dialog)); Assert.Contains("Cleanup", ModalText(dialog));
        AssertCancel(dialog); Cancel(dialog, route); Assert.Same(draft, fixture.ViewModel.SelectedProgram); AssertFiles(files); Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public void Instrument_replacement_controls_and_safe_choices_fit_supported_window_sizes(int width, int height)
    {
        using var fixture = Loaded(width, height); PrepareSlots(fixture);
        var remove = fixture.Control<Button>("Remove instrument slot from selected program"); remove.BringIntoView(); AuthoringUiFixture.Drain(); AssertInside(remove, fixture.Window!);
        AuthoringUiFixture.Click(remove); var dialog = Dialog(fixture);
        var chooser = fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog); chooser.BringIntoView(); AuthoringUiFixture.Drain();
        AssertInside(chooser, dialog); Assert.True(chooser.Focus()); Assert.Null(chooser.SelectedItem);
        AssertInside(fixture.Control<Button>("Cancel destructive operation", dialog), dialog); AssertInside(fixture.Control<Button>("Remove and replace slot", dialog), dialog);
        Assert.False(fixture.Control<Button>("Remove and replace slot", dialog).IsEnabled); Cancel(dialog, "escape"); Assert.Equal(2, fixture.ViewModel.InstrumentSlots.Count);
    }

    [AvaloniaFact]
    public void Explicit_instrument_choice_retargets_only_after_approval_and_program_save_persists_it()
    {
        using var fixture = Loaded(); PrepareSlots(fixture); var vm = fixture.ViewModel; var files = Snapshot(fixture); var draft = vm.SelectedProgram; var other = vm.Programs.Single(p => p.PlanId == "sample");
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove instrument slot from selected program")); var dialog = Dialog(fixture);
        var chooser = fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog); chooser.SelectedItem = "B"; AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<Button>("Remove and replace slot", dialog).IsEnabled); Assert.Same(draft, vm.SelectedProgram); AssertFiles(files);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove and replace slot", dialog)); Assert.Empty(fixture.Window!.OwnedWindows);
        Assert.Equal(["B"], vm.InstrumentSlots); Assert.Equal("B", Assert.Single(vm.SelectedProgram!.Setup.OfType<IdentitySetup>()).InstrumentSlot);
        Assert.All(AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram.Measure), m => Assert.Equal("B", Assert.IsType<MeasureSource>(m.Source).InstrumentSlot));
        Assert.Equal(["B"], vm.SelectedProgram.Cleanup.InstrumentSlots); Assert.Same(other, vm.Programs.Single(p => p.PlanId == "sample"));
        Assert.True(vm.HasUnsavedChanges); Assert.False(vm.WorkspaceCatalogDirty); AssertFiles(files); Assert.Contains("save the program", vm.Status);
        AuthoringUiFixture.Click(fixture.Control<Button>("Save plan")); Assert.False(vm.HasUnsavedChanges);
        var reload = new AuthoringWorkspaceViewModel(); reload.Open(fixture.WorkspaceRoot); reload.SelectProgram("slots"); Assert.Equal(["B"], reload.InstrumentSlots);
        Assert.Contains("B", reload.Workspace!.Manifest.Catalogs!.InstrumentSlotNames);
    }

    [AvaloniaFact]
    public void Instrument_confirmation_rejects_changed_selected_slot()
    {
        using var fixture = Loaded(); PrepareSlots(fixture); var vm = fixture.ViewModel; var files = Snapshot(fixture);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove instrument slot from selected program")); var dialog = Dialog(fixture);
        vm.SelectedInstrumentSlot = "B"; fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog).SelectedItem = "B"; AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove and replace slot", dialog)); Assert.Contains("selected program or instrument slot changed", vm.Error); Assert.Equal(2, vm.InstrumentSlots.Count); AssertFiles(files);
    }

    [AvaloniaTheory]
    [InlineData("last")]
    [InlineData("legacy")]
    public void Disabled_instrument_removal_explains_the_specific_blocker(string scenario)
    {
        using var fixture = scenario == "legacy"
            ? new AuthoringUiFixture(rememberWorkspace: true, compiler: new BlockerCompiler("legacy"))
            : Loaded();
        var vm = fixture.ViewModel;
        if (scenario == "legacy") { fixture.Show(); fixture.OpenRememberedWorkspace(); Settings(fixture); }
        else if (scenario == "last") vm.CreateDemoProgram("last");
        AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<Button>("Remove instrument slot from selected program").IsEnabled);
        var text = fixture.Control<TextBlock>("Instrument removal guidance").Text;
        Assert.Contains(scenario == "last" ? "at least one instrument" : "legacy instrument-based", text);
        Assert.Equal(text, AutomationProperties.GetHelpText(fixture.Control<Button>("Remove instrument slot from selected program")));
    }

    [AvaloniaTheory]
    [InlineData("unsupported", "unknown or unsupported instrument type")]
    [InlineData("different", "compatible registered instrument capabilities")]
    [InlineData("raw", "raw or unknown steps")]
    public void Conservative_instrument_compatibility_blockers_are_visible_and_accessible(string scenario, string message)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true, compiler: new BlockerCompiler(scenario));
        fixture.Show(); fixture.OpenRememberedWorkspace(); Settings(fixture);
        var remove = fixture.Control<Button>("Remove instrument slot from selected program"); Assert.False(remove.IsEnabled);
        var guidance = fixture.Control<TextBlock>("Instrument removal guidance"); guidance.BringIntoView(); AuthoringUiFixture.Drain();
        AssertInside(guidance, fixture.Window!); Assert.Contains(message, guidance.Text); Assert.Equal(guidance.Text, AutomationProperties.GetHelpText(remove));
    }

    [AvaloniaFact]
    public void Readonly_membership_controls_preserve_checked_state_and_core_draft()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var manifest = Path.Combine(fixture.WorkspaceRoot, "authoring.json"); File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal));
        fixture.Show(); fixture.OpenRememberedWorkspace(); Settings(fixture);
        var box = fixture.Control<CheckBox>("Include serial in selected program"); var before = box.IsChecked; var draft = fixture.ViewModel.SelectedProgram;
        Assert.False(box.IsEffectivelyEnabled); box.BringIntoView(); AuthoringUiFixture.Drain();
        var point = box.TranslatePoint(new Point(10, 10), fixture.Window!); Assert.NotNull(point); fixture.Window!.MouseDown(point.Value, MouseButton.Left); fixture.Window!.MouseUp(point.Value, MouseButton.Left); AuthoringUiFixture.Drain();
        Assert.Equal(before, box.IsChecked); Assert.Same(draft, fixture.ViewModel.SelectedProgram); Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData("cancel", false)]
    [InlineData("enter", false)]
    [InlineData("escape", false)]
    [InlineData("dismiss", false)]
    [InlineData("cancel", true)]
    [InlineData("enter", true)]
    [InlineData("escape", true)]
    [InlineData("dismiss", true)]
    public void Real_whole_program_modal_and_Delete_keyboard_route_cancel_without_deleting_named_target(string route, bool keyboard)
    {
        using var fixture = Loaded(); var draft = fixture.ViewModel.SelectedProgram; var files = Snapshot(fixture);
        OpenProgramModal(fixture, keyboard); var dialog = Dialog(fixture); Assert.Contains("sample", ModalText(dialog)); Assert.Contains(Sidecar(fixture, "sample"), ModalText(dialog));
        AssertCancel(dialog); Cancel(dialog, route); Assert.Same(draft, fixture.ViewModel.SelectedProgram); AssertFiles(files); Assert.Empty(fixture.Window!.OwnedWindows);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Affirmative_whole_program_removal_affects_only_named_program_and_stale_selection_is_rejected(bool stale)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateProgram("other"); Assert.True(vm.SaveAll().Succeeded); vm.SelectProgram("sample"); AuthoringUiFixture.Drain();
        OpenProgramModal(fixture, true); var dialog = Dialog(fixture); var otherBytes = File.ReadAllBytes(Sidecar(fixture, "other")); var files = Snapshot(fixture);
        if (stale) vm.SelectProgram("other"); AuthoringUiFixture.Click(fixture.Control<Button>("Remove program", dialog));
        Assert.Equal(otherBytes, File.ReadAllBytes(Sidecar(fixture, "other")));
        if (stale) { AssertFiles(files); Assert.Contains("sample", vm.Programs.Select(p => p.PlanId)); }
        else { Assert.DoesNotContain("sample", vm.Programs.Select(p => p.PlanId)); Assert.False(File.Exists(Sidecar(fixture, "sample"))); Assert.False(File.Exists(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"))); }
    }

    [AvaloniaFact]
    public void Whole_program_request_commits_focused_LostFocus_editor_before_describing_target()
    {
        using var fixture = Loaded(); fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0; fixture.ViewModel.SelectMeasure(0); AuthoringUiFixture.Drain();
        var channel = fixture.Control<AutoCompleteBox>("Channel key"); channel.BringIntoView(); AuthoringUiFixture.Drain(); var box = Assert.Single(channel.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus()); box.SelectAll(); fixture.Window!.KeyTextInput("pending-destructive-edit"); AuthoringUiFixture.Drain(); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove program")); Assert.Equal("pending-destructive-edit", fixture.ViewModel.ChannelKey); Assert.True(fixture.ViewModel.HasUnsavedChanges); Cancel(Dialog(fixture), "cancel");
    }

    [AvaloniaFact]
    public async Task Catalog_request_commits_pending_LostFocus_edit_before_fingerprinting_and_can_apply_the_reviewed_impact()
    {
        using var fixture = Loaded(); var target = AddCatalog(fixture, CatalogDeletionKind.RequiredField); Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0; fixture.ViewModel.SelectMeasure(0); AuthoringUiFixture.Drain();
        var channel = fixture.Control<AutoCompleteBox>("Channel key"); channel.BringIntoView(); AuthoringUiFixture.Drain(); var box = Assert.Single(channel.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus()); box.SelectAll(); fixture.Window!.KeyTextInput("pending-impact-channel"); AuthoringUiFixture.Drain(); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        var files = Snapshot(fixture); var request = fixture.Window!.ConfirmCatalogDeletionAsync(CatalogDeletionKind.RequiredField, target);
        Assert.Equal("pending-impact-channel", fixture.ViewModel.ChannelKey);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove from workspace", Dialog(fixture))); Assert.True(await request);
        Assert.DoesNotContain(target, fixture.ViewModel.RequiredFieldOptions); Assert.Equal("pending-impact-channel", fixture.ViewModel.ChannelKey); AssertFiles(files);
    }

    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public void Workspace_impact_many_programs_scrolls_with_cancel_visible_at_supported_sizes(int width, int height)
    {
        using var fixture = Loaded(width, height); var vm = fixture.ViewModel; AddCatalog(fixture, CatalogDeletionKind.RequiredField);
        for (var i = 0; i < 35; i++) { vm.CreateProgram($"impact-{i:00}-{new string('x', 110)}"); vm.SetRequiredFieldIncluded("fixtureId", true); }
        AuthoringUiFixture.Drain(); OpenCatalogModal(fixture, CatalogDeletionKind.RequiredField, "fixtureId"); var dialog = Dialog(fixture);
        var scroll = fixture.Control<ScrollViewer>("Destructive operation scope and impact", dialog); AssertInside(scroll, dialog); Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string)) AssertInside(button, dialog);
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), dialog); Assert.NotNull(point); dialog.MouseWheel(point.Value, new Vector(0, -1000), RawInputModifiers.None); AuthoringUiFixture.Drain(); Assert.True(scroll.Offset.Y > 0);
        Assert.Contains(vm.Programs[^1].PlanId, ModalText(dialog));
        var finalText = Assert.Single(Assert.IsType<StackPanel>(scroll.Content).Children.OfType<TextBlock>());
        AssertFinalLineVisible(finalText, scroll); Cancel(dialog, "cancel");
        Definitions(fixture); var remove = fixture.Control<Button>("Remove required field fixtureId from workspace"); remove.BringIntoView(); AuthoringUiFixture.Drain(); AssertInside(remove, fixture.Window!);
    }

    [AvaloniaTheory]
    [InlineData("catalog", "hidden")]
    [InlineData("catalog", "context")]
    [InlineData("catalog", "session")]
    [InlineData("instrument", "hidden")]
    [InlineData("instrument", "context")]
    [InlineData("instrument", "session")]
    [InlineData("binding", "hidden")]
    [InlineData("binding", "context")]
    [InlineData("binding", "session")]
    [InlineData("definition", "hidden")]
    [InlineData("definition", "context")]
    [InlineData("definition", "session")]
    [InlineData("program", "hidden")]
    [InlineData("program", "context")]
    [InlineData("program", "session")]
    public async Task Every_modal_rejects_obsolete_owner_without_mutation_or_stale_error(string operation, string transition)
    {
        using var fixture = Loaded(); using var replacement = new AuthoringUiFixture(); var vm = fixture.ViewModel;
        PrepareSlots(fixture); var target = AddCatalog(fixture, CatalogDeletionKind.RequiredField);
        vm.LoadHardwareEditor(); vm.NewInstrumentSlot = "owner-template"; vm.AddHardwareDefinition();
        Assert.Null(vm.Error); Assert.True(vm.SaveAll().Succeeded); await vm.StopRecoveryAsync();
        var prepared = vm.PrepareOpen(fixture.WorkspaceRoot); vm.CommitOpen(prepared); await vm.StopRecoveryAsync();
        vm.SelectProgram("slots"); vm.SelectedInstrumentSlot = "DMM"; vm.LoadHardwareEditor(); vm.HardwareEditAddress = "MOCK::OWNER-REPLACEMENT";
        vm.SelectedHardwareDefinition = Assert.Single(vm.HardwareDefinitions);
        Task<bool> request = operation switch
        {
            "catalog" => fixture.Window!.ConfirmCatalogDeletionAsync(CatalogDeletionKind.RequiredField, target),
            "instrument" => fixture.Window!.ConfirmInstrumentRemovalAsync(),
            "binding" => fixture.Window!.ConfirmHardwareEditAsync(),
            "definition" => fixture.Window!.ConfirmHardwareDefinitionRemovalAsync(),
            _ => (Task<bool>)typeof(MainWindow).GetMethod("ConfirmRemoveProgramAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(fixture.Window, null)!
        };
        AuthoringUiFixture.Drain(); Assert.False(request.IsCompleted); var dialog = Dialog(fixture);
        if (operation == "instrument") fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog).SelectedItem = "B";
        if (transition == "hidden") fixture.Window!.Hide();
        else if (transition == "context")
        {
            replacement.ViewModel.Open(replacement.WorkspaceRoot); await replacement.ViewModel.StopRecoveryAsync();
            fixture.Window!.DataContext = replacement.ViewModel;
        }
        else
        {
            var workspace = vm.Workspace; var oldSession = vm.WorkspaceSessionId;
            vm.CommitOpen(prepared, discardUnsavedChanges: true); await vm.StopRecoveryAsync(); vm.SelectProgram("slots");
            Assert.Same(workspace, vm.Workspace); Assert.NotEqual(oldSession, vm.WorkspaceSessionId);
        }
        var draft = vm.SelectedProgram; var revision = vm.SelectedDocument!.Revision; var status = vm.Status;
        var files = Directory.GetFiles(fixture.WorkspaceRoot, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        try
        {
            if (operation == "instrument") dialog.Close("B"); else dialog.Close(true);
            Assert.False(await request);
            Assert.Same(draft, vm.SelectedProgram); Assert.Equal(revision, vm.SelectedDocument.Revision); Assert.Equal(status, vm.Status);
            Assert.Null(vm.Error); Assert.Null(replacement.ViewModel.Error); Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(files.Keys.Order(), Directory.GetFiles(fixture.WorkspaceRoot, "*", SearchOption.AllDirectories).Order());
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            fixture.Window!.DataContext = vm;
            if (!fixture.Window.IsVisible) fixture.Window.Show();
            AuthoringUiFixture.Drain();
        }
    }

    private static AuthoringUiFixture Loaded(double width = 1280, double height = 800, bool realLifecycle = false)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(width, height, realLifecycle); fixture.OpenRememberedWorkspace(); Settings(fixture); return fixture;
    }
    private static void Settings(AuthoringUiFixture fixture) { fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain(); }
    private static void Definitions(AuthoringUiFixture fixture) { fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 6; AuthoringUiFixture.Drain(); }
    private static string AddCatalog(AuthoringUiFixture fixture, CatalogDeletionKind kind)
    {
        Definitions(fixture); var target = kind == CatalogDeletionKind.RequiredField ? "fixtureId" : "custom";
        var type = kind switch { CatalogDeletionKind.RequiredField => "required field", CatalogDeletionKind.ReportKind => "report kind", _ => "program kind" };
        fixture.Type(fixture.Control<TextBox>($"New {type}"), target); AuthoringUiFixture.Click(fixture.Control<Button>($"Add {type}"));
        if (kind == CatalogDeletionKind.RequiredField) fixture.ViewModel.SetRequiredFieldIncluded(target, true);
        else if (kind == CatalogDeletionKind.ReportKind) fixture.ViewModel.SetReportKindIncluded(target, true);
        else fixture.ViewModel.ProgramKind = target;
        Settings(fixture);
        return target;
    }
    private static void OpenCatalogModal(AuthoringUiFixture fixture, CatalogDeletionKind kind, string target)
    {
        Definitions(fixture); var type = kind switch { CatalogDeletionKind.RequiredField => "required field", CatalogDeletionKind.ReportKind => "report kind", _ => "program kind" };
        var button = fixture.Control<Button>($"Remove {type} {target} from workspace"); button.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(button);
    }
    private static void PrepareSlots(AuthoringUiFixture fixture)
    {
        var vm = fixture.ViewModel; vm.CreateDemoProgram("slots"); vm.NewInstrumentSlot = "B"; vm.AddInstrumentSlot(); vm.SelectedInstrumentSlot = "DMM"; vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        vm.SelectedInstrumentSlot = "DMM"; Assert.True(vm.SaveAll().Succeeded); Settings(fixture); fixture.Control<Button>("Remove instrument slot from selected program").BringIntoView(); AuthoringUiFixture.Drain();
    }
    private static Window Dialog(AuthoringUiFixture fixture) => Assert.Single(fixture.Window!.OwnedWindows);
    private static string ModalText(Window dialog) => string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    private static void AssertCancel(Window dialog)
    {
        var cancel = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel")); Assert.True(cancel.IsDefault); Assert.True(cancel.IsCancel); Assert.True(cancel.IsFocused);
    }
    private static void Cancel(Window dialog, string route)
    {
        if (route == "cancel") AuthoringUiFixture.Click(Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel")));
        else if (route == "dismiss") dialog.Close();
        else { var key = route == "enter" ? Key.Enter : Key.Escape; dialog.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null); AuthoringUiFixture.Drain(); }
    }
    private static void PressSpace(Window window, CheckBox box)
    {
        box.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(box.Focus()); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); AuthoringUiFixture.Drain();
    }
    private static void OpenProgramModal(AuthoringUiFixture fixture, bool keyboard)
    {
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0; AuthoringUiFixture.Drain();
        if (!keyboard) AuthoringUiFixture.Click(fixture.Control<Button>("Remove program"));
        else
        {
            var item = Assert.Single(fixture.Control<ListBox>("Programs").GetVisualDescendants().OfType<ListBoxItem>(), row => row.IsSelected);
            item.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(item.Focus());
            fixture.Window!.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); AuthoringUiFixture.Drain();
        }
    }
    private static string Sidecar(AuthoringUiFixture fixture, string id) => Path.Combine(fixture.WorkspaceRoot, id + ".program.json");
    private static Dictionary<string, byte[]> Snapshot(AuthoringUiFixture fixture) => Directory.EnumerateFiles(fixture.WorkspaceRoot).ToDictionary(p => p, File.ReadAllBytes);
    private static void AssertFiles(Dictionary<string, byte[]> files) { foreach (var entry in files) Assert.Equal(entry.Value, File.ReadAllBytes(entry.Key)); }
    private static void AssertFinalLineVisible(TextBlock text, ScrollViewer scroll)
    {
        var point = text.TranslatePoint(default, scroll); Assert.NotNull(point);
        var bottom = point.Value.Y + text.Bounds.Height; var top = bottom - text.TextLayout.TextLines.Last().Height;
        Assert.True(top >= -1 && bottom <= scroll.Bounds.Height + 1, $"Final line {top}–{bottom} must fit viewport {scroll.Bounds.Height}.");
    }
    private static void AssertInside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible); var point = control.TranslatePoint(default, window); Assert.NotNull(point); Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(point.Value, control.Bounds.Size)), $"{control.GetType().Name} at {point} ({control.Bounds.Size}) must fit inside {window.ClientSize}.");
    }
    private sealed class BlockerCompiler(string scenario) : IPlanCompiler
    {
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace)
        {
            var draft = AuthoringRecipeCatalog.CreateProgram("blocked"); var known = draft.Instruments[0].TypeId;
            draft = draft with
            {
                Instruments = [draft.Instruments[0] with { TypeId = scenario == "unsupported" ? "Unknown.Adapter" : known }, new InstrumentRef("B", scenario == "different" ? "Different.Adapter" : known, "MOCK::B")],
                Measure = scenario switch
                {
                    "raw" => [new RawStepNode("Unknown.Step", "<step/>")],
                    "legacy" => [new MetricNode(new MetricDraft("Legacy mean", "VDC.mean", "scalar", "V", new LimitSpec(null, null, 1.2), null,
                        new AlgorithmSource(AuthoringFunctionIds.BasicMeanGte, [], new Dictionary<string, string>())))],
                    _ => []
                },
            };
            return new(workspace, [draft]);
        }
        public ProgramDraft Load(string path) => throw new NotSupportedException();
        public void Save(ProgramDraft draft, string path) => throw new NotSupportedException();
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new NotSupportedException();
    }

}
