namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private bool _loadingBusy;
    private string? _loadingStage;

    public bool LoadingBusy { get => _loadingBusy; private set => SetField(ref _loadingBusy, value); }
    public string? LoadingStage { get => _loadingStage; private set => SetField(ref _loadingStage, value); }

    public async Task<bool> SelectProgramAsync(string planId, Func<bool>? ownerIsCurrent = null)
    {
        if (LoadingBusy || Workspace is not { } workspace ||
            string.Equals(SelectedProgram?.PlanId, planId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Programs.Any(program => string.Equals(program.PlanId, planId, StringComparison.OrdinalIgnoreCase))) return false;
        ownerIsCurrent ??= () => true;
        if (!ownerIsCurrent()) return false;
        var session = WorkspaceSessionId;
        LoadingStage = $"Loading test program {planId}…";
        LoadingBusy = true;
        try
        {
            // Recording files can be large. Read them before changing the selected draft,
            // then reuse the detached result while building the editor presentation.
            var datasets = await Task.Run(() => RunDatasetCatalog.List(workspace));
            if (!ownerIsCurrent() || session != WorkspaceSessionId) return false;
            _openingDatasets = datasets;
            try { SelectProgram(planId); }
            finally { _openingDatasets = null; }
            return true;
        }
        finally
        {
            LoadingBusy = false;
            LoadingStage = null;
        }
    }

    /// Prepare detached drafts away from the UI thread, then publish them on the caller's context.
    /// A failed load or an obsolete owner leaves the current workspace and its edits intact.
    public async Task<bool> OpenAsync(string root, bool discardUnsavedChanges = false, Func<bool>? ownerIsCurrent = null)
    {
        if (LoadingBusy) return false;
        if (HasUnsavedChanges && !discardUnsavedChanges)
            throw new AuthoringWorkspaceException("Save edited programs before opening a workspace, or explicitly discard them.");
        ownerIsCurrent ??= () => true;
        if (!ownerIsCurrent()) return false;
        LoadingStage = "Loading workspace and test programs…";
        LoadingBusy = true;
        try
        {
            var prepared = await Task.Run(() => PrepareOpen(root));
            if (!ownerIsCurrent()) return false;
            LoadingStage = "Preparing the test editor…";
            CommitOpen(prepared, discardUnsavedChanges);
            return true;
        }
        finally
        {
            LoadingBusy = false;
            LoadingStage = null;
        }
    }
}
