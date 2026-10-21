using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private WorkspaceCreationWindow? _workspaceCreation;
    public string WorkspaceCreationError => _viewModel.Error ?? "Workspace creation was cancelled.";

    private async void OnCreateWorkspace(object? sender, RoutedEventArgs e)
        => await ShowWorkspaceCreationAsync();

    private async Task ShowWorkspaceCreationAsync()
    {
        if (_transitionInFlight || _destructiveInFlight || _ownerClosed || _viewModel.OperationBusy || _viewModel.OperationCleanupPending) return;
        if (_workspaceCreation is not null) { _workspaceCreation.Activate(); return; }
        var dialog = new WorkspaceCreationWindow(this);
        _workspaceCreation = dialog;
        bool created;
        try { created = await dialog.ShowDialog<bool>(this); }
        finally { _workspaceCreation = null; }
        if (created && !_ownerClosed && IsVisible && ReferenceEquals(DataContext, _viewModel) && dialog.CreatedWorkspaceSession == _viewModel.WorkspaceSessionId && dialog.ContinueToPlan) await ShowPlanInitializationAsync();
    }

    public async Task<bool> CreateWorkspaceAsync(WorkspaceCreationRequest request, CancellationToken cancellationToken = default)
    {
        if (_transitionInFlight || _destructiveInFlight || _ownerClosed || !IsVisible || _viewModel.OperationBusy || _viewModel.OperationCleanupPending) return false;
        _transitionInFlight = true;
        var current = OwnerContext();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalDocument = _viewModel.SelectedDocument;
            bool ContextIsCurrent() => current() && ReferenceEquals(originalDocument, _viewModel.SelectedDocument);
            // Validate and review conflicts before asking to leave the current document.
            var initializer = new AuthoringWorkspaceInitializer();
            _ = initializer.Preview(request);
            CommitFocusedEditor();
            var decision = await ChooseTransitionAsync(cancellationToken, ContextIsCurrent);
            if (decision == UnsavedChangesChoice.Cancel || !ContextIsCurrent()) return false;
            cancellationToken.ThrowIfCancellationRequested();
            var created = initializer.Create(request, cancellationToken);
            var prepared = _viewModel.PrepareOpen(created.Root);
            _viewModel.CommitOpen(prepared, discardUnsavedChanges: decision == UnsavedChangesChoice.Discard);
            if (_viewModel.Programs.FirstOrDefault() is { } program) _viewModel.SelectProgram(program.PlanId);
            WorkspaceTabs.SelectedIndex = 8;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception ex) { if (current()) _viewModel.ReportError(ex.Message); return false; }
        finally { _transitionInFlight = false; }
    }
}
