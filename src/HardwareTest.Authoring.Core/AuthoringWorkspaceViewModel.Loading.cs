namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private bool _loadingBusy;
    private string? _loadingStage;

    public bool LoadingBusy { get => _loadingBusy; private set => SetField(ref _loadingBusy, value); }
    public string? LoadingStage { get => _loadingStage; private set => SetField(ref _loadingStage, value); }

    public async Task<bool> SelectProgramAsync(string planId, Func<bool>? ownerIsCurrent = null)
    {
        if (LoadingBusy || Workspace is null ||
            string.Equals(SelectedProgram?.PlanId, planId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Programs.Any(program => string.Equals(program.PlanId, planId, StringComparison.OrdinalIgnoreCase))) return false;
        ownerIsCurrent ??= () => true;
        if (!ownerIsCurrent()) return false;
        var session = WorkspaceSessionId;
        LoadingStage = $"Loading test program {planId}…";
        LoadingBusy = true;
        try
        {
            // Yield so the loading state is rendered before publishing selection. Recordings
            // belong to the workspace cache; switching programs must not reread their files.
            await Task.Yield();
            var datasets = CachedWorkspaceDatasets;
            if (!ownerIsCurrent() || session != WorkspaceSessionId) return false;
            _openingDatasets = datasets;
            try { SelectProgram(planId); }
            finally { _openingDatasets = null; }
            await WaitForPreviewAsync();
            return ownerIsCurrent() && session == WorkspaceSessionId;
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
        var session = WorkspaceSessionId;
        var home = Prefs.OpenTapHomeOverride;
        LoadingStage = "Loading workspace and test programs…";
        LoadingBusy = true;
        try
        {
            var prepared = await Task.Run(() => PrepareOpen(root, home));
            if (!ownerIsCurrent() || session != WorkspaceSessionId || home != Prefs.OpenTapHomeOverride) return false;
            LoadingStage = "Preparing the test editor…";
            CommitOpen(prepared, discardUnsavedChanges);
            var openedSession = WorkspaceSessionId;
            do
            {
                await Task.WhenAll(WaitForPreviewAsync(), WaitForEnvironmentInspectionAsync(),
                    WaitForBuildInspectionAsync(), WaitForEditingIssuesAsync());
                if (openedSession != WorkspaceSessionId || _previewOwner?.Invoke() == false) return false;
                // Environment publication can invalidate preview and issue snapshots after
                // their previous idle tasks completed. Await the latest generation as well.
            } while (EnvironmentInspectionPending || BuildInspectionBusy || EditingIssuesBusy || PreviewBusy);
            // The original owner predicate may intentionally include the previous session.
            // Commit replaced it; desktop pipeline ownership now guards the new session.
            return openedSession == WorkspaceSessionId && _previewOwner?.Invoke() != false;
        }
        finally
        {
            LoadingBusy = false;
            LoadingStage = null;
        }
    }
}
