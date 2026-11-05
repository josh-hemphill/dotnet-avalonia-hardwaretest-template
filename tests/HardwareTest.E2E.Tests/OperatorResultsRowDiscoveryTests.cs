using System.Collections.ObjectModel;
using Avalonia.Automation.Peers;
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
public sealed class OperatorResultsRowDiscoveryTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Filtered_unselected_rows_notify_the_list_peer_and_expose_current_identity_before_focus(int font)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = 1280; window.Height = 800; window.FontSize = font;
        var main = shown.Main;
        main.IsNavPaneOpen = false;
        var prefix = "native-discovery-" + Guid.NewGuid().ToString("N");
        var records = new[]
        {
            Record(prefix + "-full", "FULL-DUT", RunExecutionScopes.FullSuite),
            Record(prefix + "-selected", "SELECTED-DUT", RunExecutionScopes.SelectedStep),
            Record(prefix + "-legacy", "LEGACY-DUT", null),
        };
        var store = E2EHarness.RequireApp().Services.GetRequiredService<IRunStore>();
        foreach (var record in records) await store.SaveAsync(record);
        main.NavigateToPageId("Results");
        var vm = main.Results;
        await vm.CloseDetailCommand.ExecuteAsync();
        await vm.ClearFiltersCommand.ExecuteAsync();
        await vm.RefreshCommand.ExecuteAsync();
        Settle(window);
        var view = window.GetVisualDescendants().OfType<ResultsView>().Single(control => control.IsEffectivelyVisible);
        var search = view.FindControl<TextBox>("ResultsSearchBox")!;
        var list = view.FindControl<ResultsListBox>("RunResultsList")!;
        var listPeer = ControlAutomationPeer.CreatePeerForElement(list);
        // Native selection containers flatten this tree and cache its rows.
        _ = ListItems(listPeer).ToArray();
        var changes = 0;
        listPeer.ChildrenChanged += (_, _) => changes++;
        Type(records[0].RunId);
        Assert.True(list.Focus());
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        await E2EHarness.WaitUntilAsync(() => vm.ShowDetail && vm.OpenedRun?.RunId == records[0].RunId);
        Settle(window);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        var beforeFilter = changes;
        Type(records[2].RunId);
        Assert.True(changes > beforeFilter, "Rebuilt realized rows must invalidate the native selection container.");
        Assert.Null(vm.SelectedRun);
        AssertDiscoverable(records[2]);
        var beforeClear = changes;
        Type(prefix);
        Assert.True(changes > beforeClear);
        Assert.Equal(3, vm.Runs.Count);
        foreach (var record in records) AssertDiscoverable(record);
        Assert.False(main.RunTest.IsRunning);

        void Type(string text)
        {
            Assert.True(search.Focus()); search.SelectAll();
            window.KeyTextInput(text);
            Settle(window);
            Assert.Equal(text, vm.SearchText);
            Assert.False(vm.ShowDetail);
            Assert.True(search.IsFocused);
        }

        void AssertDiscoverable(TestRunRecord record)
        {
            Assert.True(search.IsFocused);
            var summary = Assert.Single(vm.Runs, run => run.RunId == record.RunId);
            // At enlarged text not all three records fit at once. Scrolling
            // must expose each peer without selecting or focusing its row.
            list.ScrollIntoView(summary);
            Settle(window);
            Assert.True(search.IsFocused);
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(summary));
            Assert.False(row.IsSelected);
            Assert.False(row.IsFocused);
            var expected = $"{summary.PlanName}, DUT {summary.DutSerial}, {summary.Result}, {summary.ExecutionScopeText}";
            var peer = Assert.Single(ListItems(listPeer), candidate => candidate.GetName() == expected);
            Assert.False(peer.IsOffscreen());
            Assert.True(peer.GetBoundingRectangle().Width > 0);
            Assert.True(peer.GetBoundingRectangle().Height > 0);
        }
    }

    [AvaloniaFact]
    public void Row_refresh_preserves_bounded_virtualization_while_scrolling_and_replacing_items()
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        var items = new ObservableCollection<string>(Enumerable.Range(0, 1000).Select(i => $"Saved run {i:D4}"));
        var list = new ResultsListBox { ItemsSource = items, Height = 180, Width = 400 };
        window.Content = list;
        Settle(window);
        var peer = ControlAutomationPeer.CreatePeerForElement(list);
        Assert.Single(ListItems(peer), item => item.GetName() == items[0]);
        var changes = 0;
        peer.ChildrenChanged += (_, _) => changes++;
        list.ScrollIntoView(items[^1]);
        Settle(window);
        Assert.True(changes > 0);
        Assert.Single(ListItems(peer), item => item.GetName() == items[^1]);
        Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
        Assert.InRange(list.GetRealizedContainers().Count(), 1, 30);
        var beforeReplace = changes;
        items.Clear(); items.Add("Replacement saved run");
        Settle(window);
        Assert.True(changes > beforeReplace);
        Assert.Equal("Replacement saved run", Assert.Single(ListItems(peer)).GetName());
        Assert.Null(list.SelectedItem);
    }

    private static IEnumerable<AutomationPeer> ListItems(AutomationPeer parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.GetAutomationControlType() == AutomationControlType.ListItem) yield return child;
            else foreach (var row in ListItems(child)) yield return row;
        }
    }

    private static TestRunRecord Record(string id, string dut, string? scope) => new()
    {
        RunId = id,
        PlanId = "row-discovery",
        PlanName = "Saved-run discovery acceptance",
        DutSerial = dut,
        ExecutionScope = scope,
        SelectedStepPath = scope == RunExecutionScopes.SelectedStep ? "Program/Identity" : null,
        OperatorName = "Quality reviewer",
        StartedAt = DateTimeOffset.UtcNow,
        Result = RunResult.Passed,
    };

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame);
        Dispatcher.UIThread.RunJobs();
    }
}
