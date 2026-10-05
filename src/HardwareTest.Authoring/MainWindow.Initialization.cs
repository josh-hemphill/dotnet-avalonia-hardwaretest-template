using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private PlanInitializationWindow? _initialization;

    private async Task ShowPlanInitializationAsync()
    {
        if (!_viewModel.CanInitializePlan) return;
        if (_initialization is not null) { _initialization.Activate(); return; }
        var current = OwnerContext();
        CommitFocusedEditor();
        _initialization = new PlanInitializationWindow(_viewModel, ownerIsCurrent: current);
        try
        {
            if (await _initialization.ShowDialog<bool>(this) && current()) WorkspaceTabs.SelectedIndex = 0;
        }
        finally { _initialization = null; }
    }

}
