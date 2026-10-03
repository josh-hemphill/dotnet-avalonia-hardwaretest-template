namespace HardwareTest.Authoring;

/// A prospective session whose file-backed presentation has loaded before replacement.
public sealed class PreparedAuthoringWorkspace
{
    internal PreparedAuthoringWorkspace(DraftWorkspace draft, WorkspacePackPreview packPreview, IReadOnlyList<RunDataset> datasets)
    {
        Draft = draft;
        PackPreview = packPreview;
        Datasets = datasets;
    }
    internal DraftWorkspace Draft { get; }
    internal WorkspacePackPreview PackPreview { get; }
    internal IReadOnlyList<RunDataset> Datasets { get; }
}

public sealed record DirtyProgramSummary(string PlanId, bool PlanDirty, bool SidecarDirty);
public sealed record ProgramSaveFailure(string PlanId, string Message);
public sealed record SaveAllResult(IReadOnlyList<string> SavedProgramIds, IReadOnlyList<ProgramSaveFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0 && !HasUnsavedChanges;
    public bool HasUnsavedChanges { get; init; }
}

public sealed partial class AuthoringWorkspaceViewModel
{
    public IReadOnlyList<DirtyProgramSummary> DirtyPrograms => DirtyProgramIds
        .Select(id => new DirtyProgramSummary(id, _dirtyPlans.Contains(id), _dirtySidecars.Contains(id))).ToArray();
    private string? _savePreviewWarning;
    public string? SavePreviewWarning
    {
        get => _savePreviewWarning;
        private set => SetField(ref _savePreviewWarning, value);
    }
    public SaveAllResult? LastSaveAllResult { get; private set; }
    public IReadOnlyList<string> SaveAllResults => LastSaveAllResult is { } result
        ? result.SavedProgramIds.Select(id => $"Saved {id}")
            .Concat(result.Failures.Select(failure => $"{failure.PlanId}: {failure.Message}")).ToArray() : [];

    public void SaveProgram(string planId) => SaveProgramWithPreview(planId, forcePlan: false, sidecarOnly: false);

    public SaveAllResult SaveAll()
    {
        var saved = new List<string>();
        var failures = new List<ProgramSaveFailure>();
        foreach (var id in DirtyProgramIds)
        {
            try { SaveProgramCore(id, forcePlan: false, sidecarOnly: false); saved.Add(id); }
            catch (Exception ex) { failures.Add(new(id, ex.Message)); }
        }
        RefreshSavePreview();
        var result = new SaveAllResult(saved.ToArray(), failures.ToArray()) { HasUnsavedChanges = HasUnsavedChanges };
        LastSaveAllResult = result;
        OnPropertyChanged(nameof(LastSaveAllResult));
        OnPropertyChanged(nameof(SaveAllResults));
        Status = $"Saved {saved.Count} program(s); {failures.Count} failure(s)";
        var messages = failures.Select(f => $"{f.PlanId}: {f.Message}")
            .Concat(SavePreviewWarning is { } warning ? [warning] : []).ToArray();
        Error = messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
        return result;
    }

    public void Apply() => SaveProgramWithPreview(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before applying."), forcePlan: true, sidecarOnly: false);

    public void SaveSidecar() => SaveProgramWithPreview(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before saving."), forcePlan: false, sidecarOnly: true);

    private void SaveProgramWithPreview(string planId, bool forcePlan, bool sidecarOnly)
    {
        SaveProgramCore(planId, forcePlan, sidecarOnly);
        RefreshSavePreview();
        Error = SavePreviewWarning;
    }

    private void RefreshSavePreview()
    {
        try
        {
            RefreshPackPreview();
            SavePreviewWarning = null;
        }
        catch (Exception ex)
        {
            SavePreviewWarning = $"Packaging preview could not refresh. Check the OpenTAP home setting and workspace paths, then retry: {ex.Message}";
        }
    }

    private void SaveProgramCore(string planId, bool forcePlan, bool sidecarOnly)
    {
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace before saving.");
        if (workspace.IsReadOnly) throw new AuthoringWorkspaceException("Workspace is read-only; cannot save.");
        var draft = Programs.FirstOrDefault(p => string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            ?? throw new AuthoringWorkspaceException($"Unknown program '{planId}'.");
        var existing = TryExistingTapPlanPath(planId);
        var path = existing ?? ResolveTapPlanPath(planId);
        var savePlan = !sidecarOnly && (forcePlan || _dirtyPlans.Contains(planId) || existing is null);
        if (sidecarOnly && existing is null) throw new AuthoringWorkspaceException($"No TapPlan path for '{planId}'.");
        if (savePlan)
        {
            AuthoringRecipeCatalog.EnsureScalarLimits(draft);
            _compiler.Save(draft, path);
            if (existing is null) Workspace = workspace with { TapPlanPaths = [.. workspace.TapPlanPaths, path] };
            _dirtyPlans.Remove(planId);
            _dirtySidecars.Remove(planId);
        }
        else if (sidecarOnly || _dirtySidecars.Contains(planId))
        {
            _compiler.SaveSidecar(path, draft.Sidecar);
            _dirtySidecars.Remove(planId);
        }
        RefreshDirtyState();
        Status = $"Saved {Path.GetFileName(savePlan ? path : PlanCompiler.SidecarPath(path))}";
        Error = null;
    }
}
