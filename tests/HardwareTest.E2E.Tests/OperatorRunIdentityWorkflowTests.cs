using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorRunIdentityWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14, RunResult.Passed)]
    [InlineData(true, 1280, 800, 14, RunResult.Failed)]
    [InlineData(false, 900, 600, 20, RunResult.Cancelled)]
    [InlineData(true, 900, 600, 20, RunResult.Failed)]
    public async Task Saved_run_verdict_and_full_identity_survive_keyboard_copy_close_and_natural_row_reopen(
        bool dark, int width, int height, int font, RunResult result)
    {
        var app = E2EHarness.RequireApp();
        var originalTheme = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var shown = E2EHarness.ShowMainWindow();
            var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
            var main = shown.Main; main.IsNavPaneOpen = width >= 1280;
            var store = app.Services.GetRequiredService<IRunStore>();
            var record = new TestRunRecord
            {
                RunId = "run-full-identity-" + Guid.NewGuid().ToString("N"),
                PlanId = "controller-production-test-v4",
                PlanName = "Controller acceptance",
                SessionId = "shift-session-full-" + Guid.NewGuid().ToString("N"),
                DutSerial = "DUT-IDENTITY-742",
                DutPartNumber = "PART-IDENTITY-27",
                DutRevision = "C",
                OperatorName = "Evidence review technician",
                StartedAt = new DateTimeOffset(2026, 11, 4, 10, 5, 3, TimeSpan.FromHours(-8)),
                CompletedAt = new DateTimeOffset(2026, 11, 4, 10, 7, 9, TimeSpan.FromHours(-8)),
                Result = result,
                ExecutionScope = RunExecutionScopes.SelectedStep,
                SelectedStepPath = "Controller acceptance/Diagnostic voltage",
            };
            await store.SaveAsync(record);
            var path = Path.Combine(store.GetRunDirectory(record.RunId), "run.json");
            var original = File.ReadAllBytes(path);
            main.NavigateToPageId("Results");
            var vm = main.Results;
            await vm.OpenRunByIdAsync(record.RunId);
            await E2EHarness.PumpUiAsync();
            using (var layout = window.CaptureRenderedFrame()) Assert.NotNull(layout);
            Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(window.GetVisualDescendants().OfType<ResultsView>(), control => control.IsEffectivelyVisible);
            var verdict = view.FindControl<TextBlock>("SavedRunVerdict")!;
            Assert.Equal($"Verdict: {result}", verdict.Text);
            Assert.Equal(verdict.Text, AutomationProperties.GetName(verdict));
            OperatorWorkflowUiTests.Inside(verdict, window);
            Assert.Contains($"({result})", vm.Status, StringComparison.Ordinal);
            var details = view.FindControl<Button>("RunIdentityButton")!;
            OperatorWorkflowUiTests.Inside(details, window);
            var suffix = $"{(dark ? "dark" : "light")}-{width}x{height}-font{font}";
            CaptureIfEnabled(window, $"results-persistent-verdict-{suffix}");
            Assert.True(details.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var full = view.FindControl<TextBox>("RunIdentityBox")!;
            await E2EHarness.WaitUntilAsync(() => full.IsEffectivelyVisible && full.IsFocused,
                TimeSpan.FromSeconds(5), "Keyboard Enter did not open and focus the full saved-run identity disclosure.");
            Assert.True(full.IsReadOnly);
            Assert.Equal(font, full.FontSize);
            foreach (var value in new[] { record.RunId, record.PlanId, record.PlanName, record.SessionId, record.DutSerial,
                         record.DutPartNumber, record.DutRevision, record.OperatorName, record.ExecutionScopeText,
                         record.StartedAt.ToString("O", CultureInfo.InvariantCulture), record.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture) })
                Assert.Contains(value!, full.Text!, StringComparison.Ordinal);
            var popup = TopLevel.GetTopLevel(full)!;
            CaptureIfEnabled(window, $"results-full-run-identity-{suffix}", popup, $"results-full-run-identity-popup-{suffix}");
            popup.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(full.Text, full.SelectedText);
            popup.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            await E2EHarness.PumpUiAsync();
            Assert.NotNull(window.Clipboard);
            Assert.Equal(full.Text, await window.Clipboard!.TryGetTextAsync());
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await E2EHarness.WaitUntilAsync(() => details.IsFocused);
            Assert.True(vm.ShowDetail);
            Assert.Equal(record.RunId, vm.OpenedRun!.RunId);
            // Closing the detail must restore the selected row naturally. Do not
            // focus the list or assign SelectedItem before testing Enter.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await E2EHarness.WaitUntilAsync(() => !vm.ShowDetail);
            var list = view.FindControl<ListBox>("RunResultsList")!;
            var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
            Assert.True(ReferenceEquals(focused, list) || focused.GetVisualAncestors().Contains(list));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == record.RunId,
                TimeSpan.FromSeconds(5), "Enter on the naturally focused saved row did not reopen its exact run.");
            Assert.Equal($"Verdict: {result}", verdict.Text);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.False(main.RunTest.IsRunning);
        }
        finally
        {
            app.RequestedThemeVariant = originalTheme;
        }
    }

    private static void CaptureIfEnabled(Window window, string name, TopLevel? popup = null, string? popupName = null)
    {
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_OPERATOR_UI") != "1") return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "operator-ui-review");
        Directory.CreateDirectory(directory);
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
}
