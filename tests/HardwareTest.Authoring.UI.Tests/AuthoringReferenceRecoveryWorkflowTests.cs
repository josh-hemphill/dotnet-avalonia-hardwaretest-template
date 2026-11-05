using System.Collections.Specialized;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringReferenceRecoveryWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 900, 600, 20)]
    public async Task Missing_measurement_unit_issue_opens_the_unit_editor_then_rebinds_only_the_target_criterion(
        bool dark, int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var app = Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        try
        {
            var window = fixture.Show(width, height);
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.FontSize = font;
            fixture.OpenRememberedWorkspace();
            await fixture.WaitForReadinessAsync();
            var vm = fixture.ViewModel;
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
            vm.RequireGoldenReference = true;
            vm.ReferenceTolerance = "0.25";
            var first = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
            vm.AddReferenceCriterion();
            vm.ReferenceTolerance = "0.5";
            var second = Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!, rule => rule.RuleId != first.RuleId);
            vm.YUnit = string.Empty;
            vm.SelectReferenceCriterion(first.RuleId);
            await fixture.WaitForReadinessAsync();
            Assert.True(vm.Error is null, vm.Error);
            // A cleared editable selection must remain a saveable incomplete draft.
            var retained = AuthoringDocumentDto.FromDraft(vm.SelectedProgram).ToDraft();
            Assert.Equal(string.Empty, Assert.Single(AuthoringReferenceRules.Metrics(retained.Measure), metric => metric.NodeId == second.ProducerStepId).Metric.YUnit);
            var missing = Assert.Single(vm.EditingIssues, issue => issue.Field == "ReferenceMeasurementUnit:" + second.RuleId);
            OpenIssue(missing);
            Assert.Equal(second.RuleId, vm.SelectedReferenceCriterion?.RuleId);
            var unitSection = fixture.Control<Expander>("Operator display selected step");
            Assert.True(unitSection.IsExpanded);
            var units = fixture.Control<ComboBox>(AuthoringInspectorCopy.OperatorYUnitLabel);
            Assert.Contains(units.GetVisualDescendants().OfType<TextBox>(), editor => editor.IsFocused);
            ResponsiveShellTests.Inside(units, window);
            var error = fixture.Control<TextBlock>("Measurement unit error");
            Assert.Contains("Set the measurement Y unit", error.Text);
            ResponsiveShellTests.Inside(error, window);
            Capture(window, "authoring-missing-reference-unit-recovery", dark, width, height, font);

            var unitEditor = Assert.Single(units.GetVisualDescendants().OfType<TextBox>(), editor => editor.IsFocused);
            unitEditor.SelectAll();
            window.KeyTextInput("1");
            Press(window, Key.Enter, PhysicalKey.Enter);
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.Equal("1", vm.YUnit);
            await fixture.WaitForReadinessAsync();
            var mismatch = Assert.Single(vm.EditingIssues, issue => issue.Field == "ReferenceTarget:" + second.RuleId);
            OpenIssue(mismatch);
            var rebind = fixture.Control<Button>("Rebind reference measurement");
            Assert.True(rebind.IsFocused);
            Press(window, Key.Space, PhysicalKey.Space);
            Assert.Equal(first, Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!, rule => rule.RuleId == first.RuleId));
            var repaired = Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!, rule => rule.RuleId == second.RuleId);
            Assert.Equal("1", repaired.Unit);
            Assert.Equal(second.ReferenceKey, repaired.ReferenceKey);
            Assert.Equal(second.Tolerance, repaired.Tolerance);
            Assert.Equal(second.RuleId, vm.SelectedReferenceCriterion?.RuleId);
            Assert.Empty(vm.ReferenceTargetError);
            Assert.DoesNotContain(AuthoringReferenceRules.Validate(vm.SelectedProgram), finding => finding.RuleId == second.RuleId);
            Capture(window, "authoring-reference-unit-rebound-selected", dark, width, height, font);

            void OpenIssue(AuthoringEditingIssue issue)
            {
                fixture.NavigateTask(2);
                fixture.Control<Expander>("Editing findings").IsExpanded = true;
                AuthoringUiFixture.Drain();
                var action = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    button.Classes.Contains("authoringAction") && ReferenceEquals(button.DataContext, issue));
                action.BringIntoView(); AuthoringUiFixture.Drain();
                AuthoringUiFixture.Click(action);
                Assert.Equal(0, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
                Assert.True(vm.Error is null, vm.Error);
            }
        }
        finally { app.RequestedThemeVariant = previousTheme; }
    }

    [AvaloniaFact]
    public async Task Measurement_unit_dropdown_choice_commits_without_a_competing_selected_item_writer()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        fixture.NavigateTask(0);
        var unitSection = fixture.Control<Expander>("Operator display selected step");
        unitSection.IsExpanded = true;
        AuthoringUiFixture.Drain();
        var units = fixture.Control<ComboBox>(AuthoringInspectorCopy.OperatorYUnitLabel);
        Assert.Equal("V", units.SelectedItem);
        units.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(units.IsEffectivelyVisible);
        Assert.True(units.IsEffectivelyEnabled);
        // Editable ComboBox delegates keyboard focus to its template editor.
        var editor = Assert.Single(units.GetVisualDescendants().OfType<TextBox>(), control => control.IsEffectivelyVisible);
        Assert.True(editor.Focus(), $"Unit editor focusable={editor.Focusable}, enabled={editor.IsEffectivelyEnabled}, visible={editor.IsEffectivelyVisible}");
        var target = Assert.Single(units.Items.OfType<string>(), unit => unit == "ms");
        Press(window, Key.F4, PhysicalKey.F4);
        Assert.True(units.IsDropDownOpen);
        var choice = Assert.IsType<ComboBoxItem>(units.ContainerFromItem(target));
        choice.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(choice.IsEffectivelyVisible);
        // The dropdown can have a separate PopupRoot. Send actual pointer input
        // to the item's own top level, rather than keyboard input to its owner.
        var popupRoot = TopLevel.GetTopLevel(choice);
        Assert.NotNull(popupRoot);
        var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), popupRoot);
        Assert.NotNull(point);
        popupRoot.MouseDown(point.Value, MouseButton.Left);
        popupRoot.MouseUp(point.Value, MouseButton.Left);
        AuthoringUiFixture.Drain();
        Assert.False(units.IsDropDownOpen);
        Press(window, Key.Tab, PhysicalKey.Tab);
        Assert.Equal("ms", vm.YUnit);
        Assert.Equal("ms", units.SelectedItem);
        Assert.Equal("ms", Assert.Single(AuthoringReferenceRules.Metrics(AuthoringDocumentDto.FromDraft(vm.SelectedProgram!).ToDraft().Measure),
            metric => metric.NodeId == vm.SelectedSequence!.NodeId).Metric.YUnit);
    }

    [AvaloniaFact]
    public async Task Criterion_bindings_can_read_options_during_owner_changes_and_repeated_preview_routes()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(900, 600);
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        var metricIndex = vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric);
        var otherIndex = vm.SequenceItems.ToList().FindIndex(row => row.Kind != SequenceRowKind.Metric && row.IsSelectable);
        Assert.True(otherIndex >= 0);
        vm.SelectSequence(metricIndex);
        vm.RequireGoldenReference = true;
        vm.ReferenceTolerance = "0.25";
        var metricId = vm.SelectedSequence!.NodeId;
        var rule = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
        var options = vm.ReferenceCriterionOptions;
        var notifications = 0;
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs change)
        {
            var before = ++notifications;
            // Compiled SelectedItem bindings do this synchronously from CollectionChanged.
            Assert.Same(options, vm.ReferenceCriterionOptions);
            _ = vm.SelectedReferenceCriterion;
            _ = vm.ReferenceKey;
            Assert.Equal(before, notifications);
        }
        var observable = Assert.IsAssignableFrom<INotifyCollectionChanged>(options);
        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            for (var round = 0; round < 4; round++)
            {
                fixture.NavigateTask(5);
                await fixture.WaitForPreviewAsync();
                fixture.NavigateTask(0);
                vm.SelectSequence(otherIndex);
                AuthoringUiFixture.Drain();
                Assert.Empty(vm.ReferenceCriterionOptions);
                vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == metricId));
                AuthoringUiFixture.Drain();
                Assert.Equal(rule.RuleId, vm.SelectedReferenceCriterion?.RuleId);
                Assert.Equal(rule.ReferenceKey, fixture.Control<TextBox>("Reference input key").Text);
                Assert.Single(vm.ReferenceCriterionOptions);
            }
            Assert.True(notifications >= 8);
        }
        finally { observable.CollectionChanged -= OnCollectionChanged; }
    }

    [AvaloniaFact]
    public async Task Pending_preview_disables_and_guards_details_until_the_current_recording_is_published()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(900, 600);
        window.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var vm = fixture.ViewModel;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        vm.RequireGoldenReference = true;
        vm.ReferenceTolerance = "0.5";
        var rule = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
        var now = DateTimeOffset.UtcNow;
        var run = new TestRunRecord
        {
            PlanId = vm.SelectedProgram.PlanId,
            RunId = "held-preview-recording",
            StartedAt = now.AddSeconds(-1),
            CompletedAt = now.AddSeconds(1),
            Samples = [new() { MetricKey = rule.MetricKey, ProducerStepId = rule.ProducerStepId, Unit = rule.Unit, Value = 1, Timestamp = now }],
            ReferenceUses = [new()
            {
                Rule = rule, ReferenceId = "held-preview-reference", ReferenceRevision = 1,
                ReferenceValue = new() { ReferenceKey = rule.ReferenceKey, MetricKey = rule.MetricKey, ProducerStepId = rule.ProducerStepId, Unit = rule.Unit, Value = 1, SampleCount = 1 }
            }]
        };
        var path = Path.Combine(fixture.WorkspaceRoot, "held-preview.json");
        File.WriteAllText(path, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        vm.ImportRecording(path, "held-preview");
        fixture.NavigateTask(5);
        await fixture.WaitForPreviewAsync();
        Assert.True(vm.HasRecordedReferences);
        var source = fixture.Control<Button>("Preview data source details");
        var references = fixture.Control<Button>("Review recorded references");
        Assert.True(source.IsEnabled); Assert.True(references.IsEnabled);
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConfigurePreviewProcessing(action => Dispatcher.UIThread.Post(action), () => window.IsVisible, input =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Preview-details test did not release its worker.");
            return AuthoringWorkspaceViewModel.EvaluatePreview(input);
        });
        AuthoringUiFixture.Drain();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AuthoringUiFixture.Drain();
            Assert.True(vm.PreviewBusy);
            foreach (var trigger in new[] { source, references })
            {
                Assert.False(trigger.IsEnabled);
                Assert.Contains("Updating preview", AutomationProperties.GetHelpText(trigger));
                // Even a queued activation that bypasses disabled input cannot open stale/empty details.
                trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            AuthoringUiFixture.Drain();
            Assert.Empty(window.OwnedWindows);
            Assert.Equal("Updating preview", vm.PreviewSourceKind);
        }
        finally { release.Set(); }
        await fixture.WaitForPreviewAsync();
        Assert.True(source.IsEnabled); Assert.True(references.IsEnabled);
        AuthoringUiFixture.Click(references);
        var dialog = Assert.Single(window.OwnedWindows);
        Assert.Contains("held-preview-reference", fixture.Control<TextBox>("Full recorded reference review", dialog).Text);
        Press(dialog, Key.Escape, PhysicalKey.Escape);
        dialog = PreviewUiActions.OpenSourceDetails(fixture);
        var details = fixture.Control<TextBox>("Full preview data source", dialog).Text;
        Assert.Contains("held-preview-recording", details);
        Assert.DoesNotContain("Example data", details);
        Press(dialog, Key.Escape, PhysicalKey.Escape);
    }

    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        var owner = window.Owner;
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        // Enter/Escape can close a modal on key-down; the physical release then
        // belongs to its live owner, never the disposed dialog platform.
        var receiver = window.IsVisible ? window : owner;
        if (receiver?.IsVisible == true) receiver.KeyRelease(key, RawInputModifiers.None, physical, null);
        AuthoringUiFixture.Drain();
    }

    private static void Capture(Window window, string name, bool dark, int width, int height, int font)
    {
        Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
        var root = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY");
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1" || string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        AuthoringUiFixture.Drain();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(root, $"{name}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
