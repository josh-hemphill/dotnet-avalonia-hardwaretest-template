namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private bool _destructiveInFlight;
    private bool _ownerClosed;

    public async Task<bool> ConfirmCatalogDeletionAsync(CatalogDeletionKind kind, string target)
    {
        if (!BeginDestructiveRequest()) return false;
        try
        {
            CommitFocusedEditor();
            var workspace = _viewModel.Workspace;
            var selectedId = _viewModel.SelectedProgram?.PlanId;
            var impact = _viewModel.PrepareCatalogDeletion(kind, target);
            if (!await new AuthoringDestructiveInteraction(this).ConfirmCatalogDeletionAsync(impact)) return false;
            if (!CurrentDestructiveOwner(workspace, selectedId)) return false;
            _viewModel.ApplyCatalogDeletion(impact);
            return true;
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _destructiveInFlight = false; }
    }

    public async Task<bool> ConfirmInstrumentRemovalAsync()
    {
        if (!BeginDestructiveRequest()) return false;
        try
        {
            CommitFocusedEditor();
            var workspace = _viewModel.Workspace;
            var selectedId = _viewModel.SelectedProgram?.PlanId;
            var impact = _viewModel.PrepareSelectedInstrumentRemoval();
            var replacement = await new AuthoringDestructiveInteraction(this).ChooseInstrumentReplacementAsync(impact);
            if (replacement is null || !CurrentDestructiveOwner(workspace, selectedId)) return false;
            _viewModel.ApplyInstrumentRemoval(impact, replacement);
            return true;
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _destructiveInFlight = false; }
    }

    public async Task<bool> ConfirmHardwareEditAsync()
    {
        if (!BeginDestructiveRequest()) return false;
        try
        {
            CommitFocusedEditor();
            var workspace = _viewModel.Workspace;
            var impact = _viewModel.PrepareHardwareEdit();
            if (!await new AuthoringDestructiveInteraction(this).ConfirmHardwareEditAsync(impact)) return false;
            if (!CurrentDestructiveOwner(workspace, impact.PlanId)) return false;
            _viewModel.ApplyHardwareEdit(impact);
            return true;
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _destructiveInFlight = false; }
    }

    public async Task<bool> ConfirmHardwareDefinitionRemovalAsync()
    {
        if (!BeginDestructiveRequest()) return false;
        try
        {
            CommitFocusedEditor();
            var workspace = _viewModel.Workspace;
            var selectedId = _viewModel.SelectedProgram?.PlanId;
            var impact = _viewModel.PrepareHardwareDefinitionRemoval();
            if (!await new AuthoringDestructiveInteraction(this).ConfirmHardwareDefinitionRemovalAsync(impact)) return false;
            if (!CurrentDestructiveOwner(workspace, selectedId)) return false;
            _viewModel.ApplyHardwareDefinitionRemoval(impact);
            return true;
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _destructiveInFlight = false; }
    }

    private async Task<bool> ConfirmRemoveProgramAsync()
    {
        if (!BeginDestructiveRequest()) return false;
        try
        {
            CommitFocusedEditor();
            var workspace = _viewModel.Workspace;
            var impact = _viewModel.PrepareSelectedProgramRemoval();
            if (!await new AuthoringDestructiveInteraction(this).ConfirmProgramRemovalAsync(impact)) return false;
            if (!CurrentDestructiveOwner(workspace, impact.PlanId)) return false;
            return _viewModel.ApplyProgramRemoval(impact);
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _destructiveInFlight = false; }
    }

    private bool BeginDestructiveRequest()
    {
        if (_destructiveInFlight || _transitionInFlight || _ownerClosed || !IsVisible) return false;
        _destructiveInFlight = true;
        return true;
    }
    private bool CurrentDestructiveOwner(AuthoringWorkspace? workspace, string? selectedId)
    {
        if (_ownerClosed || !IsVisible || !ReferenceEquals(workspace, _viewModel.Workspace)
            || selectedId != _viewModel.SelectedProgram?.PlanId)
        {
            _viewModel.ReportError("Workspace or selection changed during review; review the operation again.");
            return false;
        }
        return true;
    }
}
