using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorResultsKeyboardTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Keyboard_search_and_ArrowDown_open_an_initially_unselected_saved_run(int font)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main;
        main.IsNavPaneOpen = false;
        var store = E2EHarness.RequireApp().Services.GetRequiredService<IRunStore>();
        var record = new TestRunRecord
        {
            RunId = "keyboard-" + Guid.NewGuid().ToString("N"),
            PlanId = "keyboard-review",
            PlanName = "Saved evidence keyboard review",
            DutSerial = "KEYBOARD-DUT",
            OperatorName = "Keyboard reviewer",
            SessionId = "keyboard-session",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed
        };
        await store.SaveAsync(record);
        var path = Path.Combine(store.GetRunDirectory(record.RunId), "run.json");
        var original = File.ReadAllBytes(path);
        main.NavigateToPageId("Results");
        var vm = main.Results;
        await vm.RefreshCommand.ExecuteAsync();
        await vm.CloseDetailCommand.ExecuteAsync();
        using (var layout = window.CaptureRenderedFrame()) Assert.NotNull(layout);
        var view = window.GetVisualDescendants().OfType<ResultsView>().Single(control => control.IsEffectivelyVisible);
        var search = view.FindControl<TextBox>("ResultsSearchBox")!;
        Type(window, search, "no-record-" + record.RunId);
        await E2EHarness.WaitUntilAsync(() => vm.Runs.Count == 0);
        Assert.Null(vm.SelectedRun);
        Type(window, search, record.RunId);
        await E2EHarness.WaitUntilAsync(() => vm.Runs.Count == 1);
        Assert.Equal(record.RunId, vm.Runs[0].RunId);
        Assert.Null(vm.SelectedRun);
        Assert.False(vm.ShowDetail);
        using (var layout = window.CaptureRenderedFrame()) Assert.NotNull(layout);
        Dispatcher.UIThread.RunJobs();

        var list = view.FindControl<ListBox>("RunResultsList")!;
        Assert.True(list.Focus());
        // Normal list navigation must select the first item when filtering has
        // removed the prior selection. No SelectedItem assignment or pointer input.
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        await E2EHarness.WaitUntilAsync(() => vm.SelectedRun?.RunId == record.RunId,
            TimeSpan.FromSeconds(3), "ArrowDown did not select the initially unselected filtered saved run.");
        if (!vm.ShowDetail)
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == record.RunId,
            TimeSpan.FromSeconds(10), "Keyboard selection did not open the exact filtered saved run.");
        Assert.Equal(record.DutSerial, vm.OpenedRun!.DutSerial);
        Assert.Equal(record.OperatorName, vm.OpenedRun.OperatorName);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(main.RunTest.IsRunning);
    }

    [AvaloniaFact]
    public async Task Escape_then_Enter_reopens_the_selected_run_while_its_comparison_is_loading()
    {
        var services = E2EHarness.RequireApp().Services;
        var store = services.GetRequiredService<IRunStore>();
        var record = new TestRunRecord
        {
            RunId = "keyboard-reopen-" + Guid.NewGuid().ToString("N"),
            PlanId = "keyboard-review",
            PlanName = "Saved evidence keyboard review",
            DutSerial = "KEYBOARD-DUT",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(record);

        var comparison = new PausingRunComparison();
        var vm = new ResultsViewModel(store, services.GetRequiredService<IReportService>(), comparison: comparison);
        var view = new ResultsView { DataContext = vm };
        var window = new Window { Width = 700, Height = 600, Content = view };
        var list = view.FindControl<ListBox>("RunResultsList")!;
        window.Show();
        try
        {
            await vm.OpenRunByIdAsync(record.RunId);
            vm.SearchText = record.RunId;
            await vm.CloseDetailCommand.ExecuteAsync();
            await WaitForListFocus();

            Press(Key.Enter, PhysicalKey.Enter);
            await comparison.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail
                && view.FindControl<Button>("ResultsCloseButton")!.IsFocused, TimeSpan.FromSeconds(5));

            Press(Key.Escape, PhysicalKey.Escape);
            await WaitForListFocus();
            Press(Key.Enter, PhysicalKey.Enter);
            comparison.Release.TrySetResult();
            await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == record.RunId,
                TimeSpan.FromSeconds(5), "Enter during the previous comparison did not reopen the selected run.");
        }
        finally
        {
            comparison.Release.TrySetResult();
            window.Close();
        }

        Task WaitForListFocus() => E2EHarness.WaitUntilAsync(() =>
        {
            var current = window.FocusManager!.GetFocusedElement() as Control;
            return !vm.ShowDetail && (ReferenceEquals(current, list) || current?.GetVisualAncestors().Contains(list) == true);
        }, TimeSpan.FromSeconds(5), "Closing Results did not restore focus within the saved-run list.");

        void Press(Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
        }
    }

    private sealed class PausingRunComparison : IRunComparisonService
    {
        private int _calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RunComparisonReport> CompareToPreviousAsync(TestRunRecord current, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return new RunComparisonReport { CurrentRunId = current.RunId };
        }

        public Task<RunComparisonReport> CompareAsync(string currentRunId, string previousRunId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static void Type(Window window, TextBox box, string value)
    {
        box.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(box.Focus()); box.SelectAll();
        window.KeyTextInput(value); Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, box.Text);
    }
}
