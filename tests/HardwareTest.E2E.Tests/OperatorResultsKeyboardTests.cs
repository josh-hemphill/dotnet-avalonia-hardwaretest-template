using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private static void Type(Window window, TextBox box, string value)
    {
        box.BringIntoView(); Dispatcher.UIThread.RunJobs();
        Assert.True(box.Focus()); box.SelectAll();
        window.KeyTextInput(value); Dispatcher.UIThread.RunJobs();
        Assert.Equal(value, box.Text);
    }
}
