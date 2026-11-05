using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringReferenceWorkflowTests
{
    [AvaloniaFact]
    public async Task External_tui_command_refuses_a_reference_program_and_directs_execution_to_the_operator()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        vm.RequireGoldenReference = true;
        vm.ReferenceTolerance = "0.5";
        var before = vm.SelectedDocument!.Revision;
        Assert.False(await window.ExecuteCommandAsync("tui"));
        Assert.Contains("operator app", vm.Error);
        Assert.Contains("Prepare bench files", vm.Error);
        Assert.Equal(before, vm.SelectedDocument.Revision);
        Assert.False(vm.OperationBusy);
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Reference_criterion_is_keyboard_editable_with_explicit_units_equation_and_error_recovery(bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        using var theme = new RequestedThemeScope(dark);
        window.FontSize = font;
        Assert.Equal((double)width, window.ClientSize.Width);
        Assert.True(window.Bounds.Width <= width);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        fixture.Control<ListBox>("Program sequence").SelectedItem = vm.SequenceItems.First(row => row.Kind == SequenceRowKind.Metric);
        AuthoringUiFixture.Drain();
        var selectedRow = fixture.Control<ListBox>("Program sequence").GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is SequenceRow row && row.NodeId == vm.SelectedSequence!.NodeId);
        Assert.Equal(vm.SelectedSequence!.Label, AutomationProperties.GetName(selectedRow));
        var header = fixture.Control<Expander>("Configure selected step").GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
            .Single(button => button.Name == "ExpanderHeader");
        Assert.Equal("Configure selected step", AutomationProperties.GetName(header));
        var require = fixture.Control<CheckBox>("Require golden reference");
        require.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(require.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        AuthoringUiFixture.Drain();
        Assert.True(vm.RequireGoldenReference);
        Type("Reference input key", string.Empty);
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Drain();
        Assert.Contains("Name this reference", vm.ReferenceKeyError);
        var keyError = fixture.Control<TextBlock>("Reference input key error");
        Assert.NotEmpty(keyError.Text!);
        InsideScrollViewports(fixture.Control<TextBox>("Reference input key"));
        InsideScrollViewports(keyError);
        Assert.Equal(vm.ReferenceKeyError, AutomationProperties.GetHelpText(fixture.Control<TextBox>("Reference input key")));
        Capture(window, "authoring-reference-key-error", dark, width, height, font);
        Type("Reference input key", "golden.reference.voltage");
        Assert.Empty(vm.ReferenceKeyError);
        Type("Reference tolerance", "1e-");
        Assert.Equal("1e-", vm.ReferenceTolerance);
        Assert.Contains("finite tolerance", vm.ReferenceCriterionError);
        Assert.Contains(AuthoringIssueService.GetIssues(vm.SelectedProgram!), finding => finding.Field is { } field && AuthoringReferenceRules.BaseField(field) == "ReferenceTolerance");
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Drain();
        var error = fixture.Control<TextBlock>("Reference criterion error");
        Assert.NotEmpty(error.Text!);
        InsideScrollViewports(fixture.Control<TextBox>("Reference tolerance"));
        InsideScrollViewports(error);
        Capture(window, "authoring-reference-tolerance-error", dark, width, height, font);
        Type("Reference tolerance", "0.25");
        Assert.Equal(0.25, Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!).Tolerance);
        Assert.Contains("including repeat", vm.ReferenceReduction);
        Assert.Contains("≤", vm.ReferenceEquation);
        Assert.Contains(vm.YUnit, vm.ReferenceTarget);
        Assert.Empty(vm.ReferenceCriterionError);
        var comparison = fixture.Control<ComboBox>("Reference comparison");
        comparison.BringIntoView(); AuthoringUiFixture.Drain();
        comparison.SelectedItem = "Relative difference (%)";
        AuthoringUiFixture.Drain();
        Assert.Equal("percent", Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!).Comparison);
        Assert.Contains("zero reference", vm.ReferenceEquation);
        Capture(window, "authoring-reference-criterion", dark, width, height, font);

        void Type(string name, string value)
        {
            var box = fixture.Control<TextBox>(name);
            box.BringIntoView(); AuthoringUiFixture.Drain();
            Assert.True(box.Focus()); box.SelectAll();
            if (value.Length == 0)
            {
                window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
                window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            }
            else window.KeyTextInput(value);
            AuthoringUiFixture.Drain();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 900, 600, 20)]
    public async Task Multiple_imported_reference_criteria_show_all_equations_and_explicit_remove_all_with_undo(bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var compiler = new PlanCompiler();
        var path = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var draft = compiler.Load(path);
        var metric = draft.Measure.OfType<MetricNode>().First();
        var first = new BenchReferenceRule
        {
            RuleId = "first-imported-criterion",
            ProducerStepId = metric.NodeId,
            MetricKey = metric.Metric.ChannelKey,
            Unit = metric.Metric.YUnit,
            ReferenceKey = "golden.primary",
            Comparison = "absolute",
            Tolerance = 0.25,
        };
        var second = first with { RuleId = "second-imported-criterion", ReferenceKey = "golden.secondary", Comparison = "percent", Tolerance = 2 };
        draft.Sidecar.ReferenceRules = [first, second];
        compiler.Save(draft, path);
        var window = fixture.Show(width, height);
        using var theme = new RequestedThemeScope(dark);
        window.FontSize = font;
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Assert.Equal((double)width, window.ClientSize.Width);
        Assert.True(window.Bounds.Width <= width);
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == metric.NodeId));
        AuthoringUiFixture.Drain();
        var overview = fixture.Control<TextBlock>("All reference criteria");
        overview.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.Contains("All 2 criteria must pass", overview.Text);
        Assert.Contains("Select a criterion", overview.Text);
        ResponsiveShellTests.Inside(overview, window);
        var require = fixture.Control<CheckBox>("Require golden reference");
        Assert.Contains("uncheck removes all", Assert.IsType<TextBlock>(require.Content).Text);
        Capture(window, "authoring-multiple-reference-criteria", dark, width, height, font);
        var selector = fixture.Control<ComboBox>("Reference criterion to edit");
        var initialOptions = selector.ItemsSource;
        Assert.Contains(selector.Items.OfType<AuthoringReferenceCriterionOption>(), option => option.Label.Contains("golden.primary", StringComparison.Ordinal));
        Assert.Contains(selector.Items.OfType<AuthoringReferenceCriterionOption>(), option => option.Label.Contains("golden.secondary", StringComparison.Ordinal)
            && option.Label.Contains(BenchReferenceEvidence.DescribeRule(second), StringComparison.Ordinal));
        selector.SelectedItem = selector.Items.OfType<AuthoringReferenceCriterionOption>().Single(option => option.RuleId == second.RuleId);
        AuthoringUiFixture.Drain();
        Assert.Equal(second.RuleId, vm.SelectedReferenceCriterion!.RuleId);
        Assert.Same(initialOptions, selector.ItemsSource);
        Assert.Equal("Relative difference (%)", vm.ReferenceComparison);
        TypeTolerance("2e-");
        selector.SelectedItem = selector.Items.OfType<AuthoringReferenceCriterionOption>().Single(option => option.RuleId == first.RuleId);
        AuthoringUiFixture.Drain();
        Assert.Equal("0.25", fixture.Control<TextBox>("Reference tolerance").Text);
        Assert.Empty(vm.ReferenceToleranceError);
        await fixture.WaitForReadinessAsync();
        var incomplete = vm.EditingIssues.Single(issue => issue.Code == "INCOMPLETE_NUMERIC_INPUT" && issue.Field == AuthoringReferenceRules.ToleranceField(second));
        var target = vm.NavigateEditingIssue(incomplete);
        Assert.NotNull(target);
        Assert.True(window.GetVisualDescendants().OfType<SelectedStepInspectorView>().Single().FocusFinding(target));
        AuthoringUiFixture.Drain();
        Assert.Equal(second.RuleId, vm.SelectedReferenceCriterion!.RuleId);
        Assert.Equal("2e-", fixture.Control<TextBox>("Reference tolerance").Text);
        TypeTolerance("3");
        var previousRules = vm.SelectedProgram!.Sidecar.ReferenceRules!.ToArray();
        var add = fixture.Control<Button>("Add reference criterion");
        add.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(add.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        AuthoringUiFixture.Drain();
        var third = vm.SelectedReferenceCriterion!.RuleId;
        Assert.DoesNotContain(previousRules, rule => rule.RuleId == third);
        Assert.Equal(3, vm.SelectedProgram!.Sidecar.ReferenceRules!.Count);
        Assert.Null(Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules, rule => rule.RuleId == third).Tolerance);
        var key = fixture.Control<TextBox>("Reference input key");
        key.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(key.Focus());
        key.SelectAll(); window.KeyTextInput("golden.third"); AuthoringUiFixture.Drain();
        TypeTolerance("0.5");
        Assert.Equal(previousRules, vm.SelectedProgram.Sidecar.ReferenceRules.Where(rule => rule.RuleId != third));
        Assert.Equal("golden.third", Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules, rule => rule.RuleId == third).ReferenceKey);
        Assert.Equal(0.5, Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules, rule => rule.RuleId == third).Tolerance);
        SpaceButton("Remove selected reference criterion");
        Assert.Equal(2, vm.SelectedProgram.Sidecar.ReferenceRules!.Count);
        Assert.DoesNotContain(vm.SelectedProgram.Sidecar.ReferenceRules!, rule => rule.RuleId == third);
        var beforeRemoveAll = vm.SelectedProgram.Sidecar.ReferenceRules.ToArray();
        require.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(require.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        AuthoringUiFixture.Drain();
        Assert.Null(vm.SelectedProgram!.Sidecar.ReferenceRules);
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(beforeRemoveAll, vm.SelectedProgram.Sidecar.ReferenceRules);
        Assert.True(require.IsChecked);

        void TypeTolerance(string value)
        {
            var box = fixture.Control<TextBox>("Reference tolerance");
            box.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(box.Focus());
            box.SelectAll(); window.KeyTextInput(value); AuthoringUiFixture.Drain();
        }
        void SpaceButton(string name)
        {
            var button = fixture.Control<Button>(name);
            button.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(button.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            AuthoringUiFixture.Drain();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 900, 600, 20)]
    public async Task Imported_reference_recording_exposes_exact_readonly_provenance_and_proposed_replay(bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        using var theme = new RequestedThemeScope(dark);
        window.FontSize = font;
        Assert.Equal((double)width, window.ClientSize.Width);
        Assert.True(window.Bounds.Width <= width);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        vm.RequireGoldenReference = true;
        vm.ReferenceTolerance = "0.25";
        var rule = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
        var run = new TestRunRecord
        {
            RunId = "recorded-golden-ui-" + Guid.NewGuid().ToString("N"),
            PlanId = vm.SelectedProgram.PlanId,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            ExecutionScope = RunExecutionScopes.FullSuite,
            Samples = [new() { MetricKey = rule.MetricKey, ProducerStepId = rule.ProducerStepId, Value = 1, Unit = rule.Unit, Timestamp = DateTimeOffset.UtcNow }],
            ReferenceUses = [new()
            {
                Rule = rule, ReferenceId = "golden-reference-ui-stable-id", ReferenceName = "Golden acceptance board", ReferenceRevision = 1,
                ReferenceSha256 = "immutable-reference-sha256", SourceRunId = "source-golden-run", SourceDutSerial = "GOLDEN-ACCEPTANCE-001",
                SourceOperator = "Reference technician", SourceScope = RunExecutionScopes.ReferenceAcquisition,
                SourceStartedAt = DateTimeOffset.UtcNow.AddHours(-1), SourceCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-50),
                Context = new() { PlanId = vm.SelectedProgram.PlanId, PlanSha256 = "plan-sha", SidecarSha256 = "sidecar-sha", ConfigurationSha256 = "config-sha", VisaMode = "simulated" },
                ReferenceValue = new() { MetricKey = rule.MetricKey, ProducerStepId = rule.ProducerStepId, ReferenceKey = rule.ReferenceKey, Unit = rule.Unit, Value = 1, SampleCount = 12 }
            }]
        };
        var path = Path.Combine(fixture.WorkspaceRoot, "incoming-reference-run.json");
        File.WriteAllText(path, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        var before = File.ReadAllBytes(path);
        vm.ImportRecording(path, "golden-feedback");
        fixture.NavigateTask(5);
        await fixture.WaitForReadinessAsync();
        Assert.True(vm.HasRecordedReferences);
        var trigger = fixture.Control<Button>("Review recorded references");
        AuthoringUiFixture.Click(trigger); AuthoringUiFixture.Drain();
        var dialog = Assert.Single(window.OwnedWindows);
        var text = fixture.Control<TextBox>("Full recorded reference review", dialog);
        Assert.True(text.IsReadOnly);
        Assert.Contains("golden-reference-ui-stable-id", text.Text);
        Assert.Contains("immutable-reference-sha256", text.Text);
        Assert.Contains("GOLDEN-ACCEPTANCE-001", text.Text);
        Assert.Contains("Proposed preview: Pass", text.Text);
        Assert.Contains("preview only", text.Text);
        Assert.True(text.IsFocused);
        Capture(dialog, "authoring-reference-recording-review", dark, width, height, font);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.True(trigger.IsFocused);
        fixture.NavigateTask(0);
        var tolerance = fixture.Control<TextBox>("Reference tolerance");
        tolerance.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(tolerance.Focus()); tolerance.SelectAll(); window.KeyTextInput("1e-");
        AuthoringUiFixture.Drain();
        Assert.Equal("1e-", vm.ReferenceTolerance);
        fixture.NavigateTask(5);
        await fixture.WaitForReadinessAsync();
        trigger = fixture.Control<Button>("Review recorded references");
        AuthoringUiFixture.Click(trigger); AuthoringUiFixture.Drain();
        dialog = Assert.Single(window.OwnedWindows);
        text = fixture.Control<TextBox>("Full recorded reference review", dialog);
        Assert.Contains("Proposed preview unavailable: finish the current reference tolerance", text.Text);
        Assert.DoesNotContain("Proposed preview: Pass", text.Text);
        Assert.Contains("immutable-reference-sha256", text.Text);
        Capture(dialog, "authoring-reference-recording-invalid-tolerance", dark, width, height, font);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static void Capture(Window window, string name, bool dark, int width, int height, int font)
    {
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1") return;
        var root = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(root, $"{name}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class RequestedThemeScope : IDisposable
    {
        private readonly ThemeVariant? _original = Application.Current!.RequestedThemeVariant;
        public RequestedThemeScope(bool dark) => Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        public void Dispose() => Application.Current!.RequestedThemeVariant = _original;
    }

    private static void InsideScrollViewports(Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        foreach (var viewport in control.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            var origin = control.TranslatePoint(default, viewport);
            Assert.NotNull(origin);
            Assert.True(origin.Value.Y >= -1, $"{control.GetType().Name} is clipped above its viewport.");
            Assert.True(origin.Value.Y + control.Bounds.Height <= viewport.Bounds.Height + 1,
                $"{control.GetType().Name} is clipped below its viewport: {origin.Value.Y}+{control.Bounds.Height}>{viewport.Bounds.Height}");
        }
    }
}
