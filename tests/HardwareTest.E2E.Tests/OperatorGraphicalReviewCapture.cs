using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.OpenTap.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

/// Opt-in evidence from the real operator shell and its mock-instrument execution path.
/// No images are written during ordinary E2E validation.
[Collection("E2E")]
public sealed class OperatorGraphicalReviewCapture
{
    public static bool CaptureEnabled => Environment.GetEnvironmentVariable("HT_CAPTURE_OPERATOR_UI") == "1";

    [AvaloniaFact]
    public async Task Capture_operator_preparation_run_results_and_settings()
    {
        Assert.SkipUnless(CaptureEnabled, "Set HT_CAPTURE_OPERATOR_UI=1 to capture operator review evidence.");
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa, "Review execution must use the isolated mock-instrument configuration.");
        using var shown = E2EHarness.ShowMainWindow();
        var main = shown.Main;
        var window = shown.Window;
        var run = main.RunTest;
        var originalTheme = app.RequestedThemeVariant;
        var originalDebug = app.SettingsStore.AppSettings.IsEngineerDebugMode;
        main.NavigateToPageId("Settings");
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        try
        {
            main.NavigateToPageId("Home");
            CaptureMatrix(window, main, "home");
            main.NavigateToPageId("Results");
            await main.Results.RefreshCommand.ExecuteAsync();
            CaptureMatrix(window, main, "results-empty");
            await main.Results.ExportPackageCommand.ExecuteAsync();
            Assert.Contains("run", main.Results.Status, StringComparison.OrdinalIgnoreCase);
            CaptureMatrix(window, main, "export-no-selection");
            main.NavigateToPageId("ReportPreview");
            await main.ReportPreview.SaveCopyCommand.ExecuteAsync();
            CaptureMatrix(window, main, "report-no-selection");
            main.NavigateToPageId("Settings");
            CaptureMatrix(window, main, "settings");
            settings.IsEngineerDebugMode = true;
            var engineerSettings = Named<Expander>(window, "Engineer settings");
            CaptureMatrix(window, main, "settings-engineer", prepare: () => ExpandAtTop(engineerSettings));
            engineerSettings.IsExpanded = false;
            settings.IsEngineerDebugMode = false;
            var badgeSettings = Named<Expander>(window, "Badge and signing settings");
            CaptureMatrix(window, main, "settings-badge-signing", prepare: () => ExpandAtTop(badgeSettings));
            badgeSettings.IsExpanded = false;

            main.NavigateToPageId("RunTest");
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            run.ProgramSelection.SelectedProgram = Assert.Single(run.ProgramSelection.Programs,
                program => string.Equals(program.Path, SampleProgramFactory.EmbeddedName, StringComparison.OrdinalIgnoreCase));
            await E2EHarness.WaitUntilAsync(() => run.StepTree.Hierarchy.Count > 0, TimeSpan.FromSeconds(30));
            run.SessionPanel.DutSerialInput = "";
            run.SessionPanel.OperatorInput = "";
            CaptureMatrix(window, main, "preparation-empty");
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.True(run.SessionPanel.SessionBlocked);
            Assert.False(string.IsNullOrWhiteSpace(run.SessionPanel.DutSerialError));
            CaptureMatrix(window, main, "preparation-validation");
            run.SessionPanel.DutSerialInput = "REVIEW-DUT-0001";
            run.SessionPanel.OperatorInput = "Review operator";
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            await E2EHarness.PumpUiAsync();
            Assert.False(run.SessionPanel.SessionBlocked);
            Assert.True(run.Workspace.ShowSteps);
            CaptureMatrix(window, main, "run-ready");

            var promptCount = 0;
            var runTask = run.Run.RunCommand.ExecuteAsync();
            try
            {
                await E2EHarness.WaitUntilAsync(() => run.IsRunning || runTask.IsCompleted, TimeSpan.FromSeconds(30));
                if (run.IsRunning) CaptureMatrix(window, main, "run-active");
                await E2EHarness.WaitUntilAsync(() =>
                {
                    if (run.Interaction.IsAwaitingOperator)
                    {
                        promptCount++;
                        CaptureMatrix(window, main, promptCount == 1 ? "operator-confirmation" : "operator-input");
                        FillInteractionFields(run);
                        run.ContinueOperatorAttention();
                    }
                    return !run.IsRunning && runTask.IsCompleted;
                }, TimeSpan.FromMinutes(2), "Mock review run did not complete.");
                await runTask;
            }
            finally
            {
                if (run.IsRunning)
                {
                    await main.SafetyStopCommand.ExecuteAsync();
                    await E2EHarness.WaitUntilAsync(() => !run.IsRunning, TimeSpan.FromSeconds(30), "Mock review run did not stop.");
                }
            }
            Assert.False(string.IsNullOrWhiteSpace(run.LastRunId));
            Assert.True(promptCount >= 1, "The actual embedded program must expose its operator interaction.");
            CaptureMatrix(window, main, "run-completed");
            run.StepTree.SelectedStep = Flatten(run.StepTree.Hierarchy).First(step => step.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase));
            run.Workspace.OpenDetails();
            CaptureMatrix(window, main, "step-details");
            if (run.Workspace.CanOpenChart)
            {
                run.Workspace.OpenChart();
                CaptureMatrix(window, main, "run-chart");
                var options = Named<Button>(window, "Chart options");
                CaptureMatrix(window, main, "run-chart-options",
                    prepare: () => options.Flyout!.ShowAt(options),
                    cleanup: () => options.Flyout!.Hide(),
                    popupRoot: () => TopLevel.GetTopLevel((Control)((Flyout)options.Flyout!).Content!));
            }

            main.NavigateToPageId("Results");
            await main.Results.RefreshCommand.ExecuteAsync();
            Assert.NotEmpty(main.Results.Runs);
            CaptureMatrix(window, main, "results-list");
            var resultFilters = Named<Expander>(window, "Result filters");
            CaptureMatrix(window, main, "results-filters-expanded", prepare: () => resultFilters.IsExpanded = true);
            resultFilters.IsExpanded = false;
            main.Results.SelectedRun = Assert.Single(main.Results.Runs, record => record.RunId == run.LastRunId);
            await main.Results.OpenCommand.ExecuteAsync();
            Assert.NotNull(main.Results.OpenedRun);
            CaptureMatrix(window, main, "results-detail");
            main.NavigateToPageId("ReportPreview");
            await main.ReportPreview.LoadLatestCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => main.ReportPreview.Pages.Count > 0 || !main.ReportPreview.IsBusy,
                TimeSpan.FromMinutes(2));
            CaptureMatrix(window, main, "report-preview");
            await main.ReportPreview.LoadFromPathAsync(Path.Combine(app.SettingsStore.RootDirectory, "missing-review-report.pdf"));
            Assert.True(main.ReportPreview.ShowEmptyState);
            CaptureMatrix(window, main, "report-error");
            Window? reportDetails = null;
            CaptureMatrix(window, main, "report-details",
                prepare: () =>
                {
                    OperatorWorkflowUiTests.Click(Named<Button>(window, "Show full report details"));
                    reportDetails = Assert.Single(window.OwnedWindows);
                    Assert.True(Named<TextBox>(reportDetails, "Full report details").IsReadOnly);
                },
                cleanup: () => OperatorWorkflowUiTests.Click(Named<Button>(reportDetails!, "Close report details")),
                captureTarget: () => reportDetails);
            await CaptureExtendedWorkflowsAsync(window, main, settings);
        }
        finally
        {
            if (run.IsRunning)
            {
                await main.SafetyStopCommand.ExecuteAsync();
                await E2EHarness.WaitUntilAsync(() => !run.IsRunning, TimeSpan.FromSeconds(30));
            }
            settings.IsEngineerDebugMode = originalDebug;
            await settings.SaveCommand.ExecuteAsync();
            app.RequestedThemeVariant = originalTheme;
        }
    }

    private static async Task CaptureExtendedWorkflowsAsync(Window window, MainWindowViewModel main, SettingsViewModel settings)
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        settings.IsEngineerDebugMode = true;
        await settings.SaveCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => main.NavigationItems.Any(item => item.Id == "Instruments"), TimeSpan.FromSeconds(10));
        main.NavigateToPageId("Instruments");
        await main.Instruments.RefreshSlotsCommand.ExecuteAsync();
        await main.Instruments.RefreshVisaDiscoverCommand.ExecuteAsync();
        CaptureMatrix(window, main, "instruments-commissioning");

        var instruments = main.Instruments;
        var programId = Assert.IsType<ProgramItemViewModel>(main.RunTest.ProgramSelection.SelectedProgram).Id;
        var slot = Assert.Single(instruments.SlotOverrides, item => item.PlanId == programId && item.SlotName == "DMM");
        var originalSlot = instruments.SelectedSlot;
        var originalVisa = instruments.SelectedVisa;
        var originalOpenTap = instruments.SelectedOpenTap;
        var originalPlanFilter = instruments.PlanFilter;
        var originalOverride = slot.OverrideResource;
        var originalStatus = instruments.Status;
        var savedOverrides = app.SettingsStore.AppSettings.PlanSlotOverrides
            .Select(item => (item.PlanId, item.SlotName, item.RoleHint, item.Resource)).ToArray();
        try
        {
            instruments.PlanFilter = slot.PlanDisplayName;
            // Display names need not be unique; choose the actual current plan/slot object.
            instruments.SelectedSlot = slot;
            instruments.SelectedVisa = instruments.DiscoveredVisa.FirstOrDefault(resource =>
                resource.Resource.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase)
                && resource.Resource != slot.EffectiveResource)
                ?? instruments.DiscoveredVisa.First(resource => resource.Resource.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase));
            Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
            Assert.Same(slot, instruments.SelectedSlot);
            Assert.Equal(programId, slot.PlanId);
            Assert.Equal("DMM", slot.SlotName);
            var selectedResource = instruments.SelectedVisa?.Resource
                ?? throw new InvalidOperationException("The selected mock resource was lost.");
            CaptureMatrix(window, main, "instruments-selected-resource", prepare: () =>
            {
                var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                    AutomationProperties.GetName(control) == "Discovered VISA resource");
                picker.BringIntoView();
            });
            Assert.Equal(selectedResource, instruments.SelectedVisa?.Resource);
            await instruments.ApplySelectedResourceCommand.ExecuteAsync();
            Assert.Equal(selectedResource, slot.OverrideResource);
            Assert.Contains("save", instruments.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(savedOverrides, app.SettingsStore.AppSettings.PlanSlotOverrides
                .Select(item => (item.PlanId, item.SlotName, item.RoleHint, item.Resource)).ToArray());
            CaptureMatrix(window, main, "instruments-applied-unsaved");
        }
        finally
        {
            slot.OverrideResource = originalOverride;
            instruments.PlanFilter = originalPlanFilter;
            instruments.SelectedSlot = originalSlot;
            instruments.SelectedVisa = originalVisa;
            instruments.SelectedOpenTap = originalOpenTap;
            instruments.Status = originalStatus;
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(savedOverrides, app.SettingsStore.AppSettings.PlanSlotOverrides
            .Select(item => (item.PlanId, item.SlotName, item.RoleHint, item.Resource)).ToArray());

        using var fixture = new OperatorReviewPlanFixture();
        var run = main.RunTest;
        main.NavigateToPageId("RunTest");
        await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
        await fixture.SelectAsync(run);
        run.SessionPanel.DutSerialInput = "REVIEW-REQUIRED-IDENTITY";
        run.SessionPanel.OperatorInput = "Review operator";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(run.SessionPanel.SessionBlocked);
        CaptureMatrix(window, main, "preparation-required-part");
        run.SessionPanel.DutPartInput = "CONTROLLER-PN-101";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.True(run.SessionPanel.SessionBlocked);
        CaptureMatrix(window, main, "preparation-required-revision");
        run.SessionPanel.DutRevisionInput = "C";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(run.SessionPanel.SessionBlocked);
        run.SessionPanel.Session.MarkStale();
        run.SessionPanel.RefreshSessionSummary();
        Assert.True(run.SessionPanel.IsStalePrompt);
        CaptureMatrix(window, main, "session-stale");
        await run.SessionPanel.ConfirmSameDutCommand.ExecuteAsync();
        Assert.False(run.SessionPanel.SessionBlocked);

        var runTask = run.Run.RunCommand.ExecuteAsync();
        var capturedInvalid = false;
        await E2EHarness.WaitUntilAsync(() =>
        {
            if (run.Interaction.IsAwaitingOperator)
            {
                if (run.Interaction.HasInteractionFields && !capturedInvalid)
                {
                    run.ContinueOperatorAttention();
                    Assert.False(string.IsNullOrWhiteSpace(run.Interaction.InteractionValidationError));
                    CaptureMatrix(window, main, "operator-long-required-prompt");
                    FillInteractionFields(run);
                    var number = Assert.Single(run.Interaction.InteractionFields, field => field.IsNumber);
                    number.Value = "not a number";
                    run.ContinueOperatorAttention();
                    Assert.False(string.IsNullOrWhiteSpace(run.Interaction.InteractionValidationError));
                    CaptureMatrix(window, main, "operator-invalid-number");
                    capturedInvalid = true;
                }
                FillInteractionFields(run);
                foreach (var field in run.Interaction.InteractionFields.Where(field => field.IsNumber)) field.Value = "0";
                run.ContinueOperatorAttention();
            }
            return !run.IsRunning && runTask.IsCompleted;
        }, TimeSpan.FromMinutes(2), "Required-input mock fixture did not finish.");
        await runTask;
        Assert.True(capturedInvalid);
        var store = app.Services.GetRequiredService<IRunStore>();
        var failed = await store.LoadAsync(run.LastRunId!);
        Assert.NotNull(failed);
        Assert.Equal(RunResult.Failed, failed.Result);
        CaptureMatrix(window, main, "run-failed");
        run.StepTree.SelectedStep = Assert.Single(OperatorReviewPlanFixture.FlattenHierarchy(run.StepTree.Hierarchy),
            step => step.Name == OperatorReviewPlanFixture.LongAcquisitionName);
        run.Workspace.OpenDetails();
        CaptureMatrix(window, main, "step-details-long-name");
        main.NavigateToPageId("Results");
        await main.Results.RefreshCommand.ExecuteAsync();
        main.Results.SelectedRun = Assert.Single(main.Results.Runs, record => record.RunId == run.LastRunId);
        await main.Results.OpenCommand.ExecuteAsync();
        CaptureMatrix(window, main, "results-failed-detail");
        await main.Results.CloseDetailCommand.ExecuteAsync();
        Assert.False(main.Results.ShowDetail);
        main.Results.SearchText = "no-such-review-run-zzzz";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(main.Results.Runs);
        Assert.False(main.Results.ShowDetail);
        CaptureMatrix(window, main, "results-no-matches");
        main.Results.SearchText = "";

        main.NavigateToPageId("RunTest");
        run.Workspace.OpenSteps();
        run.ApplySelectionFromInspect(Assert.Single(OperatorReviewPlanFixture.FlattenHierarchy(run.StepTree.Hierarchy),
            step => step.Name == OperatorReviewPlanFixture.LongAcquisitionName).Path);
        Assert.True(run.CanStartRunSelected);
        var selectedTask = run.Run.RunSelectedCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => !run.IsRunning && selectedTask.IsCompleted, TimeSpan.FromMinutes(1));
        await selectedTask;
        var selected = await store.LoadAsync(run.LastRunId!);
        Assert.NotNull(selected);
        Assert.Equal(RunExecutionScopes.SelectedStep, selected.ExecutionScope);
        Assert.Equal(OperatorReviewPlanFixture.LongAcquisitionName, run.StepTree.SelectedStep!.Name);
        CaptureMatrix(window, main, "run-selected-step-completed");
        main.NavigateToPageId("Results");
        await main.Results.OpenRunByIdAsync(selected.RunId);
        CaptureMatrix(window, main, "results-selected-step-detail");
        main.NavigateToPageId("RunTest");
        var stoppedTask = run.Run.RunCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => run.Interaction.IsAwaitingOperator || stoppedTask.IsCompleted,
            TimeSpan.FromSeconds(30));
        Assert.True(run.Interaction.IsAwaitingOperator);
        CaptureMatrix(window, main, "run-before-stop");
        await main.SafetyStopCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => !run.IsRunning && stoppedTask.IsCompleted, TimeSpan.FromSeconds(60));
        await stoppedTask;
        var stopped = await store.LoadAsync(run.LastRunId!);
        Assert.NotNull(stopped);
        Assert.NotEqual(RunResult.Passed, stopped.Result);
        CaptureMatrix(window, main, "run-stopped");
    }

    private static void FillInteractionFields(RunTestViewModel run)
    {
        foreach (var field in run.Interaction.InteractionFields)
        {
            if (field.IsBoolean) { if (field.Required) field.BoolValue = true; }
            else if (string.IsNullOrWhiteSpace(field.Value)) field.Value = field.IsNumber ? "0" : "Review fixture";
        }
    }

    private static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var step in roots)
        {
            yield return step;
            foreach (var child in Flatten(step.Children)) yield return child;
        }
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetName(control) == name);

    private static void ExpandAtTop(Expander section)
    {
        section.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var viewport = section.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (viewport?.Content is Visual content && section.TranslatePoint(default, content) is { } position)
            viewport.Offset = new Vector(viewport.Offset.X, Math.Max(0, position.Y));
    }

    private static void SaveFrame(TopLevel target, string name)
    {
        using var bitmap = target.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "operator-ui-review");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void CaptureMatrix(Window window, MainWindowViewModel main, string state,
        Action? prepare = null, Action? cleanup = null, Func<TopLevel?>? captureTarget = null, Func<TopLevel?>? popupRoot = null)
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            foreach (var (width, height, font) in new[] { (1280, 800, 14), (900, 600, 20) })
            {
                window.Width = width; window.Height = height; window.FontSize = font;
                main.IsNavPaneOpen = width >= 1280;
                Dispatcher.UIThread.RunJobs();
                // Resize handlers can post focus/scroll anchoring after the first layout.
                // Let the real view complete that work; do not focus a field for the screenshot.
                using (var warm = window.CaptureRenderedFrame()) Assert.NotNull(warm);
                Dispatcher.UIThread.RunJobs();
                try
                {
                    prepare?.Invoke();
                    Dispatcher.UIThread.RunJobs();
                    var target = captureTarget?.Invoke() ?? window;
                    using (var settled = target.CaptureRenderedFrame()) Assert.NotNull(settled);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new Size(width, height), window.ClientSize);
                    if (state == "results-no-matches")
                    {
                        Assert.Empty(main.Results.Runs);
                        Assert.False(main.Results.ShowDetail);
                    }
                    var suffix = $"{theme.Key.ToString()!.ToLowerInvariant()}-{width}x{height}-font{font}";
                    SaveFrame(target, $"{state}-{suffix}");
                    // Native popup hosts are separate surfaces; retain their actual pixels too.
                    if (popupRoot?.Invoke() is { } popup && !ReferenceEquals(popup, window))
                        SaveFrame(popup, $"{state}-popup-{suffix}");
                }
                finally
                {
                    cleanup?.Invoke();
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }
    }
}
