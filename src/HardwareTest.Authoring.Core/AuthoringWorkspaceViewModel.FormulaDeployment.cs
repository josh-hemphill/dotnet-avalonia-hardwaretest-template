namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public FormulaDeploymentStatus? FormulaDeploymentStatus => HasFormula && SelectedMetric is { } metric
        ? FormulaDeploymentClassifier.Classify(metric, SelectedProgram) : null;
    public string FormulaRecordingEvidence => HasFormula && SelectedMetric is { } metric
        ? FormulaDeploymentClassifier.DescribeRecording(metric, SelectedDataset is { } dataset
            ? RunDatasetBinder.SeriesByMetric(dataset.Run) : null) : string.Empty;
    public string FormulaDeploymentLabel => FormulaDeploymentStatus?.Label ?? string.Empty;
    public string FormulaLoweringTarget => $"Lowering target: {FormulaDeploymentStatus?.Target ?? "None"}";
    public string FormulaDeploymentRequirements => FormulaDeploymentStatus?.Requirements ?? string.Empty;
    public string FormulaDeploymentMessage => FormulaDeploymentStatus?.Message ?? string.Empty;
    public bool FormulaNeedsThreshold => HasFormula && ShowThreshold && (SelectedMetric?.Limits?.Threshold is null
        || SelectedSequence?.NodeId is { } id && SelectedProgram?.AuthoringState.IncompleteNumericText.ContainsKey(
            AuthoringDocumentState.FieldKey(id, nameof(Threshold))) == true);
    public string FormulaDeploymentInclusion => FormulaExplorationOnly
        ? "Exploration: saved unchanged; explicitly excluded from deployment and build artifacts."
        : "Deployment: this expression must satisfy the recipe requirements before building.";
    public IReadOnlyList<FormulaCatalog.Item> DeployableFormulaCompletions => FormulaCompletions.Where(item => item.Packs).ToArray();
    public IReadOnlyList<FormulaCatalog.Item> ExplorationFormulaCompletions => FormulaCompletions.Where(item => !item.Packs && item.Kind == "function").ToArray();
    public IReadOnlyList<FormulaCatalog.Item> FormulaChannelCompletions => FormulaCompletions.Where(item => item.Kind == "channel").ToArray();
    public IReadOnlyList<string> ExcludedExplorationFormulas => Programs.SelectMany(draft =>
        AuthoringDependencyIndex.Build(draft).Nodes.Where(node => AuthoringFormulaDeployment.ExcludedNodes(draft).Contains(node.NodeId)).Select(node => $"{draft.PlanId}: {node.ProducedChannel} (exploration)")).ToArray();

    private void RaiseFormulaDeployment()
    {
        foreach (var property in new[] { nameof(FormulaRecordingEvidence), nameof(FormulaNeedsThreshold), nameof(FormulaDeploymentStatus), nameof(FormulaDeploymentLabel),
            nameof(FormulaLoweringTarget), nameof(FormulaDeploymentRequirements), nameof(FormulaDeploymentMessage),
            nameof(FormulaDeploymentInclusion), nameof(DeployableFormulaCompletions), nameof(ExplorationFormulaCompletions),
            nameof(FormulaChannelCompletions), nameof(ExcludedExplorationFormulas) }) OnPropertyChanged(property);
    }
}
