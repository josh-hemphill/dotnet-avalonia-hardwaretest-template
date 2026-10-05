namespace HardwareTest.Authoring;

public sealed record AuthoringCompletedBuild(AuthoringBuildResult Result, string OutputDirectory, string SelectedHome)
{
    public string DisplayText => $"{Result.Receipt.CompletedAt:u} — {Result.Manifest.PackageName} {Result.Manifest.Version}; build {Result.Receipt.BuildId}; {OutputDirectory}";
}
public sealed record AuthoringBuildProgramLine(string PlanId, bool Included, long? SavedRevision, string State, bool CanChangeInclusion);

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly List<AuthoringCompletedBuild> _buildHistory = [];
    private string? _completedBuildIdentity;
    public Guid WorkspaceSessionId => _workspaceSession;
    public IReadOnlyList<AuthoringCompletedBuild> BuildHistory => _buildHistory.ToArray();
    public AuthoringCompletedBuild? LastCompletedBuild => _buildHistory.LastOrDefault();
    public AuthoringBuildReceipt? LastBuildReceipt => LastCompletedBuild?.Result.Receipt;
    public bool HasCompletedBuild => LastCompletedBuild is not null;
    public string BuildReadinessText => OperationBusy ? $"Build operation: {OperationStage ?? "starting"}" : LastCompletedBuild is null
        ? "No completed checked build. Compatibility has not been checked."
        : HasUnsavedChanges || _completedBuildIdentity != CurrentBuildIdentity()
            ? "Current revision needs a new build. Previous checked artifacts are retained."
            : "Completed build checks apply to the recorded saved revision.";
    public string CompatibilityState => LastBuildReceipt?.RequiredChecks.Any(c => c.Code == "BUILD_COMPATIBILITY_PASS") == true
        ? "Passed for recorded revision by production TuiCompatChecker (in-process)."
        : LastBuildReceipt is null ? "Not checked" : "Injected checker evidence; production compatibility not established.";
    public IReadOnlyList<string> BuildChecks => LastBuildReceipt?.RequiredChecks.Select(c => $"{c.Code}: {c.Message}").ToArray() ?? [];
    public IReadOnlyList<string> CheckedRevisions => LastBuildReceipt is { } receipt
        ? new[] { $"Workspace saved revision: {receipt.WorkspaceSavedRevision?.ToString() ?? "compiled workspace"}; build {receipt.BuildId}" }
            .Concat(receipt.Sources.Select(s => $"{s.PlanId}: saved revision {s.SavedRevision?.ToString() ?? "compiled import"}; SHA256 {s.SourceSha256 ?? "recorded compiled input"}")).ToArray() : [];
    public IReadOnlyList<string> CheckedOutputs => LastBuildReceipt?.Outputs.Select(o => $"{o.Path}: SHA256 {o.Sha256}").ToArray() ?? [];
    public IReadOnlyList<AuthoringBuildProgramLine> BuildPrograms => Workspace is null ? [] : AuthoringBuildInclusion.ProgramIds(Workspace)
        .Select(id => new AuthoringBuildProgramLine(id, AuthoringBuildInclusion.Includes(Workspace.Manifest, id),
            _sourceDocuments.GetValueOrDefault(id)?.Revision, _uncompiledDocuments.Contains(id) ? "Saved source requires compilation" : _compiledConflicts.Contains(id) ? "Source conflict" : "Saved compiled input", !Workspace.IsReadOnly && AuthoringBuildInclusion.AllowsProgram(Workspace.Manifest, id))).ToArray();

    public void SetBuildProgramIncluded(string planId, bool included)
    {
        if (OperationBusy) throw new AuthoringWorkspaceException("Wait for the active operation before changing package inclusion.");
        if (Workspace is null || !AuthoringBuildInclusion.ProgramIds(Workspace).Contains(planId, StringComparer.OrdinalIgnoreCase))
            throw new AuthoringWorkspaceException("Choose an existing saved program.");
        if (included && !AuthoringBuildInclusion.AllowsProgram(Workspace.Manifest, planId))
            throw new AuthoringWorkspaceException("The template package includes only its fixed program files. Change the package name before including other programs.");
        RunCatalogEdit("Change package program inclusion", () =>
        {
            Workspace.Manifest.ExcludedProgramIds.RemoveAll(id => string.Equals(id, planId, StringComparison.OrdinalIgnoreCase));
            if (!included) Workspace.Manifest.ExcludedProgramIds.Add(planId);
        });
        RefreshPackPreview();
        RaisePackGuardProperties();
    }

    private string? CurrentBuildIdentity() => Workspace is null ? null : ContentFingerprint() + "|" + AuthoringHomeText;
    private void RetainCompletedBuild(AuthoringBuildResult result, string outputDirectory, string selectedHome, string? checkedIdentity = null)
    {
        _buildHistory.Add(new(result, Path.GetFullPath(outputDirectory), selectedHome));
        if (_buildHistory.Count > 20) _buildHistory.RemoveAt(0);
        _completedBuildIdentity = checkedIdentity ?? CurrentBuildIdentity();
        RaiseBuildResultProperties();
    }
    private void ClearCompletedBuilds()
    {
        _buildHistory.Clear(); _completedBuildIdentity = null;
        RaiseBuildResultProperties();
    }
    private void RaiseBuildResultProperties()
    {
        foreach (var name in new[] { nameof(BuildHistory), nameof(LastCompletedBuild), nameof(LastBuildReceipt), nameof(HasCompletedBuild), nameof(BuildReadinessText),
            nameof(CompatibilityState), nameof(BuildChecks), nameof(CheckedRevisions), nameof(CheckedOutputs), nameof(BuildPrograms) }) OnPropertyChanged(name);
    }
}
