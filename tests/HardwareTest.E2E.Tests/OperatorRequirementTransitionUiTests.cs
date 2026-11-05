using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Credentials;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorRequirementTransitionUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 900, 600, 20)]
    public async Task Stricter_program_and_saved_badge_policy_require_visible_explicit_reconfirmation(
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
        var oldTheme = app.RequestedThemeVariant;
        var oldBadgePolicy = app.SettingsStore.AppSettings.RequireCredentialForOperator;
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Width = width; window.Height = height; window.FontSize = font;
            main.IsNavPaneOpen = width >= 1280;
            app.SettingsStore.AppSettings.RequireCredentialForOperator = false;
            main.NavigateToPageId("RunTest");
            await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
            run.ProgramSelection.SelectedProgram = Assert.Single(run.ProgramSelection.Programs,
                program => program.Path == SampleProgramFactory.EmbeddedName);
            await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady);
            await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
            Settle(window);
            Type(window, "DutSerialBox", "STRICTER-PROGRAM-DUT");
            Type(window, "TechnicianBox", "Confirmed technician");
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Confirm session"));
            Settle(window);
            Assert.True(run.CanStartRun);
            Assert.Null(run.Session.DutPartNumber);
            var lastRunId = run.LastRunId;
            var sessionId = run.Session.SessionId;

            await fixture.SelectAsync(run);
            Settle(window);
            Assert.True(run.SessionPanel.NeedsDutConfirm);
            Assert.False(run.CanStartRun);
            Assert.False(run.SessionPanel.ShowSameDutPrompt);
            Assert.Equal(sessionId, run.Session.SessionId);
            Assert.Equal("STRICTER-PROGRAM-DUT", run.SessionPanel.DutSerialInput);
            Assert.Equal("Confirmed technician", run.SessionPanel.OperatorInput);
            OperatorWorkflowUiTests.Inside(Named<Button>(window, "Change session"), window);
            await run.Run.RunCommand.ExecuteAsync();
            Assert.False(run.IsRunning);
            Assert.Equal(lastRunId, run.LastRunId);
            Assert.DoesNotContain("Same DUT", run.Status, StringComparison.OrdinalIgnoreCase);
            Capture(window, "persona-program-requirements-changed", dark, width, height, font);

            Type(window, "DutPartBox", "PART-STRICTER");
            Type(window, "DutRevisionBox", "R");
            await run.Run.RunCommand.ExecuteAsync();
            Assert.True(run.SessionPanel.SessionBlocked);
            Assert.False(run.IsRunning);
            Assert.Equal(lastRunId, run.LastRunId);
            Capture(window, "persona-required-details-unconfirmed", dark, width, height, font);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Confirm session"));
            Settle(window);
            Assert.True(run.CanStartRun);
            Assert.Equal("PART-STRICTER", run.Session.DutPartNumber);
            Assert.Equal("R", run.Session.DutRevision);

            // A saved station policy is a production event, not a manual panel refresh.
            app.SettingsStore.AppSettings.RequireCredentialForOperator = true;
            await app.SettingsStore.SaveAppSettingsAsync();
            await E2EHarness.WaitUntilAsync(() => run.SessionPanel.NeedsDutConfirm && run.SessionPanel.SessionBlocked);
            Settle(window);
            Assert.False(run.CanStartRun);
            OperatorWorkflowUiTests.Inside(Named<Button>(window, "Change session"), window);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Confirm session"));
            Assert.True(run.SessionPanel.SessionBlocked);
            Assert.Contains("badge", run.SessionPanel.OperatorError, StringComparison.OrdinalIgnoreCase);
            Capture(window, "persona-badge-policy-reconfirmation", dark, width, height, font);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Tap or insert badge"));
            await E2EHarness.WaitUntilAsync(() => !run.SessionPanel.IsCapturingCredential
                && run.Session.OperatorCredentialSerial == MockOperatorCredentialBroker.MockSerial);
            Assert.True(run.SessionPanel.SessionBlocked);
            await run.Run.RunCommand.ExecuteAsync();
            Assert.False(run.IsRunning);
            Assert.True(run.SessionPanel.SessionBlocked);
            Assert.Equal(lastRunId, run.LastRunId);
            Capture(window, "persona-badge-present-unconfirmed", dark, width, height, font);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Confirm session"));
            Settle(window);
            Assert.True(run.CanStartRun);
            Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, run.Session.OperatorName);
            Assert.Equal(sessionId, run.Session.SessionId);
            Assert.False(run.HasBanner);
            Assert.False(main.ShellNotification.HasContent);
            Capture(window, "persona-required-context-reconfirmed", dark, width, height, font);
        }
        finally
        {
            app.SettingsStore.AppSettings.RequireCredentialForOperator = oldBadgePolicy;
            app.RequestedThemeVariant = oldTheme;
            if (run.CanSafetyStop)
            {
                await main.SafetyStopCommand.ExecuteAsync();
                await E2EHarness.WaitUntilAsync(() => !run.IsRunning && !run.CanSafetyStop, TimeSpan.FromSeconds(60));
            }
        }
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.IsEffectivelyVisible
            && AutomationProperties.GetName(control) == name);

    private static void Type(Window window, string name, string value)
    {
        var preparation = window.GetVisualDescendants().OfType<RunPreparationView>().Single(view => view.IsEffectivelyVisible);
        var input = preparation.FindControl<TextBox>(name)!;
        input.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(input.Focus()); input.SelectAll();
        window.KeyTextInput(value); Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, input.Text);
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
