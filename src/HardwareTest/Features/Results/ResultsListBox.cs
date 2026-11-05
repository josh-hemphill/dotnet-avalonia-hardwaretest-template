using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HardwareTest.Features.Results;

/// <summary>Keeps native saved-run discovery in sync with realized rows.</summary>
public sealed class ResultsListBox : ListBox
{
    private bool _refreshQueued;
    private bool _attached;

    public ResultsListBox()
    {
        AttachedToVisualTree += (_, _) => _attached = true;
        DetachedFromVisualTree += (_, _) => _attached = false;
        ContainerPrepared += (_, _) => QueueAutomationRefresh();
        ContainerClearing += (_, _) => QueueAutomationRefresh();
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new ResultsListBoxAutomationPeer(this);

    private void QueueAutomationRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        // AT-SPI exposes realized selection items as direct list children. Its
        // cached flattened list is not invalidated by descendant panel changes.
        // Wait until preparation/clearing and layout finish, then notify the
        // list peer once. Keep normal virtualization and container recycling.
        Dispatcher.Post(() =>
        {
            _refreshQueued = false;
            if (_attached && ControlAutomationPeer.FromElement(this) is ResultsListBoxAutomationPeer peer)
                peer.RefreshRealizedRows();
        }, DispatcherPriority.Loaded);
    }

    private sealed class ResultsListBoxAutomationPeer(ResultsListBox owner) : ListBoxAutomationPeer(owner)
    {
        public void RefreshRealizedRows() => InvalidateChildren();
    }
}
