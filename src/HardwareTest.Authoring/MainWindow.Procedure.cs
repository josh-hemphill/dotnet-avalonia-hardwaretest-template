using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private async void OnEditProcedure(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProgram is not { } draft) return;
        try
        {
            var session = _viewModel.OpenProcedureEditor();
            var window = new ProcedureEditorWindow(session, draft.Sidecar.DisplayName ?? draft.PlanId,
                _viewModel.SelectedDataset, _viewModel.CanEditProgramSettings);
            if (await window.ShowDialog<ProgramDraft?>(this) is { } edited)
                _viewModel.ApplyProcedureEdit(draft, edited);
        }
        catch (Exception error)
        {
            _viewModel.ReportError(AuthoringWorkspaceViewModel.PersistenceError(error));
        }
    }
}
