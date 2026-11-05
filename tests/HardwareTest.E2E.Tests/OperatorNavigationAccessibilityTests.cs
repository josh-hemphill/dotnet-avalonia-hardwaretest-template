using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAvalonia.UI.Controls;
using HardwareTest.Features.Shell;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorNavigationAccessibilityTests
{
    [AvaloniaTheory]
    [InlineData(1280, 800, 14)]
    [InlineData(900, 600, 20)]
    public async Task Composite_navigation_exposes_navigation_page_and_footer_without_owning_their_selection(int width, int height, int font)
    {
        E2EHarness.RequireApp();
        var pageAction = new Button { Content = "Review current DUT" };
        var footerAction = new Button { Content = "Stop current run" };
        var first = new FANavigationViewItem { Content = "Home" };
        var second = new FANavigationViewItem { Content = "Run" };
        var navigation = new OperatorNavigationView
        {
            Content = pageAction,
            PaneFooter = footerAction,
            PaneDisplayMode = FANavigationViewPaneDisplayMode.Left,
            IsPaneOpen = true,
        };
        navigation.MenuItems.Add(first);
        navigation.MenuItems.Add(second);
        navigation.SelectedItem = second;
        var window = new Window { Width = width, Height = height, FontSize = font, Content = navigation };
        try
        {
            window.Show();
            await E2EHarness.PumpUiAsync();
            var peer = ControlAutomationPeer.CreatePeerForElement(navigation);
            Assert.Equal(AutomationControlType.Pane, peer.GetAutomationControlType());
            Assert.Equal("Operator workspace", peer.GetName());
            // AT-SPI flattens selection-container children. A composite shell must not
            // advertise that pattern and hide its page or footer behind navigation items.
            Assert.Null(peer.GetProvider<ISelectionProvider>());
            var peers = Descendants(peer).OfType<ControlAutomationPeer>().ToArray();
            Assert.Contains(peers, child => ReferenceEquals(child.Owner, pageAction));
            Assert.Contains(peers, child => ReferenceEquals(child.Owner, footerAction));
            Assert.Contains(peers, child => ReferenceEquals(child.Owner, first));
            Assert.Contains(peers, child => ReferenceEquals(child.Owner, second));
            Assert.Contains(peers, child => child.Owner is Button && child.GetName() == "Toggle navigation");
            Assert.Same(second, navigation.SelectedItem);
            navigation.SelectedItem = first;
            await E2EHarness.PumpUiAsync();
            Assert.Same(first, navigation.SelectedItem);
            Assert.Contains(Descendants(peer).OfType<ControlAutomationPeer>(), child => ReferenceEquals(child.Owner, pageAction));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer root)
    {
        var seen = new HashSet<AutomationPeer>();
        var pending = new Stack<AutomationPeer>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var child in current.GetChildren()) pending.Push(child);
        }
    }
}
