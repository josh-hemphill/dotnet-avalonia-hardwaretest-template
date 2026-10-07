using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringGuidedOnboardingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_mock_voltage_task_uses_shared_document_preview_save_reopen_and_completion(bool demo)
    {
        using var fixture = new AuthoringUiFixture(); fixture.Show();
        var root = Path.Combine(Path.GetDirectoryName(fixture.WorkspaceRoot)!, "first-success");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace from welcome"));
        var workspace = Assert.IsType<WorkspaceCreationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Set(fixture, workspace, "Workspace destination", root); Set(fixture, workspace, "Workspace display name", "Board"); Set(fixture, workspace, "Package name", "Board tests");
        fixture.Control<ComboBox>("Workspace template", workspace).SelectedIndex = demo ? 2 : 1;
        AuthoringUiFixture.Click(fixture.Control<Button>("Review workspace creation", workspace));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create workspace", workspace));
        var adapter = AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM");
        PreparePackage(fixture, demo ? AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM") : adapter);
        var dialog = Start(fixture);
        Set(fixture, dialog, "Stable plan ID", "first-voltage"); Set(fixture, dialog, "Plan display name", "First voltage"); Set(fixture, dialog, "Device family", "board-v1");
        if (demo) fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 2;
        Next(fixture, dialog); Assert.Contains("Instrument", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        if (!demo)
        {
            fixture.Control<ComboBox>("Hardware choice", dialog).SelectedItem = fixture.Control<ComboBox>("Hardware choice", dialog).Items.Single(item => item!.ToString()!.Contains("Create Mock DMM", StringComparison.Ordinal));
            Set(fixture, dialog, "Instrument address", "MOCK::BENCH");
        }
        Next(fixture, dialog); Set(fixture, dialog, "Sample count", "12"); Set(fixture, dialog, "Output channel", "rail.voltage");
        Next(fixture, dialog); Set(fixture, dialog, "Pass threshold", "1.25");
        Next(fixture, dialog);
        var review = fixture.Control<TextBlock>("Initialization review", dialog).Text!;
        Assert.Contains("identity", review, StringComparison.OrdinalIgnoreCase); Assert.Contains("DMM", review); Assert.Contains("1.25", review);
        Next(fixture, dialog); AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(fixture.Window.OwnedWindows);
        var draft = fixture.ViewModel.SelectedProgram!;
        Assert.Equal("first-voltage", draft.PlanId); Assert.Single(draft.Setup.OfType<IdentitySetup>());
        Assert.Equal(["DMM"], draft.Cleanup.InstrumentSlots); Assert.True(draft.Cleanup.IncludeSafeShutdown);
        var metric = Assert.IsType<MetricNode>(Assert.Single(draft.Measure)); Assert.Equal(1.25, metric.Metric.Limits!.Threshold);
        var direct = fixture.ViewModel.ReviewPlanInitialization(new PlanInitializationRequest("equivalent")
        {
            DisplayName = "First voltage",
            DeviceFamily = "board-v1",
            StartingPoint = demo ? PlanStartingPoint.DemoVoltageTask : PlanStartingPoint.VoltageTask,
            Instruments = draft.Instruments,
            UseTemplateHardware = false,
            IncludeTemplateMeasurement = false,
            IdentityInstrumentSlot = "DMM",
            Measurement = new(AuthoringRecipeIds.MeanGte, "DMM") { ChannelKey = "rail.voltage", SampleCount = "12", ThresholdText = "1.25" }
        }).Draft;
        // Independently created documents have fresh IDs; align only those IDs and the plan name.
        direct = direct with
        {
            PlanId = draft.PlanId,
            Cleanup = direct.Cleanup with { NodeId = draft.Cleanup.NodeId },
            Setup = [Assert.IsType<IdentitySetup>(Assert.Single(direct.Setup)) with { NodeId = Assert.Single(draft.Setup).NodeId }],
            Measure = [Assert.IsType<MetricNode>(Assert.Single(direct.Measure)) with { NodeId = metric.NodeId }]
        };
        Assert.Equal(AuthoringDocumentSnapshot.Capture(draft).PlanIdentity, AuthoringDocumentSnapshot.Capture(direct).PlanIdentity);
        Assert.Equal(AuthoringDocumentSnapshot.Capture(draft).SidecarIdentity, AuthoringDocumentSnapshot.Capture(direct).SidecarIdentity);
        AuthoringUiFixture.Click(fixture.Control<Button>("Preview voltage result"));
        Assert.Equal(5, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Save and check draft"));
        Assert.True(fixture.Control<TextBlock>("Guidance feedback").Text!.Contains("First voltage test complete", StringComparison.Ordinal), fixture.Control<TextBlock>("Guidance feedback").Text);
        var source = new AuthoringDocumentStore(root).Load("first-voltage").Document!; Assert.False(source.RequiresCompilation);
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave saved guidance"));
        fixture.ViewModel.Open(root); fixture.ViewModel.SelectProgram("first-voltage"); AuthoringUiFixture.Drain();
        Assert.Equal(metric.NodeId, Assert.IsType<MetricNode>(Assert.Single(fixture.ViewModel.SelectedProgram!.Measure)).NodeId);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.True(fixture.Control<TextBlock>("Guidance feedback").Text!.Contains("First voltage test complete", StringComparison.Ordinal), fixture.Control<TextBlock>("Guidance feedback").Text);
        Assert.Contains("Demo — Mock", fixture.Control<TextBlock>("Guidance feedback").Text);
    }

    [AvaloniaFact]
    public void Leaving_and_resuming_retains_raw_inputs_stage_and_durable_expert_skip_without_source_publication()
    {
        using var fixture = Loaded(); var dialog = Start(fixture);
        Set(fixture, dialog, "Stable plan ID", "retained"); Set(fixture, dialog, "Plan display name", "Retained input");
        Next(fixture, dialog); fixture.Control<ComboBox>("Hardware choice", dialog).SelectedItem = fixture.Control<ComboBox>("Hardware choice", dialog).Items.Single(item => item!.ToString()!.Contains("Create Mock DMM", StringComparison.Ordinal));
        Set(fixture, dialog, "Instrument address", "MOCK::bench"); Next(fixture, dialog);
        Set(fixture, dialog, "Sample count", "pending"); Next(fixture, dialog); Set(fixture, dialog, "Pass threshold", "not yet");
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", dialog));
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("retained").Exists);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Assert.Contains("Pass criterion", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        Assert.Equal("pending", fixture.Control<TextBox>("Sample count", dialog).Text); Assert.Equal("not yet", fixture.Control<TextBox>("Pass threshold", dialog).Text);
        Assert.Equal("MOCK::bench", fixture.Control<TextBox>("Instrument address", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Skip optional guidance", dialog));
        var prefs = new AuthoringPreferencesStore(fixture.Preferences.FilePath); prefs.Load(); Assert.True(prefs.Current.SkipGuidance);
        dialog = Start(fixture); Assert.Contains("Name and destination", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        Assert.DoesNotContain("guided", dialog.Title!, StringComparison.OrdinalIgnoreCase);
        AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
    }

    [AvaloniaFact]
    public void Missing_hardware_and_package_have_next_actions_and_keep_draft_history_in_normal_editor()
    {
        using var fixture = Loaded(); var dialog = Start(fixture); Set(fixture, dialog, "Stable plan ID", "blocked");
        Next(fixture, dialog); fixture.Control<ComboBox>("Hardware choice", dialog).SelectedItem = fixture.Control<ComboBox>("Hardware choice", dialog).Items.Single(item => item!.ToString()!.Contains("Create Mock DMM", StringComparison.Ordinal));
        Set(fixture, dialog, "Instrument address", "MOCK::bench");
        for (var stage = 0; stage < 4; stage++) Next(fixture, dialog);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var feedback = fixture.Control<TextBlock>("Guidance feedback").Text!;
        Assert.Contains("missing", feedback, StringComparison.OrdinalIgnoreCase); Assert.Contains("Environment", feedback); Assert.Contains("Issues", feedback); Assert.Contains("Build", feedback);
        Assert.DoesNotContain("First voltage test complete", feedback);
        var session = fixture.ViewModel.SelectedDocument; fixture.ViewModel.DisplayName = "Edited after leaving";
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave saved guidance"));
        Assert.Same(session, fixture.ViewModel.SelectedDocument); Assert.True(fixture.ViewModel.CanUndo); fixture.ViewModel.Undo();
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance")); AuthoringUiFixture.Click(fixture.Control<Button>("Fix guidance blockers"));
        Assert.Equal(2, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare guidance packages")); Assert.Equal(3, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        AuthoringUiFixture.Click(fixture.Control<Button>("Review guidance build")); Assert.Equal(4, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
    }

    [AvaloniaFact]
    public void Empty_route_uses_shared_empty_document_and_stale_session_cannot_publish_retained_inputs()
    {
        using var fixture = Loaded(); var dialog = Start(fixture);
        Set(fixture, dialog, "Stable plan ID", "empty-guided");
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 0;
        Assert.Contains("Save and check", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(fixture.ViewModel.SelectedProgram!.Instruments); Assert.Empty(fixture.ViewModel.SelectedProgram.Measure); Assert.Empty(fixture.ViewModel.SelectedProgram.Setup);
        var id = fixture.ViewModel.SelectedProgram.PlanId;
        dialog = Start(fixture); Set(fixture, dialog, "Stable plan ID", "obsolete");
        for (var stage = 0; stage < 5; stage++) Next(fixture, dialog);
        fixture.ViewModel.Open(fixture.WorkspaceRoot); AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Contains("workspace changed", fixture.Control<TextBlock>("Initialization error", dialog).Text, StringComparison.OrdinalIgnoreCase);
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("obsolete").Exists);
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", dialog));
        fixture.ViewModel.SelectProgram(id); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.Empty(fixture.Window!.OwnedWindows);
    }

    [AvaloniaFact]
    public void Saved_guidance_preserves_editor_viewport_at_large_text_and_stale_artifacts_do_not_claim_completion()
    {
        using var fixture = Loaded(); var window = fixture.Window!;
        window.Width = 960; window.Height = 600; window.FontSize = 20; window.SetRenderScaling(1.5);
        PreparePackage(fixture, AuthoringInstrumentCatalog.All.Single(item => item.DisplayName == "Mock DMM"));
        fixture.ViewModel.InitializePlan(new PlanInitializationRequest("viewport-demo") { StartingPoint = PlanStartingPoint.DemoVoltageTask, IdentityInstrumentSlot = "DMM" });
        AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Save and check draft"));
        Assert.Contains("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
        Assert.True(window.FindControl<Border>("GuidanceHost")!.Bounds.Height <= 160);
        Assert.True(window.FindControl<TabControl>("WorkspaceTabs")!.Bounds.Height > 150);
        var sequence = fixture.Control<ListBox>("Program sequence");
        var inspector = fixture.Control<ScrollViewer>("Selected step inspector");
        ResponsiveShellTests.Inside(sequence, window); ResponsiveShellTests.Inside(inspector, window);
        Assert.True(sequence.Bounds.Height >= 40); Assert.True(inspector.Bounds.Height >= 80);
        var rail = fixture.Control<ScrollViewer>("Guidance rail viewport");
        foreach (var name in new[] { "Start guided voltage test", "Resume guidance", "Preview voltage result", "Fix guidance blockers", "Prepare guidance packages", "Review guidance build",
            "Validate saved plans", "Save and check draft", "Leave saved guidance", "Skip saved guidance" })
        {
            var button = fixture.Control<Button>(name); button.BringIntoView(); AuthoringUiFixture.Drain();
            Assert.True(button.Focus()); ResponsiveShellTests.Inside(button, window);
            ResponsiveShellTests.Inside(Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()), window);
        }
        Assert.True(rail.Offset.Y > 0);
        var programs = fixture.Control<ListBox>("Programs"); programs.BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(programs, window); Assert.True(programs.Bounds.Height >= 40);
        programs.SelectedItem = fixture.ViewModel.ProgramRows.Single(row => row.PlanId == "sample"); AuthoringUiFixture.Drain();
        Assert.Equal("sample", fixture.ViewModel.SelectedProgram!.PlanId);
        programs.SelectedItem = fixture.ViewModel.ProgramRows.Single(row => row.PlanId == "viewport-demo"); AuthoringUiFixture.Drain();
        Assert.Equal("viewport-demo", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.True(window.FindControl<Border>("GuidanceHost")!.IsVisible);
        var recording = new TestRunRecord
        {
            PlanId = "viewport-demo",
            DutSerial = string.Join(" ", Enumerable.Repeat("Long translated device identification and recorded provenance", 50)),
            Samples = [new StoredSample { MetricKey = "VDC", Value = 1, ElapsedMs = 0 }, new StoredSample { MetricKey = "VDC", Value = 2, ElapsedMs = 5 }]
        };
        var recordingPath = Path.Combine(fixture.WorkspaceRoot, "guided-long-provenance.json");
        File.WriteAllText(recordingPath, JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord));
        fixture.ViewModel.ImportRecording(recordingPath, "guided-long-provenance");
        var previewAction = fixture.Control<Button>("Preview voltage result"); previewAction.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(previewAction);
        var board = fixture.Control<OperatorPreviewPane>("Operator preview chrome");
        ResponsiveShellTests.Inside(board, window); Assert.True(board.Bounds.Height >= 80);
        var sourceViewport = fixture.Control<ScrollViewer>("Data source details viewport"); Assert.True(sourceViewport.Extent.Height > sourceViewport.Viewport.Height);
        var recordings = fixture.Control<ListBox>("Recordings"); recordings.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(recordings, window);
        var example = fixture.Control<Button>("Use example data"); example.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(example, window);
        AuthoringUiFixture.Click(example); ResponsiveShellTests.Inside(board, window);
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0; AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(sequence, window); ResponsiveShellTests.Inside(inspector, window);
        var direct = fixture.Control<Button>("New test plan"); direct.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(direct.Focus()); ResponsiveShellTests.Inside(direct, window);
        AuthoringUiFixture.Click(direct); var dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(window.OwnedWindows));
        Assert.Equal("New test plan", dialog.Title); AuthoringUiFixture.Click(fixture.Control<Button>("Cancel", dialog));
        var compiled = fixture.ViewModel.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == "viewport-demo");
        File.AppendAllText(compiled, "\n<!-- external change -->\n");
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        Assert.DoesNotContain("First voltage test complete", fixture.Control<TextBlock>("Guidance feedback").Text);
    }

    [AvaloniaTheory]
    [InlineData("1000")]
    [InlineData("pending")]
    public void Interval_applies_only_to_acquisition_and_survives_leave_resume_save_and_reopen(string entered)
    {
        using var fixture = Loaded(); var owner = fixture.Window!;
        owner.Width = 960; owner.Height = 600; owner.FontSize = 20; owner.SetRenderScaling(1.5);
        AuthoringUiFixture.Drain(); var dialog = Start(fixture); Compact(dialog);
        Set(fixture, dialog, "Stable plan ID", "retained-interval");
        fixture.Control<ComboBox>("Starting point", dialog).SelectedIndex = 2;
        Next(fixture, dialog); Next(fixture, dialog);
        var interval = fixture.Control<TextBox>("Sampling interval ms", dialog);
        Assert.False(interval.IsEnabled);
        var applicability = fixture.Control<TextBlock>("Sampling interval applicability", dialog);
        Assert.Contains("does not use", applicability.Text);
        interval.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(interval, dialog);
        applicability.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(applicability, dialog);
        Next(fixture, dialog); fixture.Control<CheckBox>("Mean greater than or equal criterion", dialog).IsChecked = false;
        AuthoringUiFixture.Click(fixture.Control<Button>("Back", dialog));
        Assert.True(interval.IsEnabled); interval.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(interval.Focus()); ResponsiveShellTests.Inside(interval, dialog);
        Set(fixture, dialog, "Sampling interval ms", entered);
        Assert.Equal("retained-interval", fixture.Control<TextBox>("Stable plan ID", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Leave guidance", dialog));
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("retained-interval").Exists);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        dialog = Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows)); Compact(dialog);
        interval = fixture.Control<TextBox>("Sampling interval ms", dialog);
        Assert.True(interval.IsEnabled); Assert.Equal(entered, interval.Text);
        Assert.Equal("retained-interval", fixture.Control<TextBox>("Stable plan ID", dialog).Text);
        Next(fixture, dialog); fixture.Control<CheckBox>("Mean greater than or equal criterion", dialog).IsChecked = true;
        Next(fixture, dialog);
        Assert.Contains("Pass criterion", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        Assert.Contains("interval", fixture.Control<TextBlock>("Initialization error", dialog).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Turn off", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.Equal(entered, interval.Text); Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("retained-interval").Exists);
        fixture.Control<CheckBox>("Mean greater than or equal criterion", dialog).IsChecked = false;
        Next(fixture, dialog); Next(fixture, dialog); AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var metric = Assert.IsType<MetricNode>(Assert.Single(fixture.ViewModel.SelectedProgram!.Measure));
        AssertInterval(fixture.ViewModel.SelectedProgram);
        Assert.Empty(fixture.Window!.OwnedWindows); Assert.Equal("retained-interval", fixture.ViewModel.SelectedProgram.PlanId);
        var loaded = new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("retained-interval");
        Assert.True(loaded.IsSuccess, loaded.Error); var saved = Assert.IsType<AuthoringDocumentDto>(loaded.Document);
        AssertInterval(saved.ToDraft());
        fixture.ViewModel.Open(fixture.WorkspaceRoot); fixture.ViewModel.SelectProgram("retained-interval"); AuthoringUiFixture.Drain();
        var reopened = Assert.IsType<MetricNode>(Assert.Single(fixture.ViewModel.SelectedProgram!.Measure));
        Assert.Equal(metric.NodeId, reopened.NodeId); AssertInterval(fixture.ViewModel.SelectedProgram);
        if (entered == "pending") Assert.Contains(fixture.ViewModel.EditingIssues, issue => issue.Message.Contains("IntervalMs", StringComparison.Ordinal));

        static void Compact(Window window)
        {
            window.Width = 480; window.Height = 460; window.FontSize = 20; window.SetRenderScaling(1.5); AuthoringUiFixture.Drain();
        }

        void AssertInterval(ProgramDraft draft)
        {
            var node = Assert.IsType<MetricNode>(Assert.Single(draft.Measure));
            var source = Assert.IsType<MeasureSource>(node.Metric.Source);
            if (entered == "pending") Assert.Equal(entered, draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "MetricSetting:IntervalMs")]);
            else Assert.Equal(entered, source.Settings["IntervalMs"]);
        }
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
    private static AuthoringUiFixture Loaded() { var fixture = new AuthoringUiFixture(rememberWorkspace: true); fixture.Show(); fixture.OpenRememberedWorkspace(); return fixture; }
    private static PlanInitializationWindow Start(AuthoringUiFixture fixture) { AuthoringUiFixture.Click(fixture.Control<Button>("Start guided voltage test")); return Assert.IsType<PlanInitializationWindow>(Assert.Single(fixture.Window!.OwnedWindows)); }
    private static void Next(AuthoringUiFixture fixture, Window dialog) => AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
    private static void Set(AuthoringUiFixture fixture, Window dialog, string name, string value) { fixture.Control<TextBox>(name, dialog).Text = value; AuthoringUiFixture.Drain(); }
}
