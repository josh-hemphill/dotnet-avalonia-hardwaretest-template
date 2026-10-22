namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private sealed record RecordingChoice(Guid Session, string PlanId, string Path, string? RunId);
    private RecordingChoice? _selectedRecordingChoice;
    private bool _refreshingDatasets;
    private Guid _workspaceDatasetsSession;
    private IReadOnlyList<RunDataset> _workspaceDatasets = [];

    internal IReadOnlyList<RunDataset> CachedWorkspaceDatasets
        => _workspaceDatasetsSession == WorkspaceSessionId ? _workspaceDatasets : [];

    private bool MatchesRecordingChoice(RunDataset dataset)
        => _selectedRecordingChoice is { } choice && choice.Session == _workspaceSession
            && string.Equals(choice.PlanId, SelectedProgram?.PlanId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(choice.PlanId, dataset.Run.PlanId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(choice.Path, dataset.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && string.Equals(choice.RunId, dataset.Run.RunId, StringComparison.OrdinalIgnoreCase);

    public void SelectDataset(int index)
    {
        if (_refreshingDatasets) return;
        var count = _datasets.Count;
        var clamped = count == 0 || index < 0 ? -1 : Math.Clamp(index, 0, count - 1);
        _selectedRecordingChoice = clamped < 0 ? null : new RecordingChoice(
            _workspaceSession, _datasets[clamped].Run.PlanId, _datasets[clamped].Path, _datasets[clamped].Run.RunId);
        if (!SetField(ref _selectedDatasetIndex, clamped, nameof(SelectedDatasetIndex)))
        {
            OnPropertyChanged(nameof(SelectedDataset));
            RaiseEditorProperties();
            return;
        }

        OnPropertyChanged(nameof(SelectedDataset));
        RaiseEditorProperties();
    }

    private void RefreshDatasets()
    {
        _refreshingDatasets = true;
        try
        {
            var all = _openingDatasets ?? (_previewDispatch is not null ? CachedWorkspaceDatasets
                : Workspace is null ? [] : RunDatasetCatalog.List(Workspace));
            _workspaceDatasets = all;
            _workspaceDatasetsSession = WorkspaceSessionId;
            var planId = SelectedProgram?.PlanId;
            _datasets = string.IsNullOrWhiteSpace(planId)
                ? []
                : all.Where(dataset =>
                        string.Equals(dataset.Run.PlanId, planId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            _datasetItems = _datasets.Select(FormatDataset).ToArray();
            _selectedDatasetIndex = _datasets.ToList().FindIndex(MatchesRecordingChoice);
            if (_selectedDatasetIndex < 0) _selectedRecordingChoice = null;
            OnPropertyChanged(nameof(Datasets));
            OnPropertyChanged(nameof(DatasetItems));
            OnPropertyChanged(nameof(SelectedDatasetIndex));
            OnPropertyChanged(nameof(SelectedDataset));
        }
        finally { _refreshingDatasets = false; }
    }

    /// Explicitly reload external recording changes; ordinary selection and edits use
    /// the validated workspace cache instead of traversing and parsing every run.json.
    public async Task<bool> RefreshRecordingsAsync(Func<bool>? ownerIsCurrent = null)
    {
        if (LoadingBusy || Workspace is not { } workspace) return false;
        ownerIsCurrent ??= () => true;
        if (!ownerIsCurrent()) return false;
        var session = WorkspaceSessionId;
        var descriptor = RecordingWorkspaceDescriptor(workspace);
        LoadingStage = "Refreshing recordings…";
        LoadingBusy = true;
        try
        {
            var recordings = await Task.Run(() => RunDatasetCatalog.List(descriptor));
            if (!ownerIsCurrent() || session != WorkspaceSessionId) return false;
            _openingDatasets = recordings;
            try { RefreshDatasets(); }
            finally { _openingDatasets = null; }
            RaiseEditorProperties();
            return true;
        }
        finally { LoadingBusy = false; LoadingStage = null; }
    }

    private static AuthoringWorkspace RecordingWorkspaceDescriptor(AuthoringWorkspace workspace)
        => new(workspace.Root, new AuthoringManifest { RecordingsDirectory = workspace.Manifest.RecordingsDirectory }, []);

    private void AcceptImportedRecording(RunDataset imported)
    {
        _workspaceDatasets = CachedWorkspaceDatasets.Append(imported)
            .OrderBy(dataset => dataset.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        _workspaceDatasetsSession = WorkspaceSessionId;
        _openingDatasets = _workspaceDatasets;
        try { RefreshDatasets(); }
        finally { _openingDatasets = null; }
        SelectDataset(_datasets.ToList().FindIndex(dataset => dataset.Path == imported.Path));
    }

    private static string FormatDataset(RunDataset dataset)
    {
        var id = string.IsNullOrWhiteSpace(dataset.Run.RunId)
            ? Path.GetFileName(Path.GetDirectoryName(dataset.Path)) ?? "run"
            : dataset.Run.RunId;
        return string.IsNullOrWhiteSpace(dataset.Run.DutSerial)
            ? id
            : $"{id} ({dataset.Run.DutSerial})";
    }

}
