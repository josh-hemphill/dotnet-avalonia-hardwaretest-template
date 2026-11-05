using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private PlanInitializationWindow? _initialization;
    private GuidedFormState? _directInitializationForm;
    private Guid _directInitializationSession;
    private QuickPlanCreationWindow? _quickInitialization;
    private QuickPlanForm? _quickPlanForm;
    private Guid _quickPlanSession;

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
        if (_quickInitialization is not null) { _quickInitialization.Activate(); return; }
        if (_directInitializationForm is not null && _directInitializationSession == _viewModel.WorkspaceSessionId)
        {
            await ShowAdvancedPlanInitializationAsync();
            return;
        }
        var current = OwnerContext();
        var returnFocus = FocusManager?.GetFocusedElement() as Control;
        CommitFocusedEditor();
        if (_quickPlanSession != _viewModel.WorkspaceSessionId) _quickPlanForm = null;
        _quickPlanSession = _viewModel.WorkspaceSessionId;
        var dialog = new QuickPlanCreationWindow(_viewModel, current, _quickPlanForm) { FontSize = FontSize };
        _quickInitialization = dialog;
        bool advanced = false;
        try
        {
            var created = await dialog.ShowDialog<bool>(this);
            if (!current()) return;
            _quickPlanForm = created ? null : dialog.CaptureForm();
            advanced = dialog.AdvancedRequested;
            if (created)
            {
                WorkspaceTabs.SelectedIndex = 0;
                Dispatcher.UIThread.Post(() =>
                {
                    if (current()) this.GetVisualDescendants().OfType<SequenceEditorView>().SingleOrDefault()?.FocusAdd();
                }, DispatcherPriority.Loaded);
            }
            else if (!advanced)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!current()) return;
                    var target = returnFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                        ? returnFocus : this.FindControl<Button>("PlanActionsButton");
                    target?.Focus();
                }, DispatcherPriority.Loaded);
            }
        }
        finally { _quickInitialization = null; }
        if (advanced && current()) await ShowAdvancedPlanInitializationAsync(_quickPlanForm);
    }

    private async Task ShowAdvancedPlanInitializationAsync(QuickPlanForm? identity = null)
    {
        if (!_viewModel.CanInitializePlan) return;
        if (_initialization is not null) { _initialization.Activate(); return; }
        var current = OwnerContext();
        CommitFocusedEditor();
        if (_directInitializationSession != _viewModel.WorkspaceSessionId) _directInitializationForm = null;
        _directInitializationSession = _viewModel.WorkspaceSessionId;
        _initialization = new PlanInitializationWindow(_viewModel, retained: _directInitializationForm, ownerIsCurrent: current)
        { FontSize = FontSize, Height = Math.Min(680, ClientSize.Height - 40) };
        if (identity is not null) _initialization.PrefillIdentity(identity.Name, identity.Id, identity.Family);
        try
        {
            var created = await _initialization.ShowDialog<bool>(this);
            if (!current()) return;
            _directInitializationForm = !created && _initialization.EnvironmentRequested ? _initialization.CaptureGuidedForm() : null;
            if (created) { _quickPlanForm = null; WorkspaceTabs.SelectedIndex = 0; }
            else if (_initialization.EnvironmentRequested) WorkspaceTabs.SelectedIndex = 3;
        }
        finally { _initialization = null; }
    }

}
