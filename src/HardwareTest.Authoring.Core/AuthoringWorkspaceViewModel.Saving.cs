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
    public SaveAllResult? LastSaveAllResult { get; private set; }
    public IReadOnlyList<string> SaveAllResults => LastSaveAllResult is { } result
        ? result.SavedProgramIds.Select(id => $"Saved {id}")
            .Concat(result.Failures.Select(failure => $"{failure.PlanId}: {failure.Message}")).ToArray() : [];

    public void SaveProgram(string planId) => SaveProgramCore(planId, forcePlan: false, sidecarOnly: false);

    public SaveAllResult SaveAll()
    {
        var saved = new List<string>();
        var failures = new List<ProgramSaveFailure>();
        foreach (var id in DirtyProgramIds)
        {
            try { SaveProgram(id); saved.Add(id); }
            catch (Exception ex) { failures.Add(new(id, ex.Message)); }
        }
        var result = new SaveAllResult(saved.ToArray(), failures.ToArray()) { HasUnsavedChanges = HasUnsavedChanges };
        LastSaveAllResult = result;
        OnPropertyChanged(nameof(LastSaveAllResult));
        OnPropertyChanged(nameof(SaveAllResults));
        Status = $"Saved {saved.Count} program(s); {failures.Count} failure(s)";
        Error = failures.Count == 0 ? null : string.Join(Environment.NewLine, failures.Select(f => $"{f.PlanId}: {f.Message}"));
        return result;
    }

    public void Apply() => SaveProgramCore(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before applying."), forcePlan: true, sidecarOnly: false);

    public void SaveSidecar() => SaveProgramCore(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before saving."), forcePlan: false, sidecarOnly: true);

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
        RefreshPackPreview();
        Status = $"Saved {Path.GetFileName(savePlan ? path : PlanCompiler.SidecarPath(path))}";
        Error = null;
    }
}
