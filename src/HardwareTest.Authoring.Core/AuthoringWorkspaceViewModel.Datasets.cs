namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private sealed record RecordingChoice(Guid Session, string PlanId, string Path, string? RunId);
    private RecordingChoice? _selectedRecordingChoice;
    private bool _refreshingDatasets;

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
            var all = _openingDatasets ?? (Workspace is null ? [] : RunDatasetCatalog.List(Workspace));
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
