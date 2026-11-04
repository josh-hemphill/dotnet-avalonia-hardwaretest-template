using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorContextWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Equal_program_names_have_distinct_identity_and_full_keyboard_selectable_details(bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        var originalTheme = app.RequestedThemeVariant;
        var suffix = $"{(dark ? "dark" : "light")}-{width}x{height}-font{font}";
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var shown = E2EHarness.ShowMainWindow();
            using var first = new OperatorReviewPlanFixture();
            using var second = new OperatorReviewPlanFixture();
            var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
            var main = shown.Main; main.IsNavPaneOpen = width >= 1280; main.NavigateToPageId("RunTest");
            var run = main.RunTest;
            await second.SelectAsync(run);
            var selected = Assert.Single(run.ProgramSelection.Programs, item => item.Id == second.Id);
            var alternative = Assert.Single(run.ProgramSelection.Programs, item => item.Id == first.Id);
            Assert.Equal(alternative.DisplayName, selected.DisplayName);
            Assert.NotEqual(alternative.SelectionIdentity, selected.SelectionIdentity);
            var header = Assert.Single(window.GetVisualDescendants().OfType<RunHeaderView>());
            var selector = Assert.Single(header.GetVisualDescendants().OfType<ComboBox>());
            Assert.Same(selected, selector.SelectedItem);
            Assert.Contains(second.Id, AutomationProperties.GetHelpText(selector), StringComparison.Ordinal);
            selector.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var item = Assert.IsType<ComboBoxItem>(selector.ContainerFromIndex(selector.SelectedIndex));
            Assert.Equal(selected.SelectionIdentity, ControlAutomationPeer.CreatePeerForElement(item).GetName());
            CaptureIfEnabled(window, $"program-dropdown-equal-names-{suffix}", TopLevel.GetTopLevel(item), $"program-dropdown-equal-names-popup-{suffix}");
            selector.IsDropDownOpen = false;
            var details = Button(header, "Program details");
            details.Flyout!.ShowAt(details);
            Dispatcher.UIThread.RunJobs();
            var full = header.FindControl<TextBox>("ProgramDetailsBox")!;
            Assert.True(full.IsReadOnly);
            Assert.True(full.IsFocused);
            Assert.Equal(font, full.FontSize);
            Assert.Contains(second.Id, full.Text!, StringComparison.Ordinal);
            Assert.Contains(selected.Path, full.Text!, StringComparison.Ordinal);
            full.SelectAll();
            Assert.Equal(full.Text, full.SelectedText);
            CaptureIfEnabled(window, $"program-full-identity-{suffix}", TopLevel.GetTopLevel(full), $"program-full-identity-popup-{suffix}");
            details.Flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second.Id, run.Session.ProgramId);
            Assert.False(run.IsRunning);
            var mode = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>(),
                control => AutomationProperties.GetName(control) == "VISA: simulated");
            OperatorWorkflowUiTests.Inside(mode, window);
            OperatorWorkflowUiTests.Inside(details, window);
        }
        finally
        {
            app.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Missing_assigned_program_blocks_old_plan_execution_and_keeps_identity_and_evidence_for_reselection(bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        var originalTheme = app.RequestedThemeVariant;
        var suffix = $"{(dark ? "dark" : "light")}-{width}x{height}-font{font}";
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var shown = E2EHarness.ShowMainWindow();
            using var fixture = new OperatorReviewPlanFixture();
            var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
            var main = shown.Main; main.IsNavPaneOpen = width >= 1280; main.NavigateToPageId("RunTest");
            var run = main.RunTest;
            await fixture.SelectAsync(run);
            await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady);
            var correct = run.ProgramSelection.SelectedProgram!;
            run.SessionPanel.DutSerialInput = "UNAVAILABLE-PROGRAM-DUT";
            run.SessionPanel.DutPartInput = "PART-RETAIN";
            run.SessionPanel.DutRevisionInput = "D";
            run.SessionPanel.OperatorInput = "Program recovery operator";
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.True(run.CanStartRun);
            var store = app.Services.GetRequiredService<IRunStore>();
            var savedBefore = (await store.ListAsync()).Select(record => record.RunId).Order().ToArray();
            var previousRunId = run.LastRunId;
            var missing = new ProgramItemViewModel
            {
                Id = "assigned-unavailable-" + Guid.NewGuid().ToString("N"),
                DisplayName = "Assigned test program unavailable",
                Path = Path.Combine(Path.GetTempPath(), "missing-assigned-" + Guid.NewGuid().ToString("N") + ".TapPlan"),
                DutFamily = correct.DutFamily,
                Requirements = correct.Requirements,
            };
            run.ProgramSelection.Programs.Add(missing);
            var header = Assert.Single(window.GetVisualDescendants().OfType<RunHeaderView>());
            var selector = Assert.Single(header.GetVisualDescendants().OfType<ComboBox>());
            selector.SelectedItem = missing;
            await E2EHarness.WaitUntilAsync(() => !run.IsProgramLoading && run.ProgramAvailabilityMessage.Contains("Cannot load", StringComparison.Ordinal));
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.False(run.SessionPanel.SessionBlocked);
            Assert.False(run.CanStartRun);
            Assert.False(Button(header, "Run").IsEffectivelyEnabled);
            Assert.Contains("station technician", run.CanStartRunTip, StringComparison.Ordinal);
            OperatorWorkflowUiTests.Inside(Button(header, "Reload available test programs"), window);
            CaptureIfEnabled(window, $"program-unavailable-{suffix}", null, string.Empty);
            await run.Run.RunCommand.ExecuteAsync();
            await run.Run.RunSelectedCommand.ExecuteAsync();
            Assert.False(run.IsRunning);
            Assert.Equal(previousRunId, run.LastRunId);
            Assert.Equal(savedBefore, (await store.ListAsync()).Select(record => record.RunId).Order().ToArray());
            Assert.Equal("UNAVAILABLE-PROGRAM-DUT", run.SessionPanel.DutSerialInput);
            Assert.Equal("Program recovery operator", run.SessionPanel.OperatorInput);
            selector.SelectedItem = null;
            Assert.False(run.CanStartRun);
            Assert.True(run.ShowProgramRecovery);
            CaptureIfEnabled(window, $"program-selection-required-{suffix}", null, string.Empty);
            selector.SelectedItem = correct;
            await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady);
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.True(run.CanStartRun);
            Assert.Equal(fixture.Id, run.Session.ProgramId);
            Assert.False(run.IsRunning);
            Assert.Equal("PART-RETAIN", run.SessionPanel.DutPartInput);
            Assert.Equal("D", run.SessionPanel.DutRevisionInput);
            Assert.Equal(savedBefore, (await store.ListAsync()).Select(record => record.RunId).Order().ToArray());
        }
        finally
        {
            app.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Completed_mock_run_exposes_a_reachable_action_opening_its_exact_saved_record(int font)
    {
        Assert.True(E2EHarness.RequireApp().SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window; window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main; main.IsNavPaneOpen = false; main.NavigateToPageId("RunTest");
        var run = main.RunTest;
        await fixture.SelectAsync(run);
        run.SessionPanel.DutSerialInput = "CONTEXT-DUT";
        run.SessionPanel.DutPartInput = "CONTEXT-PART";
        run.SessionPanel.DutRevisionInput = "A";
        run.SessionPanel.OperatorInput = "Context operator";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(run.ShowSavedResult);
        var running = run.Run.RunCommand.ExecuteAsync();
        try
        {
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator)
                {
                    foreach (var field in run.Interaction.InteractionFields)
                    {
                        if (field.IsBoolean) field.BoolValue = true;
                        else field.Value = field.IsNumber ? "0" : "Context fixture";
                    }
                    run.ContinueOperatorAttention();
                }
                if (run.IsRunning) Assert.False(run.ShowSavedResult);
                return !run.IsRunning && running.IsCompleted;
            }, TimeSpan.FromMinutes(2));
            await running;
            Assert.True(run.ShowSavedResult);
            var runId = run.LastRunId;
            Assert.False(string.IsNullOrWhiteSpace(runId));
            var header = Assert.Single(window.GetVisualDescendants().OfType<RunHeaderView>());
            using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
            Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "Run", "Clear current run and iterations", "Change session", "Program details", "View saved result" })
                OperatorWorkflowUiTests.Inside(Button(header, name), window);
            OperatorWorkflowUiTests.Click(Button(header, "View saved result"));
            await E2EHarness.WaitUntilAsync(() => main.Results.OpenedRun?.RunId == runId);
            Assert.Same(main.Results, main.CurrentPage);
            Assert.Equal(fixture.Id, main.Results.OpenedRun!.PlanId);
            Assert.Equal("CONTEXT-DUT", main.Results.OpenedRun.DutSerial);
            Assert.Equal("Context operator", main.Results.OpenedRun.OperatorName);
        }
        finally
        {
            if (run.IsRunning)
            {
                await main.SafetyStopCommand.ExecuteAsync();
                await E2EHarness.WaitUntilAsync(() => !run.IsRunning && running.IsCompleted);
            }
            await running;
        }
    }

    private static void CaptureIfEnabled(Window window, string name, TopLevel? popup, string popupName)
    {
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_OPERATOR_UI") != "1") return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "operator-ui-review");
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        using (var settled = window.CaptureRenderedFrame()) Assert.NotNull(settled);
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        if (popup is not null && !ReferenceEquals(popup, window))
        {
            using var frame = popup.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, popupName + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    private static Button Button(Control root, string name)
        => Assert.Single(root.GetVisualDescendants().OfType<Button>(), control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
}
