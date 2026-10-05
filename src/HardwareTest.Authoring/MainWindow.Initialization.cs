using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private PlanInitializationWindow? _initialization;
    private Window? _commandPalette;

    private async Task ShowPlanInitializationAsync()
    {
        if (!_viewModel.CanInitializePlan) return;
        if (_initialization is not null) { _initialization.Activate(); return; }
        CommitFocusedEditor();
        _initialization = new PlanInitializationWindow(_viewModel);
        try
        {
            if (await _initialization.ShowDialog<bool>(this)) WorkspaceTabs.SelectedIndex = 0;
        }
        finally { _initialization = null; }
    }

    internal async void OnOpenCommandPalette(object? sender, RoutedEventArgs e)
    {
        if (_commandPalette is not null) { _commandPalette.Activate(); return; }
        var command = new Button { Content = "New test plan", IsEnabled = _viewModel.CanInitializePlan };
        AutomationProperties.SetName(command, "New test plan command");
        var createWorkspace = new Button { Content = "Create workspace", IsEnabled = !_viewModel.OperationBusy && !_viewModel.OperationCleanupPending };
        AutomationProperties.SetName(createWorkspace, "Create workspace command");
        var requestedWorkspace = false;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window
        {
            Title = "Commands",
            Width = 400,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { command, createWorkspace, cancel } }
        };
        createWorkspace.Click += (_, _) => { requestedWorkspace = true; dialog.Close(true); };
        command.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        _commandPalette = dialog;
        bool selected;
        try { selected = await dialog.ShowDialog<bool>(this); }
        finally { _commandPalette = null; }
        if (selected && requestedWorkspace) await ShowWorkspaceCreationAsync();
        else if (selected) await ShowPlanInitializationAsync();
    }

    private void InitializePlanCommands()
    {
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.P || e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift)) return;
            OnOpenCommandPalette(this, new RoutedEventArgs()); e.Handled = true;
        };
    }
}
