using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace HardwareTest.Authoring;

/// Retains page controls and stable route selection without presenting tab navigation.
public sealed class TaskPageHost : TabControl
{
    public TaskPageHost() => AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Pane);

    protected override AutomationPeer OnCreateAutomationPeer() => new TaskWorkspacePeer(this);

    private sealed class TaskWorkspacePeer(Control owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => "AuthoringTaskWorkspace";
    }
}
