using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorResultsFilterContinuityTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Closing_a_saved_run_then_clearing_and_refining_search_keeps_focus_until_keyboard_activation(
        bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = width; window.Height = height; window.FontSize = font;
        var main = shown.Main;
        main.IsNavPaneOpen = width >= 1280;
        main.NavigateToPageId("Results");
        var vm = main.Results;
        var view = Assert.Single(window.GetVisualDescendants().OfType<ResultsView>(), control => control.IsEffectivelyVisible);
        var search = view.FindControl<TextBox>("ResultsSearchBox")!;
        var list = view.FindControl<ListBox>("RunResultsList")!;
        var prefix = "filter-journey-" + Guid.NewGuid().ToString("N");
        var first = Record(prefix + "-first", "FILTER-DUT-FIRST", DateTimeOffset.UtcNow);
        var second = Record(prefix + "-second", "FILTER-DUT-SECOND", first.StartedAt.AddMinutes(-1));
        var store = app.Services.GetRequiredService<IRunStore>();
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        var firstPath = Path.Combine(store.GetRunDirectory(first.RunId), "run.json");
        var secondPath = Path.Combine(store.GetRunDirectory(second.RunId), "run.json");
        var firstBytes = File.ReadAllBytes(firstPath);
        var secondBytes = File.ReadAllBytes(secondPath);
        var originalTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            await vm.CloseDetailCommand.ExecuteAsync();
            await vm.ClearFiltersCommand.ExecuteAsync();
            await vm.RefreshCommand.ExecuteAsync();
            Settle(window);
            search.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(search.Focus());
            ClearWithKeyboard(window);
            window.KeyTextInput(first.RunId);
            Settle(window);
            Assert.Equal(first.RunId, Assert.Single(vm.Runs).RunId);
            var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0));
            OperatorWorkflowUiTests.Inside(row, window);
            var center = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left);
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == first.RunId);
            Settle(window);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(window);
            Assert.False(vm.ShowDetail);
            Assert.Equal(first.RunId, vm.SelectedRun?.RunId);
            Assert.True(search.Focus());
            ClearWithKeyboard(window);
            Settle(window);
            Assert.Equal(string.Empty, vm.SearchText);
            AssertSearchStillActive();
            Assert.Equal(first.RunId, vm.SelectedRun?.RunId);
            Capture(window, "quality-results-search-cleared", dark, width, height, font);

            foreach (var character in second.RunId)
            {
                window.KeyTextInput(character.ToString());
                Dispatcher.UIThread.RunJobs();
                AssertSearchStillActive();
            }
            Settle(window);
            AssertSearchStillActive();
            Assert.Equal(second.RunId, Assert.Single(vm.Runs).RunId);
            Assert.Null(vm.SelectedRun);
            Capture(window, "quality-results-search-refined", dark, width, height, font);

            // Reach the list through actual Tab navigation, then let normal row
            // selection/Enter open it. No SelectedItem assignment or forced row focus.
            for (var attempts = 0; attempts < 12 && !vm.ShowDetail && !FocusIsWithin(list); attempts++)
            {
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
            }
            if (vm.ShowDetail)
            {
                // Some platform tab policies select the first row on entry. That
                // is an explicit user navigation, and must target this filtered run.
                Assert.Equal(second.RunId, vm.OpenedRun?.RunId);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Settle(window);
            }
            Assert.True(FocusIsWithin(list), "Tab navigation must reach the saved-run list from search.");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            if (!vm.ShowDetail) window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == second.RunId);
            Settle(window);
            Assert.Equal(second.DutSerial, vm.OpenedRun!.DutSerial);
            Capture(window, "quality-results-filtered-record", dark, width, height, font);

            // Escape restores natural selected-row focus; Enter reopens that same row.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(window);
            Assert.False(vm.ShowDetail);
            Assert.True(FocusIsWithin(list));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == second.RunId);
            Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
            Assert.Equal(secondBytes, File.ReadAllBytes(secondPath));
            Assert.False(main.RunTest.IsRunning);

            void AssertSearchStillActive()
            {
                Assert.False(vm.ShowDetail);
                Assert.True(search.IsEffectivelyVisible);
                Assert.True(search.IsFocused);
            }

            bool FocusIsWithin(Control control)
            {
                var focused = window.FocusManager!.GetFocusedElement() as Control;
                return ReferenceEquals(focused, control) || focused?.GetVisualAncestors().Contains(control) == true;
            }
        }
        finally { app.RequestedThemeVariant = originalTheme; }
    }

    private static TestRunRecord Record(string id, string dut, DateTimeOffset started)
        => new()
        {
            RunId = id,
            PlanId = "filter-continuity",
            PlanName = "Filter continuity acceptance",
            ExecutionScope = RunExecutionScopes.FullSuite,
            DutSerial = dut,
            OperatorName = "Evidence reviewer",
            StartedAt = started,
            CompletedAt = started.AddSeconds(1),
            Result = RunResult.Passed,
        };

    private static void ClearWithKeyboard(Window window)
    {
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        Settle(window);
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")!;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
