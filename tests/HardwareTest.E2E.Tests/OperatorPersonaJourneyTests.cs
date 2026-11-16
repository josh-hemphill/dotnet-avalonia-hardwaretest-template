using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Features;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorPersonaJourneyTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Departing_and_incoming_operators_complete_distinct_jobs_without_changing_saved_evidence(
        bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        Assert.True(app.SettingsStore.AppSettings.UseMockOperatorCredential);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window;
        var main = shown.Main;
        var run = main.RunTest;
        var store = app.Services.GetRequiredService<IRunStore>();
        var originalTheme = app.RequestedThemeVariant;
        var originalReconfirm = app.SettingsStore.AppSettings.RequireDutConfirmEveryRun;
        var originalRequiredBadge = app.SettingsStore.AppSettings.RequireCredentialForOperator;
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Width = width; window.Height = height; window.FontSize = font;
            main.IsNavPaneOpen = width >= 1280;
            app.SettingsStore.AppSettings.RequireDutConfirmEveryRun = true;
            app.SettingsStore.AppSettings.RequireCredentialForOperator = false;
            main.NavigateToPageId("RunTest");
            await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
            await fixture.SelectAsync(run);
            Settle(window);
            FillIdentity(window, "HANDOFF-DEPARTING", "PN-HANDOFF", "A", null);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Tap or insert badge"));
            await E2EHarness.WaitUntilAsync(() => !run.SessionPanel.IsCapturingCredential
                && run.Session.OperatorCredentialSerial == MockOperatorCredentialBroker.MockSerial);
            Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, run.SessionPanel.OperatorInput);
            Confirm(window, run);
            var departingSessionId = run.Session.SessionId;
            Capture(window, "persona-departing-ready", dark, width, height, font);

            var departingId = await ExecuteJobAsync(window, run);
            var departing = Assert.IsType<TestRunRecord>(await store.LoadAsync(departingId));
            Assert.Equal(RunResult.Failed, departing.Result);
            Assert.Equal("HANDOFF-DEPARTING", departing.DutSerial);
            Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, departing.OperatorName);
            Assert.Equal(departingSessionId, departing.SessionId);
            Assert.Equal(fixture.Id, departing.PlanId);
            var departingPath = Path.Combine(store.GetRunDirectory(departingId), "run.json");
            var originalEvidence = File.ReadAllBytes(departingPath);

            Assert.True(run.SessionPanel.IsStalePrompt);
            Assert.False(run.CanStartRun);
            Capture(window, "persona-repeat-reconfirmation", dark, width, height, font);
            OperatorWorkflowUiTests.Click(ContentButton(window, "Same DUT"));
            Assert.True(run.CanStartRun);
            Assert.Equal(departingSessionId, run.Session.SessionId);
            Assert.Equal("HANDOFF-DEPARTING", run.Session.DutSerial);
            Assert.Equal(MockOperatorCredentialBroker.MockSerial, run.Session.OperatorCredentialSerial);
            Assert.False(run.IsRunning);

            await OpenSavedResultAsync(window, main, departingId);
            Capture(window, "persona-departing-saved-result", dark, width, height, font);
            main.NavigateToPageId("RunTest");
            Settle(window);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Clear current run and iterations"));
            Assert.Null(run.LastRunId);
            Assert.Equal(departingSessionId, run.Session.SessionId);
            Assert.Equal("HANDOFF-DEPARTING", run.Session.DutSerial);
            Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, run.Session.OperatorName);
            Assert.Equal(fixture.Id, run.ProgramSelection.SelectedProgram!.Id);
            Assert.True(run.CanStartRun);
            Assert.Equal(originalEvidence, File.ReadAllBytes(departingPath));

            OperatorWorkflowUiTests.Click(Named<Button>(window, "Change session"));
            Assert.NotEqual(departingSessionId, run.Session.SessionId);
            Assert.Null(run.Session.OperatorCredentialSerial);
            Assert.Null(run.Session.OperatorCredentialTransport);
            Assert.Null(run.Session.OperatorName);
            Assert.Empty(run.Session.DutSerial);
            Assert.Empty(run.SessionPanel.OperatorInput);
            Assert.Empty(run.SessionPanel.DutSerialInput);
            Assert.True(run.SessionPanel.SessionBlocked);
            Assert.False(run.CanStartRun);
            Assert.Equal(fixture.Id, run.ProgramSelection.SelectedProgram!.Id);
            Capture(window, "persona-incoming-session-empty", dark, width, height, font);

            FillIdentity(window, "HANDOFF-INCOMING", "PN-HANDOFF", "B", "Incoming operator");
            Confirm(window, run);
            var incomingSessionId = run.Session.SessionId;
            Capture(window, "persona-incoming-ready", dark, width, height, font);
            var incomingId = await ExecuteJobAsync(window, run);
            Assert.NotEqual(departingId, incomingId);
            var incoming = Assert.IsType<TestRunRecord>(await store.LoadAsync(incomingId));
            Assert.Equal(incomingSessionId, incoming.SessionId);
            Assert.NotEqual(departing.SessionId, incoming.SessionId);
            Assert.Equal("Incoming operator", incoming.OperatorName);
            Assert.Equal("HANDOFF-INCOMING", incoming.DutSerial);
            Assert.Equal("PN-HANDOFF", incoming.DutPartNumber);
            Assert.Equal("B", incoming.DutRevision);
            Assert.Equal(fixture.Id, incoming.PlanId);
            Assert.Equal(originalEvidence, File.ReadAllBytes(departingPath));

            await OpenSavedResultAsync(window, main, incomingId);
            Capture(window, "persona-incoming-saved-result", dark, width, height, font);
            var results = window.GetVisualDescendants().OfType<ResultsView>().Single(view => view.IsEffectivelyVisible);
            OperatorWorkflowUiTests.Click(results.FindControl<Button>("ResultsCloseButton")!);
            Type(window, results.FindControl<TextBox>("ResultsSearchBox")!, departingId);
            await E2EHarness.WaitUntilAsync(() => main.Results.Runs.Count == 1
                && main.Results.Runs[0].RunId == departingId);
            var list = results.FindControl<ListBox>("RunResultsList")!;
            if (!main.Results.ShowDetail)
            {
                // Filtering out the previously opened incoming record clears its
                // selection. Activate the actual remaining row rather than assuming
                // focusing the list container selects an item for Enter.
                list.ScrollIntoView(main.Results.Runs[0]);
                Settle(window);
                var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0));
                row.BringIntoView();
                Settle(window);
                OperatorWorkflowUiTests.Inside(row, window);
                var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
                Assert.NotNull(point);
                window.MouseDown(point.Value, MouseButton.Left);
                window.MouseUp(point.Value, MouseButton.Left);
                await E2EHarness.WaitUntilAsync(() => main.Results.ShowDetail && main.Results.OpenedRun?.RunId == departingId,
                    TimeSpan.FromSeconds(15), $"Activating filtered departing row did not open {departingId}.");
            }
            Assert.Equal(departingId, main.Results.OpenedRun!.RunId);
            Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, main.Results.OpenedRun.OperatorName);
            Assert.Equal(originalEvidence, File.ReadAllBytes(departingPath));
            Capture(window, "persona-departing-evidence-retained", dark, width, height, font);
        }
        finally
        {
            await StopIfRunningAsync(main);
            app.SettingsStore.AppSettings.RequireDutConfirmEveryRun = originalReconfirm;
            app.SettingsStore.AppSettings.RequireCredentialForOperator = originalRequiredBadge;
            app.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaFact]
    public async Task Operator_can_stop_a_required_prompt_from_Settings_and_inspect_the_cancelled_saved_record()
    {
        var app = E2EHarness.RequireApp();
        Assert.True(app.SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window;
        var main = shown.Main;
        var run = main.RunTest;
        window.Width = 900; window.Height = 600; window.FontSize = 20;
        main.IsNavPaneOpen = false;
        try
        {
            main.NavigateToPageId("RunTest");
            await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
            await fixture.SelectAsync(run);
            Settle(window);
            FillIdentity(window, "PERSONA-STOP", "PN-STOP", "C", "Interrupted operator");
            Confirm(window, run);
            var sessionId = run.Session.SessionId;
            var previousRunId = run.LastRunId;
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Run"));
            await E2EHarness.WaitUntilAsync(() => run.IsRunning && run.Interaction.IsAwaitingOperator,
                TimeSpan.FromSeconds(30));
            // The UI hides session changes while active; the command guard also
            // refuses programmatic attempts rather than mutating ownership.
            await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
            Assert.Equal(sessionId, run.Session.SessionId);
            main.NavigateToPageId("Settings");
            Settle(window);
            Assert.Equal("Settings", main.SelectedItem!.Id);
            var stop = Named<Button>(window, main.SafetyStopLabel);
            OperatorWorkflowUiTests.Inside(stop, window);
            Capture(window, "persona-stop-from-settings", app.ActualThemeVariant == ThemeVariant.Dark, 900, 600, 20);
            OperatorWorkflowUiTests.Click(stop);
            await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !run.CanSafetyStop
                && run.LastRunId is not null && run.LastRunId != previousRunId, TimeSpan.FromSeconds(60));
            var store = app.Services.GetRequiredService<IRunStore>();
            var stopped = Assert.IsType<TestRunRecord>(await store.LoadAsync(run.LastRunId!));
            Assert.Equal(RunResult.Cancelled, stopped.Result);
            Assert.Equal(sessionId, stopped.SessionId);
            Assert.Equal("Interrupted operator", stopped.OperatorName);
            Assert.Equal("PERSONA-STOP", stopped.DutSerial);
            main.NavigateToPageId("RunTest");
            Settle(window);
            await OpenSavedResultAsync(window, main, stopped.RunId);
            Capture(window, "persona-stopped-saved-result", app.ActualThemeVariant == ThemeVariant.Dark, 900, 600, 20);
        }
        finally
        {
            await StopIfRunningAsync(main);
        }
    }

    private static async Task<string> ExecuteJobAsync(Window window, RunTestViewModel run)
    {
        var prior = run.LastRunId;
        OperatorWorkflowUiTests.Click(Named<Button>(window, "Run"));
        await E2EHarness.WaitUntilAsync(() =>
        {
            if (run.Interaction.IsAwaitingOperator)
            {
                Settle(window);
                var host = window.GetVisualDescendants().OfType<InteractionHostView>().Single(view => view.IsEffectivelyVisible);
                foreach (var field in run.Interaction.InteractionFields)
                {
                    if (field.IsBoolean)
                    {
                        var check = Named<CheckBox>(host, field.AutomationName);
                        if (field.Required && check.IsChecked != true)
                        {
                            check.BringIntoView(); Dispatcher.UIThread.RunJobs();
                            Assert.True(check.Focus());
                            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                        }
                    }
                    else
                    {
                        Type(window, Named<TextBox>(host, field.AutomationName), field.IsNumber ? "3.5" : "FIXTURE-PERSONA");
                    }
                }
                var next = host.FindControl<Button>("ContinueButton")!;
                OperatorWorkflowUiTests.Inside(next, window);
                OperatorWorkflowUiTests.Click(next);
            }
            return !run.IsRunning && run.LastRunId is not null && run.LastRunId != prior;
        }, TimeSpan.FromMinutes(2), "Persona job did not finish through its actual operator controls.");
        return Assert.IsType<string>(run.LastRunId);
    }

    private static void FillIdentity(Window window, string serial, string part, string revision, string? technician)
    {
        var preparation = window.GetVisualDescendants().OfType<RunPreparationView>().Single(view => view.IsEffectivelyVisible);
        Type(window, preparation.FindControl<TextBox>("DutSerialBox")!, serial);
        Type(window, preparation.FindControl<TextBox>("DutPartBox")!, part);
        Type(window, preparation.FindControl<TextBox>("DutRevisionBox")!, revision);
        if (technician is not null) Type(window, preparation.FindControl<TextBox>("TechnicianBox")!, technician);
    }

    private static void Confirm(Window window, RunTestViewModel run)
    {
        OperatorWorkflowUiTests.Click(Named<Button>(window, "Confirm session"));
        Settle(window);
        Assert.False(run.SessionPanel.SessionBlocked);
        Assert.False(run.IsRunning);
        var start = Named<Button>(window, "Run");
        OperatorWorkflowUiTests.Inside(start, window);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.True(start.IsFocused);
    }

    private static async Task OpenSavedResultAsync(Window window, MainWindowViewModel main, string expectedId)
    {
        OperatorWorkflowUiTests.Click(Named<Button>(window, "View saved result"));
        await E2EHarness.WaitUntilAsync(() => main.SelectedItem?.Id == "Results"
            && main.Results.ShowDetail && main.Results.OpenedRun?.RunId == expectedId);
        Settle(window);
    }

    private static async Task StopIfRunningAsync(MainWindowViewModel main)
    {
        if (main.RunTest.CanSafetyStop)
        {
            await main.SafetyStopCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() => !main.RunTest.IsRunning && !main.RunTest.CanSafetyStop, TimeSpan.FromSeconds(60));
        }
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.IsEffectivelyVisible
            && AutomationProperties.GetName(control) == name);

    private static Button ContentButton(Control root, string content)
        => Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible
            && string.Equals(button.Content as string, content, StringComparison.Ordinal));

    private static void Type(Window window, TextBox control, string value)
    {
        control.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(control.Focus()); control.SelectAll();
        window.KeyTextInput(value); Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, control.Text);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        Settle(window);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "operator-persona-review");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
