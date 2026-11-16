using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FluentAvalonia.UI.Controls;

namespace HardwareTest.Features.Shell;

/// The shell contains both navigation and a page, rather than being one selectable list.
public sealed class OperatorNavigationView : FANavigationView
{
    protected override Type StyleKeyOverride => typeof(FANavigationView);

    protected override AutomationPeer OnCreateAutomationPeer() => new WorkspacePeer(this);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("TogglePaneButton") is { } toggle)
        {
            AutomationProperties.SetName(toggle, "Toggle navigation");
            AutomationProperties.SetHelpText(toggle, "Show or hide the navigation labels.");
        }
    }

    private sealed class WorkspacePeer(OperatorNavigationView owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetNameCore() => "Operator workspace";
    }
}
