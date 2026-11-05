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

            units.SelectedItem = "mV";
            AuthoringUiFixture.Drain();
            Assert.Equal("mV", vm.YUnit);
            await fixture.WaitForReadinessAsync();
            var mismatch = Assert.Single(vm.EditingIssues, issue => issue.Field == "ReferenceTarget:" + second.RuleId);
            OpenIssue(mismatch);
            var rebind = fixture.Control<Button>("Rebind reference measurement");
            Assert.True(rebind.IsFocused);
            Press(window, Key.Space, PhysicalKey.Space);
            Assert.Equal(first, Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!, rule => rule.RuleId == first.RuleId));
            var repaired = Assert.Single(vm.SelectedProgram.Sidecar.ReferenceRules!, rule => rule.RuleId == second.RuleId);
            Assert.Equal("mV", repaired.Unit);
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
                Assert.Null(vm.Error);
            }
        }
        finally { app.RequestedThemeVariant = previousTheme; }
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
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
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
