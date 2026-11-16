using System.Collections.ObjectModel;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringSequenceRowDiscoveryTests
{
    [AvaloniaTheory]
    [InlineData(1280, 800, 14)]
    [InlineData(900, 600, 20)]
    public async Task Replaced_sequence_rows_are_named_before_focus_and_keyboard_focus_reaches_the_measurement(int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = font;
        Assert.Equal((double)width, window.ClientSize.Width);
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var list = fixture.Control<AuthoringSequenceListBox>("Program sequence");
        var peer = ControlAutomationPeer.CreatePeerForElement(list);
        // Prime the same flattened row cache used by native selection containers.
        _ = ListItems(peer).ToArray();
        var changes = 0;
        peer.ChildrenChanged += (_, _) => changes++;
        var add = fixture.Control<Button>("Add step");
        Assert.True(add.Focus());
        var vm = fixture.ViewModel;
        vm.InitializePlan(new("native-sequence-discovery")
        {
            StartingPoint = PlanStartingPoint.Empty,
            UseTemplateHardware = false,
            IncludeTemplateMeasurement = false,
            Instruments = [new("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::ROW-DISCOVERY")],
            IdentityInstrumentSlot = "DMM",
            IncludeSafeShutdown = true,
        });
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.MetricName = "Measured voltage";
        var measurement = vm.SequenceItems.Single(row => row.Kind == SequenceRowKind.Metric);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.IsSelectable && row.NodeId != measurement.NodeId));
        Settle(window);
        Assert.True(changes > 0, "Realized row changes must invalidate the native selection container.");
        Assert.True(add.Focus());
        list.ScrollIntoView(measurement);
        Settle(window);
        Assert.True(add.IsFocused);
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(measurement));
        Assert.False(row.IsSelected);
        Assert.False(row.IsFocused);
        var discovered = Assert.Single(ListItems(peer), candidate => candidate.GetName() == "Measured voltage");
        Assert.False(discovered.IsOffscreen());
        Assert.True(discovered.GetBoundingRectangle().Width > 0);
        Assert.True(discovered.GetBoundingRectangle().Height > 0);

        Assert.True(list.Focus());
        SendKey(Key.Home, PhysicalKey.Home);
        for (var attempt = 0; attempt < vm.SequenceItems.Count && vm.SelectedSequence?.NodeId != measurement.NodeId; attempt++)
            SendKey(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(measurement.NodeId, vm.SelectedSequence?.NodeId);
        row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(measurement));
        Assert.True(row.IsFocused);
        var focusedPeer = Assert.Single(ListItems(peer), candidate => candidate.GetName() == "Measured voltage");
        Assert.Same(row, Assert.IsAssignableFrom<ControlAutomationPeer>(focusedPeer).Owner);
        Assert.True(focusedPeer.HasKeyboardFocus());

        void SendKey(Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Settle(window);
        }
    }

    [AvaloniaFact]
    public void Peer_refresh_preserves_virtualization_and_selection_while_scrolling_and_replacing_rows()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 1000).Select(index => $"Sequence step {index:D4}"));
        var list = new AuthoringSequenceListBox { ItemsSource = items, Height = 180, Width = 400 };
        var window = new Window { Content = list, Width = 420, Height = 240 };
        try
        {
            window.Show();
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
            Assert.Null(list.SelectedItem);
            var beforeReplace = changes;
            items.Clear();
            items.Add("Replacement sequence step");
            Settle(window);
            Assert.True(changes > beforeReplace);
            Assert.Equal("Replacement sequence step", Assert.Single(ListItems(peer)).GetName());
            Assert.Null(list.SelectedItem);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<AutomationPeer> ListItems(AutomationPeer parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.GetAutomationControlType() == AutomationControlType.ListItem) yield return child;
            else foreach (var row in ListItems(child)) yield return row;
        }
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        // Default CI uses the headless drawing backend, which has no bitmap.
        // Advance rendering/layout in either backend, then require real pixels
        // only when the harness selected Skia for graphical capture.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") == "1")
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
