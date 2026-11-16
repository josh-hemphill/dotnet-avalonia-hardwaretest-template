using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HardwareTest.Authoring;

/// Keeps the native selection container synchronized with realized sequence rows.
public sealed class AuthoringSequenceListBox : ListBox
{
    private bool _refreshQueued;
    private bool _attached;

    public AuthoringSequenceListBox()
    {
        AttachedToVisualTree += (_, _) => { _attached = true; QueueAutomationRefresh(); };
        DetachedFromVisualTree += (_, _) => _attached = false;
        ContainerPrepared += (_, _) => QueueAutomationRefresh();
        ContainerClearing += (_, _) => QueueAutomationRefresh();
    }

    protected override Type StyleKeyOverride => typeof(ListBox);
    protected override AutomationPeer OnCreateAutomationPeer() => new SequenceListAutomationPeer(this);

    private void QueueAutomationRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        // Native selection containers cache flattened rows. Descendant panel
        // changes alone do not invalidate that list. Notify after layout once,
        // keeping normal virtualization, container recycling and selection.
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (_attached && ControlAutomationPeer.FromElement(this) is SequenceListAutomationPeer peer)
                peer.RefreshRealizedRows();
        }, DispatcherPriority.Loaded);
    }

    private sealed class SequenceListAutomationPeer(AuthoringSequenceListBox owner) : ListBoxAutomationPeer(owner)
    {
        public void RefreshRealizedRows() => InvalidateChildren();
    }
}
