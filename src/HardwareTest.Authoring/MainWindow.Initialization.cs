using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private PlanInitializationWindow? _initialization;
    private GuidedFormState? _directInitializationForm;
    private Guid _directInitializationSession;

    internal async void OnResumeInitialization(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_guidedForm is not null && _guidedSession == _viewModel.WorkspaceSessionId) await ShowGuidedInitializationAsync();
            else await ShowPlanInitializationAsync();
        }
        catch (Exception error) { _viewModel.ReportError(AuthoringWorkspaceViewModel.PersistenceError(error)); }
    }

    private async Task ShowPlanInitializationAsync()
    {
        if (!_viewModel.CanInitializePlan) return;
        if (_initialization is not null) { _initialization.Activate(); return; }
        var current = OwnerContext();
        CommitFocusedEditor();
        if (_directInitializationSession != _viewModel.WorkspaceSessionId) _directInitializationForm = null;
        _directInitializationSession = _viewModel.WorkspaceSessionId;
        _initialization = new PlanInitializationWindow(_viewModel, retained: _directInitializationForm, ownerIsCurrent: current) { FontSize = FontSize };
        try
        {
            var created = await _initialization.ShowDialog<bool>(this);
            if (!current()) return;
            _directInitializationForm = !created && _initialization.EnvironmentRequested ? _initialization.CaptureGuidedForm() : null;
            if (created) WorkspaceTabs.SelectedIndex = 0;
            else if (_initialization.EnvironmentRequested) WorkspaceTabs.SelectedIndex = 3;
        }
        finally { _initialization = null; }
    }

}
