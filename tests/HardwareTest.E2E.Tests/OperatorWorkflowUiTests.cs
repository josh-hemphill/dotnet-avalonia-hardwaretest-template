using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Required_identity_errors_focus_the_exact_field_and_clear_after_keyboard_correction(int font)
    {
        Assert.True(E2EHarness.RequireApp().SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main;
        var run = main.RunTest;
        main.NavigateToPageId("RunTest");
        await run.SessionPanel.ChangeSessionCommand.ExecuteAsync();
        await fixture.SelectAsync(run);
        var planBytes = File.ReadAllBytes(fixture.PlanPath);
        var sidecarBytes = File.ReadAllBytes(fixture.SidecarPath);
        var preparation = window.GetVisualDescendants().OfType<RunPreparationView>().Single(view => view.IsEffectivelyVisible);
        var serial = preparation.FindControl<TextBox>("DutSerialBox")!;
        var technician = preparation.FindControl<TextBox>("TechnicianBox")!;
        var part = preparation.FindControl<TextBox>("DutPartBox")!;
        var revision = preparation.FindControl<TextBox>("DutRevisionBox")!;
        var confirm = preparation.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "Confirm session");
        var fields = preparation.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(view => AutomationProperties.GetName(view) == "Session preparation fields");
        Assert.DoesNotContain(confirm, fields.GetVisualDescendants());
        foreach (var field in new[] { serial, technician, part, revision })
        {
            var label = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(field));
            Assert.False(string.IsNullOrWhiteSpace(label.Text));
            Assert.True(label.IsEffectivelyVisible);
        }
        Type(window, serial, "UI-IDENTITY-SERIAL");
        Type(window, technician, "UI operator");
        Click(confirm);
        Assert.True(run.SessionPanel.SessionBlocked);
        Assert.True(part.IsFocused);
        Assert.False(string.IsNullOrWhiteSpace(run.SessionPanel.DutPartError));
        Assert.Equal(run.SessionPanel.DutPartError, AutomationProperties.GetHelpText(part));
        Assert.True(string.IsNullOrEmpty(run.SessionPanel.DutSerialError));
        Inside(part, window); Inside(confirm, window);
        Type(window, part, "PN-900-REVIEW");
        Assert.True(string.IsNullOrEmpty(run.SessionPanel.DutPartError));
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(part)));
        Click(confirm);
        Assert.True(run.SessionPanel.SessionBlocked);
        Assert.True(revision.IsFocused);
        Assert.False(string.IsNullOrWhiteSpace(run.SessionPanel.DutRevisionError));
        Assert.Equal(run.SessionPanel.DutRevisionError, AutomationProperties.GetHelpText(revision));
        Assert.True(string.IsNullOrEmpty(run.SessionPanel.DutSerialError));
        Assert.True(string.IsNullOrEmpty(run.SessionPanel.DutPartError));
        Inside(revision, window); Inside(confirm, window);
        Type(window, revision, "C");
        Assert.True(string.IsNullOrEmpty(run.SessionPanel.DutRevisionError));
        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(revision)));
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(run.SessionPanel.SessionBlocked);
        Assert.True(run.Workspace.ShowSteps);
        var nextRun = window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Run");
        Assert.True(nextRun.IsFocused);
        Assert.Equal("PN-900-REVIEW", run.SessionPanel.Session.DutPartNumber);
        Assert.Equal("C", run.SessionPanel.Session.DutRevision);
        Assert.False(run.IsRunning);
        Assert.Equal(planBytes, File.ReadAllBytes(fixture.PlanPath));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(fixture.SidecarPath));
    }

    [AvaloniaFact]
    public async Task Long_required_prompt_retains_invalid_input_and_keeps_Continue_outside_its_scroller()
    {
        Assert.True(E2EHarness.RequireApp().SettingsStore.AppSettings.UseMockVisa);
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new OperatorReviewPlanFixture();
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = 20;
        var main = shown.Main;
        var run = main.RunTest;
        main.NavigateToPageId("RunTest");
        await fixture.SelectAsync(run);
        run.SessionPanel.DutSerialInput = "UI-REQUIRED-SERIAL";
        run.SessionPanel.DutPartInput = "PN-427";
        run.SessionPanel.DutRevisionInput = "D";
        run.SessionPanel.OperatorInput = "UI operator";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        Assert.False(run.SessionPanel.SessionBlocked);
        var running = run.Run.RunCommand.ExecuteAsync();
        try
        {
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator && !run.Interaction.HasInteractionFields)
                    run.ContinueOperatorAttention();
                return run.Interaction.IsAwaitingOperator && run.Interaction.HasInteractionFields;
            }, TimeSpan.FromSeconds(30), "The persisted mock fixture did not reach its required-input step.");
            var interaction = window.GetVisualDescendants().OfType<InteractionHostView>()
                .Single(view => view.IsEffectivelyVisible);
            var body = interaction.FindControl<ScrollViewer>("PromptBodyScroller")!;
            var continueButton = interaction.FindControl<Button>("ContinueButton")!;
            Assert.DoesNotContain(continueButton, body.GetVisualDescendants());
            var text = Assert.Single(run.Interaction.InteractionFields, field => !field.IsNumber);
            var number = Assert.Single(run.Interaction.InteractionFields, field => field.IsNumber);
            text.Value = "FIXTURE-UI-42";
            number.Value = "invalid torque";
            var numberInput = interaction.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetName(control) == number.AutomationName);
            numberInput.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(numberInput.Focus());
            var waitingStep = run.CurrentStepPath;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(run.Interaction.IsAwaitingOperator);
            Assert.Equal(waitingStep, run.CurrentStepPath);
            Assert.Equal("FIXTURE-UI-42", text.Value);
            Assert.Equal("invalid torque", number.Value);
            Assert.Contains("number", run.Interaction.InteractionValidationError, StringComparison.OrdinalIgnoreCase);
            Inside(continueButton, window);
            Inside(interaction.FindControl<Button>("ShowThisStepButton")!, window);
            Assert.True(numberInput.IsFocused);
            // A later invalid field stays visible when a window is reduced after validation.
            window.Width = 1280; window.Height = 800;
            Dispatcher.UIThread.RunJobs();
            using (var wide = window.CaptureRenderedFrame()) Assert.NotNull(wide);
            Dispatcher.UIThread.RunJobs();
            Assert.True(numberInput.IsFocused);
            Inside(numberInput, window);
            window.Width = 900; window.Height = 600;
            Dispatcher.UIThread.RunJobs();
            using (var compact = window.CaptureRenderedFrame()) Assert.NotNull(compact);
            Dispatcher.UIThread.RunJobs();
            Assert.True(numberInput.IsFocused);
            Inside(numberInput, window);
            Inside(continueButton, window);
            Assert.Equal("invalid torque", number.Value);
            Assert.True(run.Interaction.IsAwaitingOperator);
        }
        finally
        {
            if (run.IsRunning)
            {
                await main.SafetyStopCommand.ExecuteAsync();
                await E2EHarness.WaitUntilAsync(() => !run.IsRunning && running.IsCompleted, TimeSpan.FromSeconds(60));
            }
            await running;
        }
    }

    private static void Type(Window window, TextBox field, string value)
    {
        field.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(field.Focus());
        field.SelectAll();
        window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, field.Text);
    }

    internal static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
        var owner = TopLevel.GetTopLevel(button);
        Assert.NotNull(owner);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner);
        Assert.NotNull(point);
        Assert.True(new Rect(owner.ClientSize).Contains(point.Value));
        owner.MouseDown(point.Value, MouseButton.Left);
        owner.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Inside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        var location = control.TranslatePoint(default, window);
        Assert.NotNull(location);
        var bounds = new Rect(location.Value, control.Bounds.Size);
        Assert.True(new Rect(window.ClientSize).Inflate(1).Contains(bounds),
            $"{control.Name ?? control.GetType().Name} {bounds} must fit {window.ClientSize}.");
        foreach (var viewport in control.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            var point = control.TranslatePoint(default, viewport);
            Assert.NotNull(point);
            Assert.True(new Rect(viewport.Bounds.Size).Inflate(1).Contains(new Rect(point.Value, control.Bounds.Size)),
                $"{control.Name ?? control.GetType().Name} must fit the visible viewport {viewport.Bounds.Size}.");
        }
    }
}
