namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public IReadOnlyList<BoardPreviewTile> BoardTiles => BoardPreviewBuilder.Build(SelectedProgram, SelectedDataset?.Run);
    public bool CanImportRecording => Workspace is { IsReadOnly: false } && SelectedProgram is not null && !OperationBusy;
    public string RecordingEmptyState => Datasets.Count == 0 ? "No recordings for this program. Import run.json, or inspect example data." : string.Empty;
    public string DataSourceDetails
    {
        get
        {
            if (SelectedDataset is not { } dataset) return "Example data · simulated values · no hardware execution";
            var run = dataset.Run;
            var time = run.Samples.Count > 0 && run.Samples.All(sample => sample.ElapsedMs is { } elapsed && double.IsFinite(elapsed)) ? "elapsed time available" : "elapsed time missing or incomplete";
            return $"Recording · {dataset.Path}\nRun {run.RunId} · DUT {(string.IsNullOrWhiteSpace(run.DutSerial) ? "unspecified" : run.DutSerial)} · {run.Samples.Count} samples · {time}\nChannels: {string.Join(", ", run.Samples.Select(sample => sample.EffectiveMetricKey).Distinct(StringComparer.OrdinalIgnoreCase))} · no hardware execution";
        }
    }

    public void UseExampleData() => SelectDataset(-1);

    public void ImportRecording(string path, string? destinationName = null)
        => ImportRecording(path, destinationName, beforeCopy: null);

    internal void ImportRecording(string path, string? destinationName, Action? beforeCopy)
    {
        EnsureWritableWorkspace("import a recording");
        if (!CanImportRecording) throw new AuthoringWorkspaceException("Select a program and finish the current operation before importing.");
        var imported = RunDatasetCatalog.Import(Workspace!, path, destinationName ?? Guid.NewGuid().ToString("N"), SelectedProgram!.PlanId, beforeCopy);
        RefreshDatasets();
        SelectDataset(_datasets.ToList().FindIndex(dataset => dataset.Path == imported.Path));
    }

    private void RaiseBoardProperties()
    {
        OnPropertyChanged(nameof(BoardTiles));
        OnPropertyChanged(nameof(DataSourceDetails));
        OnPropertyChanged(nameof(RecordingEmptyState));
        OnPropertyChanged(nameof(CanImportRecording));
    }
}
