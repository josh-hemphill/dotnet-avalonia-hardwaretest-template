using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.Authoring.Tests;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class WorkspaceTaskLayoutTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public void Actual_checked_findings_remain_usable_with_editing_findings_expanded(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var path = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var xml = System.Xml.Linq.XDocument.Load(path);
        var acquisition = xml.Descendants("TestStep").Single(step => ((string?)step.Attribute("type"))?.EndsWith("AcquireVoltageStep", StringComparison.Ordinal) == true);
        acquisition.Add(new System.Xml.Linq.XElement("SeriesCompliance", "allSamples")); xml.Save(path);
        fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "unavailable-home");
        fixture.Show(960, 600); fixture.Window!.FontSize = fontSize; fixture.OpenRememberedWorkspace();
        fixture.ViewModel.Validate(); AuthoringUiFixture.Drain();
        Assert.NotEmpty(fixture.ViewModel.EditingIssues); Assert.NotEmpty(fixture.ViewModel.FindingRows);
        fixture.Window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 2; AuthoringUiFixture.Drain();
        fixture.Control<Expander>("Editing findings").IsExpanded = true; AuthoringUiFixture.Drain();
        var list = fixture.Control<ListBox>("Contract findings");
        list.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(list.Bounds.Height >= 90); ResponsiveShellTests.Inside(list, fixture.Window);
        var row = fixture.ViewModel.FindingRows.First();
        var fields = list.GetVisualDescendants().OfType<TextBlock>().Where(field => Equals(field.DataContext, row));
        foreach (var field in fields.Where(field => field.IsEffectivelyVisible))
        {
            field.BringIntoView(); AuthoringUiFixture.Drain();
            Assert.True(field.Bounds.Height > 0); ResponsiveShellTests.Inside(field, fixture.Window);
        }
        var navigation = Assert.Single(list.GetVisualDescendants().OfType<Button>(), button => Equals(button.DataContext, row));
        navigation.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(navigation, fixture.Window);
        ResponsiveActionLabelTests.LabelFits(navigation, fixture.Window);
    }

    [AvaloniaFact]
    public async Task Actual_workspace_load_check_and_home_changes_refresh_rendered_editing_findings_without_a_draft_edit()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var home = Path.Combine(fixture.WorkspaceRoot, "selected-home");
        var package = Path.Combine(home, "Packages", "HardwareTest Basic"); Directory.CreateDirectory(package);
        var metadata = Path.Combine(package, "package.xml");
        File.WriteAllText(metadata, "<Package Name=\"HardwareTest Basic\" Version=\"0.3.0\"><Files><File Path=\"HardwareTest.OpenTap.Plugins.Basic.dll\"/></Files></Package>");
        fixture.ViewModel.OpenTapHomeOverride = home;
        fixture.Show(960, 600); fixture.OpenRememberedWorkspace();
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        fixture.Control<Expander>("Editing findings").IsExpanded = true; AuthoringUiFixture.Drain();
        var rows = fixture.Control<ItemsControl>("Editing findings");
        Assert.NotEmpty(fixture.ViewModel.EditingIssues);
        MatchesCurrentIssues();
        // Real adapter payload appears without changing the selected path or editing the plan.
        File.Copy(typeof(HardwareTest.OpenTap.Plugins.Basic.MockDmmInstrument).Assembly.Location,
            Path.Combine(package, "HardwareTest.OpenTap.Plugins.Basic.dll"));
        fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(typeof(MainWindow).Assembly.Location), action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        AuthoringUiFixture.Click(fixture.Control<Button>("Validate workspace"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (fixture.ViewModel.OperationBusy) { AuthoringUiFixture.Drain(); await Task.Delay(10, timeout.Token); }
        AuthoringUiFixture.Drain(); Assert.Null(fixture.ViewModel.Error);
        Assert.Empty(fixture.ViewModel.EditingIssues); MatchesCurrentIssues();
        fixture.ViewModel.OpenTapHomeOverride = Path.Combine(fixture.WorkspaceRoot, "other-unavailable-home"); AuthoringUiFixture.Drain();
        Assert.NotEmpty(fixture.ViewModel.EditingIssues); MatchesCurrentIssues();
        Assert.False(fixture.ViewModel.HasUnsavedChanges);

        void MatchesCurrentIssues()
        {
            Assert.Equal(fixture.ViewModel.EditingIssues, rows.ItemsSource!.Cast<AuthoringEditingIssue>());
            Assert.Contains(fixture.Window!.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == fixture.ViewModel.IssuesSummary);
        }
    }

    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public void Expanded_editing_findings_show_provenance_and_scroll_to_each_real_navigation_action(int fontSize)
    {
        using var fixture = Loaded();
        fixture.ViewModel.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
        fixture.ViewModel.FormulaSource = "mean(VDC)";
        fixture.ViewModel.Threshold = "";
        fixture.ViewModel.CreateDemoProgram("second-findings-program");
        fixture.ViewModel.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        fixture.ViewModel.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
        fixture.ViewModel.FormulaSource = "mean(VDC)";
        fixture.ViewModel.Threshold = "";
        var issues = fixture.ViewModel.EditingIssues.Where(item => item.Code == AuthoringCompileCodes.MissingLimits).ToArray();
        Assert.Equal(2, issues.Length);
        fixture.Window!.FontSize = fontSize;
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        fixture.Control<Expander>("Editing findings").IsExpanded = true; AuthoringUiFixture.Drain();
        var viewport = fixture.Control<ScrollViewer>("Editing findings viewport");
        Assert.True(viewport.Viewport.Width > 300);
        Assert.True(viewport.Viewport.Height > 100);
        var rows = fixture.Control<ItemsControl>("Editing findings");
        foreach (var issue in issues)
        {
            var fields = rows.GetVisualDescendants().OfType<TextBlock>().Where(text => Equals(text.DataContext, issue)).ToArray();
            foreach (var expected in new[] { issue.Severity.ToString(), issue.Code, "Program: " + issue.PlanId, issue.Message,
                "Editing draft — separate from saved-plan checks", "Section: " + issue.Section, "Field: " + issue.Field })
            {
                var field = Assert.Single(fields, text => text.Text == expected);
                InViewport(field);
            }
            InViewport(Assert.Single(rows.GetVisualDescendants().OfType<Button>(), button => Equals(button.DataContext, issue)));
        }
        Assert.True(viewport.Offset.Y > 0);
        var empty = fixture.Control<TextBlock>("Saved-plan empty state");
        empty.BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(empty, fixture.Window!);
        var open = Assert.Single(rows.GetVisualDescendants().OfType<Button>(), button => Equals(button.DataContext, issues[^1]));
        InViewport(open);
        AuthoringUiFixture.Click(open);
        Assert.Equal(issues[^1].PlanId, fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Equal(0, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.True(fixture.Control<TextBox>("Threshold").IsFocused);

        void InViewport(Control control)
        {
            control.BringIntoView(); AuthoringUiFixture.Drain();
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
            ResponsiveShellTests.Inside(control, fixture.Window!);
            var position = control.TranslatePoint(default, viewport)!.Value;
            Assert.True(position.X >= 0 && position.Y >= 0);
            Assert.True(position.X + control.Bounds.Width <= viewport.Viewport.Width + 1);
            Assert.True(position.Y + control.Bounds.Height <= viewport.Viewport.Height + 1);
        }
    }

    [AvaloniaFact]
    public void Build_distinguishes_compiled_only_input_from_a_saved_editable_revision_and_keeps_pack_near_readiness()
    {
        using var fixture = Loaded();
        fixture.Window!.Width = 1280; fixture.Window.Height = 800;
        fixture.Window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 4;
        AuthoringUiFixture.Drain();
        var programs = fixture.Control<ItemsControl>("Build program inclusion");
        Assert.Contains(programs.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Compiled input only — no editable source revision");
        var pack = fixture.Control<Button>("Pack workspace");
        ResponsiveShellTests.Inside(pack, fixture.Window!);
        fixture.ViewModel.DisplayName = "Saved editable build title";
        Assert.True(fixture.ViewModel.SaveAll().Succeeded); AuthoringUiFixture.Drain();
        Assert.Contains(programs.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Saved source revision ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(programs.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved source revision ");
    }

    [AvaloniaFact]
    public void Instruments_exposes_bindings_and_operator_task_owns_identity()
    {
        using var fixture = Loaded();
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        var bindings = fixture.Control<ItemsControl>("Hardware binding table");
        bindings.BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(bindings, fixture.Window!);
        fixture.NavigateTask(7);
        var identity = fixture.Control<TextBox>("Display name");
        identity.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(identity, fixture.Window!);
        fixture.Type(identity, "Hardware entry retained program title");
        Assert.Equal("Hardware entry retained program title", fixture.ViewModel.DisplayName);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Separate_plan_form_inherits_owner_font_and_decisions_remain_reachable_in_every_stage(bool guided)
    {
        using var fixture = Loaded();
        fixture.Window!.FontSize = 20;
        AuthoringUiFixture.Click(fixture.Control<Button>(guided ? "Start guided voltage test" : "New test plan"));
        var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window.OwnedWindows));
        dialog.Width = 480; dialog.Height = 460;
        AuthoringUiFixture.Drain();
        Assert.Equal(20, dialog.FontSize);
        Assert.Equal(20, fixture.Control<TextBox>("Plan display name", dialog).FontSize);
        Assert.DoesNotContain("voltage", dialog.Title!, StringComparison.OrdinalIgnoreCase);
        for (var stage = 0; stage < 6; stage++)
        {
            var decision = fixture.Control<Button>(stage == 5 ? "Create test plan" : "Next", dialog);
            ResponsiveShellTests.Inside(decision, dialog);
            ResponsiveActionLabelTests.LabelFits(decision, dialog);
            ResponsiveShellTests.Inside(fixture.Control<Button>("Cancel", dialog), dialog);
            if (stage > 0) ResponsiveShellTests.Inside(fixture.Control<Button>("Back", dialog), dialog);
            foreach (var input in dialog.GetVisualDescendants().OfType<TextBox>().Where(input => input.IsEffectivelyVisible))
            {
                input.BringIntoView(); AuthoringUiFixture.Drain();
                ResponsiveShellTests.Inside(input, dialog);
            }
            if (stage < 5) AuthoringUiFixture.Click(decision);
        }
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    [AvaloniaFact]
    public void Workspace_creation_and_preferences_keep_their_decisions_visible_at_large_text()
    {
        using var fixture = Loaded(); fixture.Window!.FontSize = 20;
        var creation = new WorkspaceCreationWindow(fixture.Window) { Width = 400, Height = 460 };
        creation.Show(fixture.Window); AuthoringUiFixture.Drain();
        Assert.Equal(20, fixture.Control<TextBox>("Workspace destination", creation).FontSize);
        foreach (var input in creation.GetVisualDescendants().OfType<TextBox>().Where(input => input.IsEffectivelyVisible))
        {
            input.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(input, creation);
        }
        ResponsiveShellTests.Inside(fixture.Control<Button>("Create workspace", creation), creation);
        ResponsiveActionLabelTests.LabelFits(fixture.Control<Button>("Create workspace", creation), creation);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel workspace creation", creation));
        AuthoringUiFixture.Click(fixture.Control<Button>("Open settings"));
        var settings = Assert.IsType<SettingsWindow>(Assert.Single(fixture.Window.OwnedWindows));
        settings.Width = 400; settings.Height = 360; AuthoringUiFixture.Drain();
        Assert.Equal(20, fixture.Control<TextBox>("OpenTAP home override", settings).FontSize);
        fixture.Control<TextBox>("Last workspace", settings).BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(fixture.Control<TextBox>("Last workspace", settings), settings);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Close settings", settings), settings);
        AuthoringUiFixture.Click(fixture.Control<Button>("Close settings", settings));
        Assert.Empty(fixture.Window.OwnedWindows);
    }

    [AvaloniaTheory]
    [InlineData(1, "Review binding change")]
    [InlineData(1, "Add instrument slot")]
    [InlineData(2, "Saved-plan empty state")]
    [InlineData(3, "Prepare selected authoring environment")]
    [InlineData(3, "Declare Instrument Components dependency")]
    [InlineData(4, "Pack workspace")]
    [InlineData(5, "Import recording")]
    [InlineData(6, "Include definition in selected program")]
    public void Workspace_tasks_remain_reachable_with_large_text_and_wrapped_content(int route, string action)
    {
        using var fixture = Loaded(); var owner = fixture.Window!;
        owner.FontSize = 20; owner.SetRenderScaling(1.5);
        fixture.ViewModel.DisplayName = "Selected program with a deliberately long descriptive title for task scope and wrapping";
        owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = route;
        AuthoringUiFixture.Drain();
        Control control = action == "Saved-plan empty state" ? fixture.Control<TextBlock>(action) : fixture.Control<Button>(action); control.BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(control, owner);
        if (control is Button button) ResponsiveActionLabelTests.LabelFits(button, owner);
        Assert.True(control.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Library_slot_suggestion_changes_only_untouched_input_and_retention_preserves_the_name(bool deliberate)
    {
        using var fixture = Loaded();
        InstallLibrary(fixture);
        var dialog = new PlanInitializationWindow(fixture.ViewModel); dialog.Show(fixture.Window!); AuthoringUiFixture.Drain();
        var slot = fixture.Control<TextBox>("Instrument slot", dialog);
        if (deliberate) slot.Text = "Retained bench name";
        var hardware = fixture.Control<ComboBox>("Hardware choice", dialog);
        hardware.SelectedItem = hardware.Items.Single(item => item!.ToString()!.StartsWith("Create DC Power Supply", StringComparison.Ordinal));
        AuthoringUiFixture.Drain();
        Assert.Equal(deliberate ? "Retained bench name" : "INSTR1", slot.Text);
        // An intentional DMM is not mistaken for the automatic legacy suggestion.
        slot.Text = "DMM"; AuthoringUiFixture.Drain();
        var state = dialog.CaptureGuidedForm();
        dialog.Close(false);
        dialog = new PlanInitializationWindow(fixture.ViewModel, retained: state); dialog.Show(fixture.Window!); AuthoringUiFixture.Drain();
        Assert.Equal("DMM", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        hardware = fixture.Control<ComboBox>("Hardware choice", dialog);
        hardware.SelectedItem = hardware.Items.Single(item => item!.ToString()!.StartsWith("Create Oscilloscope", StringComparison.Ordinal));
        AuthoringUiFixture.Drain();
        Assert.Equal("DMM", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        dialog.Close(false);
    }

    [AvaloniaFact]
    public void Untouched_reuse_preserves_the_resource_name_and_retained_identity()
    {
        using var fixture = Loaded();
        fixture.ViewModel.CreateDemoProgram("reuse-scope", "BenchScope");
        var dialog = new PlanInitializationWindow(fixture.ViewModel); dialog.Show(fixture.Window!); AuthoringUiFixture.Drain();
        var hardware = fixture.Control<ComboBox>("Hardware choice", dialog);
        hardware.SelectedItem = hardware.Items.First(item => item!.ToString()!.StartsWith("Reuse BenchScope", StringComparison.Ordinal));
        AuthoringUiFixture.Drain();
        Assert.Equal("BenchScope", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        var retained = dialog.CaptureGuidedForm(); dialog.Close(false);
        dialog = new PlanInitializationWindow(fixture.ViewModel, retained: retained); dialog.Show(fixture.Window!); AuthoringUiFixture.Drain();
        Assert.Equal("BenchScope", fixture.Control<TextBox>("Instrument slot", dialog).Text);
        var restored = dialog.CaptureGuidedForm().ReusedInstrument!;
        Assert.Equal(retained.ReusedInstrument!.TypeId, restored.TypeId);
        Assert.Equal(retained.ReusedInstrument.VisaAddress, restored.VisaAddress);
        Assert.Equal(retained.ReusedInstrument.Settings, restored.Settings);
        dialog.Close(false);
    }

    private static AuthoringUiFixture Loaded()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(960, 600); fixture.OpenRememberedWorkspace(); return fixture;
    }

    private static void InstallLibrary(AuthoringUiFixture fixture)
    {
        var package = PublishedLibraryFixture.PackageRoot;
        var home = Path.Combine(fixture.WorkspaceRoot, "task-layout-library-home");
        var payload = Path.Combine(home, "Packages", AuthoringInstrumentCatalog.LibraryPackage); Directory.CreateDirectory(payload);
        foreach (var file in new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll" })
            File.Copy(Path.Combine(package!, file), Path.Combine(home, file));
        File.Copy(Path.Combine(package!, "package.xml"), Path.Combine(payload, "package.xml"));
        fixture.ViewModel.OpenTapHomeOverride = home;
    }
}
