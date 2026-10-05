using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringPlanInitializationTests
{
    [AvaloniaFact]
    public void Programs_entry_walks_all_six_stages_preserves_incomplete_text_and_opens_normal_document()
    {
        using var fixture = Loaded();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Type(fixture, dialog, "Plan display name", "Rail checks");
        Type(fixture, dialog, "Stable plan ID", "rail-checks");
        Type(fixture, dialog, "Device family", "board-x");
        Assert.Contains("rail-checks.authoring.json", fixture.Control<TextBox>("Draft destination", dialog).Text);
        Next(fixture, dialog, "Starting point");
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 1;
        Next(fixture, dialog, "Hardware");
        Assert.Equal(0, fixture.Control<ComboBox>("Hardware choice", dialog).SelectedIndex);
        Next(fixture, dialog, "Setup and cleanup");
        Type(fixture, dialog, "Required operator fields", "fixtureId");
        Type(fixture, dialog, "Fixture confirmation", "Fixture seated?");
        Type(fixture, dialog, "Fixture input field", "fixtureId");
        Next(fixture, dialog, "First measurement and criterion");
        Type(fixture, dialog, "Output channel", "rail.voltage");
        Type(fixture, dialog, "Sample count", "pending");
        Next(fixture, dialog, "Review and create");
        var review = fixture.Control<TextBlock>("Initialization review", dialog).Text;
        Assert.Contains("board-x", review); Assert.Contains("fixtureId", review); Assert.Contains("pending", review);
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("rail-checks").Exists);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(fixture.Window.OwnedWindows); Assert.Equal("rail-checks", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Empty(fixture.ViewModel.SelectedProgram.Instruments); Assert.NotNull(fixture.ViewModel.SelectedSequence?.NodeId);
        Assert.False(fixture.ViewModel.HasUnsavedChanges, $"Dirty: {string.Join(", ", fixture.ViewModel.DirtyPrograms)}; catalog: {fixture.ViewModel.WorkspaceCatalogDirty}"); Assert.True(fixture.ViewModel.HasUncompiledSources);
        Assert.Contains(fixture.ViewModel.EditingIssues, issue => issue.Code == "MISSING_INSTRUMENT_BINDING");
        var id = fixture.ViewModel.SelectedSequence!.NodeId;
        fixture.ViewModel.DisplayName = "Edited"; Assert.True(fixture.ViewModel.CanUndo); fixture.ViewModel.Undo(); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        fixture.ViewModel.Open(fixture.WorkspaceRoot); fixture.ViewModel.SelectProgram("rail-checks");
        Assert.Equal(id, Assert.Single(fixture.ViewModel.SelectedProgram!.Measure).NodeId);
        Assert.Contains("pending", fixture.ViewModel.SelectedProgram.AuthoringState.IncompleteNumericText.Values);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Command_entry_and_cancel_never_publish_or_mutate_programs(bool reviewFirst)
    {
        using var fixture = Loaded(); var before = fixture.ViewModel.Programs.ToArray();
        AuthoringUiFixture.Click(fixture.Control<Button>("Command palette"));
        var palette = Assert.Single(fixture.Window!.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan command", palette));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        if (reviewFirst) for (var stage = 0; stage < 5; stage++) AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
        Assert.Empty(fixture.Window.OwnedWindows); Assert.Equal(before, fixture.ViewModel.Programs);
        Assert.Empty(new AuthoringDocumentStore(fixture.WorkspaceRoot).ListDocumentIds());
    }

    [AvaloniaFact]
    public void Explicit_demo_and_criterion_review_shows_mock_shutdown_and_valid_criterion_choices()
    {
        using var fixture = Loaded(); AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Type(fixture, dialog, "Stable plan ID", "demo-task");
        Next(fixture, dialog, "Starting point"); fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 2;
        Next(fixture, dialog, "Hardware"); Assert.Equal(2, fixture.Control<ComboBox>("Hardware choice", dialog).SelectedIndex);
        Assert.Contains("Mock DMM", fixture.Control<TextBlock>("Hardware readiness", dialog).Text);
        Next(fixture, dialog, "Setup and cleanup"); fixture.Control<CheckBox>("Check instrument identity", dialog).IsChecked = true;
        Assert.Contains("DMM", fixture.Control<TextBlock>("Shutdown coverage", dialog).Text);
        Next(fixture, dialog, "First measurement and criterion"); fixture.Control<CheckBox>("Mean greater than or equal criterion", dialog).IsChecked = true;
        Type(fixture, dialog, "Pass threshold", "1.25"); Type(fixture, dialog, "Output channel", "rail.mean");
        Next(fixture, dialog, "Review and create"); Assert.Contains("Mock DMM", fixture.Control<TextBlock>("Initialization review", dialog).Text);
        Assert.Contains("DemoVoltageTask", fixture.Control<TextBlock>("Initialization review", dialog).Text);
        Assert.Contains("rail.mean", fixture.Control<TextBlock>("Initialization review", dialog).Text); Assert.Contains("1.25", fixture.Control<TextBlock>("Initialization review", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var draft = fixture.ViewModel.SelectedProgram!; Assert.Equal("DMM", Assert.Single(draft.Instruments).SlotName);
        Assert.Single(draft.Setup.OfType<IdentitySetup>()); Assert.Equal(["DMM"], draft.Cleanup.InstrumentSlots);
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure)); Assert.Equal("rail.mean", metric.Metric.ChannelKey); Assert.Equal(1.25, metric.Metric.Limits!.Threshold);
        Assert.False(File.Exists(Path.Combine(fixture.WorkspaceRoot, "demo-task.TapPlan")));
    }

    [AvaloniaFact]
    public void Keyboard_command_and_optional_task_measurement_can_be_cancelled_or_saved_without_mock()
    {
        using var fixture = Loaded();
        fixture.Window!.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, null); AuthoringUiFixture.Drain();
        var palette = Assert.Single(fixture.Window!.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan command", palette));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        Type(fixture, dialog, "Stable plan ID", "optional-task"); Next(fixture, dialog, "Starting point");
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 1;
        Next(fixture, dialog, "Hardware"); Next(fixture, dialog, "Setup and cleanup"); Next(fixture, dialog, "First measurement and criterion");
        fixture.Control<CheckBox>("Include first measurement", dialog).IsChecked = false;
        Next(fixture, dialog, "Review and create"); Assert.Contains("VoltageTask", fixture.Control<TextBlock>("Initialization review", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(fixture.ViewModel.SelectedProgram!.Measure); Assert.Empty(fixture.ViewModel.SelectedProgram.Instruments); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan")); dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AuthoringUiFixture.Drain();
        Assert.Empty(fixture.Window.OwnedWindows); Assert.Equal(["optional-task"], new AuthoringDocumentStore(fixture.WorkspaceRoot).ListDocumentIds());
    }

    [AvaloniaFact]
    public void Missing_VISA_dependency_remains_visible_in_review_and_saved_reopened_draft()
    {
        using var fixture = Loaded();
        fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "missing-home");
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Type(fixture, dialog, "Stable plan ID", "real-voltage"); Next(fixture, dialog, "Starting point");
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 1;
        Next(fixture, dialog, "Hardware"); fixture.Control<ComboBox>("Hardware choice", dialog).SelectedIndex = 1;
        Type(fixture, dialog, "Instrument address", "TCPIP::192.0.2.1::INSTR");
        Assert.Contains("VISA DMM", fixture.Control<TextBlock>("Hardware readiness", dialog).Text);
        Next(fixture, dialog, "Setup and cleanup"); fixture.Control<CheckBox>("Check instrument identity", dialog).IsChecked = true;
        Next(fixture, dialog, "First measurement and criterion"); Next(fixture, dialog, "Review and create");
        Assert.Contains("Reinstall 'HardwareTest VISA'", fixture.Control<TextBlock>("Initialization review", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var instrument = Assert.Single(fixture.ViewModel.SelectedProgram!.Instruments);
        Assert.Equal("TCPIP::192.0.2.1::INSTR", instrument.VisaAddress);
        Assert.Equal("VISA DMM", AuthoringInstrumentCatalog.All.Single(adapter => adapter.TypeId == instrument.TypeId).DisplayName);
        Assert.Contains(fixture.ViewModel.EditingIssues, issue => issue.Code == "INSTRUMENT_UNAVAILABLE");
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        fixture.ViewModel.Open(fixture.WorkspaceRoot); fixture.ViewModel.SelectProgram("real-voltage");
        Assert.Contains(fixture.ViewModel.EditingIssues, issue => issue.Code == "INSTRUMENT_UNAVAILABLE");
        var reopened = Assert.Single(fixture.ViewModel.SelectedProgram!.Instruments);
        Assert.Equal(instrument.TypeId, reopened.TypeId); Assert.Equal(instrument.SlotName, reopened.SlotName);
        Assert.Equal(instrument.VisaAddress, reopened.VisaAddress); Assert.Equal(instrument.Settings, reopened.Settings);
    }

    [AvaloniaFact]
    public void Opening_saved_task_preserves_implicit_enum_default_but_actual_choice_commits_and_undo_redo_restore_it()
    {
        using var fixture = Loaded();
        fixture.ViewModel.InitializePlan(new PlanInitializationRequest("enum-task") { StartingPoint = PlanStartingPoint.DemoVoltageTask });
        AuthoringUiFixture.Drain();
        Assert.False(fixture.ViewModel.HasUnsavedChanges); Assert.False(fixture.ViewModel.CanUndo);
        var settings = fixture.Control<ItemsControl>("Recipe settings");
        var choice = Assert.Single(settings.GetVisualDescendants().OfType<ComboBox>(), control => control.DataContext is AuthoringSettingRow { Key: "SeriesCompliance" } && control.IsEffectivelyVisible);
        var source = Assert.IsType<MeasureSource>(fixture.ViewModel.SelectedMetric!.Source); Assert.False(source.Settings.ContainsKey("SeriesCompliance"));
        choice.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(choice.Focus());
        Assert.True(fixture.Control<Button>("Save all").Focus()); AuthoringUiFixture.Drain(); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        choice.SelectedItem = "allSamples"; AuthoringUiFixture.Drain();
        Assert.Equal("allSamples", Assert.IsType<MeasureSource>(fixture.ViewModel.SelectedMetric!.Source).Settings["SeriesCompliance"]);
        Assert.True(fixture.ViewModel.HasUnsavedChanges); Assert.True(fixture.ViewModel.CanUndo);
        fixture.ViewModel.Undo(); AuthoringUiFixture.Drain(); Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(Assert.IsType<MeasureSource>(fixture.ViewModel.SelectedMetric!.Source).Settings.ContainsKey("SeriesCompliance"));
        fixture.ViewModel.Redo(); AuthoringUiFixture.Drain(); Assert.Equal("allSamples", Assert.IsType<MeasureSource>(fixture.ViewModel.SelectedMetric!.Source).Settings["SeriesCompliance"]);
    }

    [AvaloniaFact]
    public void External_collision_at_Create_retains_dialog_and_existing_bytes()
    {
        using var fixture = Loaded(); AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Type(fixture, dialog, "Stable plan ID", "collision");
        for (var stage = 0; stage < 5; stage++) AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        var path = new AuthoringDocumentStore(fixture.WorkspaceRoot).GetDocumentPath("collision"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "external");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Same(dialog, Assert.Single(fixture.Window.OwnedWindows)); Assert.Equal("external", File.ReadAllText(path));
        Assert.Contains("already exists", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.DoesNotContain(fixture.ViewModel.Programs, draft => draft.PlanId == "collision");
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
    }

    private static AuthoringUiFixture Loaded()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(); fixture.OpenRememberedWorkspace(); return fixture;
    }
    private static void Type(AuthoringUiFixture fixture, Window dialog, string field, string text)
    {
        var box = fixture.Control<TextBox>(field, dialog); box.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(box.Focus()); box.SelectAll(); dialog.KeyTextInput(text); AuthoringUiFixture.Drain(); Assert.Equal(text, box.Text);
    }
    private static void Next(AuthoringUiFixture fixture, Window dialog, string stage)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        Assert.Contains(stage, fixture.Control<TextBlock>("Initialization stage", dialog).Text); Assert.True(string.IsNullOrEmpty(fixture.Control<TextBlock>("Initialization error", dialog).Text));
    }
}
