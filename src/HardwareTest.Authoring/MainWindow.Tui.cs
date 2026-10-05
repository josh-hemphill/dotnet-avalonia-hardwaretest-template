namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private bool _tuiInFlight;
    private string? SelectedTuiPlan => _viewModel.Workspace is { } workspace && _viewModel.SelectedProgram is { } program
        ? workspace.TapPlanPaths.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), program.PlanId, StringComparison.OrdinalIgnoreCase)) : null;
    private string? TuiBlocker()
    {
        if (_tuiInFlight) return "The external TUI is already open.";
        if (WorkspaceBlocker() is { } workspace) return workspace;
        if (_viewModel.CompiledConflictProgramIds.Count > 0 || _viewModel.HasUncompiledSources) return "Reconcile external edits and compile the saved source before opening the TUI. Unsaved drafts will remain in the editor.";
        return AuthoringExternalTuiLauncher.Prerequisite(_viewModel.AuthoringHomeText, SelectedTuiPlan);
    }
    public async Task RefreshAfterExternalTuiAsync(Func<Task> externalProcess)
    {
        var current = OwnerContext();
        try
        {
            await externalProcess();
            if (current()) _viewModel.RefreshExternalCompiledChanges();
        }
        catch (Exception error) { if (current()) _viewModel.ReportError(error.Message); }
    }
    private async Task LaunchTuiAsync()
    {
        var current = OwnerContext();
        var plan = SelectedTuiPlan ?? throw new AuthoringWorkspaceException("Select a compiled plan.");
        _tuiInFlight = true;
        try
        {
            await RefreshAfterExternalTuiAsync(async () =>
            {
                var exit = await new AuthoringExternalTuiLauncher().LaunchAsync(_viewModel.AuthoringHomeText, plan);
                if (exit != 0 && current()) _viewModel.ReportError($"External TUI exited with code {exit}; inspect its terminal output.");
            });
        }
        finally { _tuiInFlight = false; }
    }
}
