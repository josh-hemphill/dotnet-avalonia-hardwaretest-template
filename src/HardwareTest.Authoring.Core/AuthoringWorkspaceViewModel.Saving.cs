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
    public bool Succeeded => Failures.Count == 0 && WorkspaceCatalogFailure is null && !HasUnsavedChanges;
    public bool WorkspaceCatalogSaved { get; init; }
    public string? WorkspaceCatalogFailure { get; init; }
    public bool HasUnsavedChanges { get; init; }
}

public sealed partial class AuthoringWorkspaceViewModel
{
    public IReadOnlyList<DirtyProgramSummary> DirtyPrograms => DirtyProgramIds
        .Select(id => new DirtyProgramSummary(id, _dirtyPlans.Contains(id), _dirtySidecars.Contains(id))).ToArray();
    internal Action<string, string>? WorkspaceManifestReplacement { get; set; }
    internal Action<string, string>? WorkspaceSourceReplacement { get; set; }
    private bool _workspaceCatalogDirty;
    private string? _workspaceCatalogSaveFailure;
    public bool WorkspaceCatalogDirty
    {
        get => _workspaceCatalogDirty;
        private set => SetField(ref _workspaceCatalogDirty, value);
    }
    public string? WorkspaceCatalogSaveFailure
    {
        get => _workspaceCatalogSaveFailure;
        private set => SetField(ref _workspaceCatalogSaveFailure, value);
    }
    public IReadOnlyList<string> UnsavedChangesSummary => DirtyPrograms
        .Select(p => $"Program '{p.PlanId}' ({(p.PlanDirty ? "plan and sidecar" : "sidecar")})")
        .Concat(WorkspaceCatalogDirty ? ["Workspace catalog changes (Save All required)"] : []).ToArray();
    private string? _savePreviewWarning;
    public string? SavePreviewWarning
    {
        get => _savePreviewWarning;
        private set => SetField(ref _savePreviewWarning, value);
    }
    public SaveAllResult? LastSaveAllResult { get; private set; }
    public IReadOnlyList<string> SaveAllResults => LastSaveAllResult is { } result
        ? result.SavedProgramIds.Select(id => $"Saved {id}")
            .Concat(result.Failures.Select(failure => $"{failure.PlanId}: {failure.Message}"))
            .Concat(result.WorkspaceCatalogSaved ? ["Saved workspace catalog"] : [])
            .Concat(result.WorkspaceCatalogFailure is { } catalogFailure ? [$"Workspace catalog: {catalogFailure}"] : []).ToArray() : [];

    public static string PersistenceError(Exception error)
    {
        var message = error.Message;
        if (error.Data["AuthoringRecoveryBackups"] is string[] backups)
            message += $" Recovery backups: {string.Join(", ", backups)}. {error.Data["AuthoringRecoveryAction"]}";
        return message;
    }

    public void SaveProgram(string planId) => SaveProgramWithPreview(planId, forcePlan: false, sidecarOnly: false);

    public SaveAllResult SaveAll()
    {
        var saved = new List<string>();
        var failures = new List<ProgramSaveFailure>();
        foreach (var id in DirtyProgramIds)
        {
            try { SaveProgramCore(id, forcePlan: false, sidecarOnly: false); saved.Add(id); }
            catch (Exception ex) { failures.Add(new(id, PersistenceError(ex))); }
        }
        var catalogSaved = false;
        WorkspaceCatalogSaveFailure = null;
        if (WorkspaceCatalogDirty && failures.Count == 0 && DirtyProgramIds.Count == 0)
        {
            try
            {
                EnsureWritableWorkspace("save the workspace catalog");
                var beforeManifestSave = CaptureWorkspace();
                AuthoringWorkspaceLoader.SaveManifest(Workspace!.Root, Workspace.Manifest, WorkspaceManifestReplacement);
                new AuthoringDocumentStore(Workspace.Root, new AuthoringAtomicWriter(WorkspaceSourceReplacement)).SaveWorkspace(Workspace.Manifest);
                _workspaceHistory.RebaseCurrent(beforeManifestSave, CaptureWorkspace());
                _savedManifestIdentity = ManifestIdentity();
                WorkspaceCatalogDirty = false;
                catalogSaved = true;
            }
            catch (Exception ex) { WorkspaceCatalogSaveFailure = $"Workspace catalog could not be saved; retry Save All: {ex.Message}"; }
        }
        RefreshDirtyState();
        RefreshSavePreview();
        var result = new SaveAllResult(saved.ToArray(), failures.ToArray())
        {
            HasUnsavedChanges = HasUnsavedChanges,
            WorkspaceCatalogSaved = catalogSaved,
            WorkspaceCatalogFailure = WorkspaceCatalogSaveFailure,
        };
        LastSaveAllResult = result;
        OnPropertyChanged(nameof(LastSaveAllResult));
        OnPropertyChanged(nameof(SaveAllResults));
        Status = $"Saved {saved.Count} program(s){(catalogSaved ? " and workspace catalog" : "")}; {failures.Count + (WorkspaceCatalogSaveFailure is null ? 0 : 1)} failure(s)";
        var messages = failures.Select(f => $"{f.PlanId}: {f.Message}")
            .Concat(WorkspaceCatalogSaveFailure is { } catalogFailure ? [catalogFailure] : [])
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
            if (!HasUncompiledSources) SavePreviewWarning = null;
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
        var actualDirty = _documents[planId].GetDirtyState(draft);
        var savePlan = !sidecarOnly && (forcePlan || actualDirty.PlanDirty || existing is null);
        if (sidecarOnly && existing is null) throw new AuthoringWorkspaceException($"No TapPlan path for '{planId}'.");
        var store = new AuthoringDocumentStore(workspace.Root);
        store.ValidatePath(path);
        store.ValidatePath(PlanCompiler.SidecarPath(path));
        _sourceDocuments.TryGetValue(planId, out var baseline);
        if (baseline is not null && CompiledChanged(baseline)) _compiledConflicts.Add(planId);
        // Publish authoring content independently of deployment readiness.
        var document = AuthoringDocumentDto.FromDraft(draft, _documents[planId].Revision,
            compiledPlanHash: baseline?.CompiledPlanHash ?? AuthoringDocumentStore.ComputeHash(existing),
            compiledSidecarHash: baseline?.CompiledSidecarHash ?? AuthoringDocumentStore.ComputeHash(existing is null ? null : PlanCompiler.SidecarPath(existing)));
        document.RequiresCompilation = savePlan || sidecarOnly || actualDirty.PlanDirty || actualDirty.SidecarDirty || baseline?.RequiresCompilation == true;
        store.Save(document);
        if (document.RequiresCompilation) _uncompiledDocuments.Add(planId);
        _sourceDocuments[planId] = document;
        _recovery?.Cancel(workspace.Root, planId);
        store.DeleteRecovery(planId);
        string? compilationFailure = null;
        if (_compiledConflicts.Contains(planId)) compilationFailure = "External compiled edits require reconciliation before export.";
        else if (draft.AuthoringState.IncompleteNumericText.Count > 0 || draft.AuthoringState.FormulaIntent.Values.Contains(FormulaDeploymentIntent.Explore))
            compilationFailure = "Incomplete or exploration content was saved as an authoring draft.";
        else
        {
            try
            {
                if (savePlan)
                {
                    AuthoringRecipeCatalog.EnsureScalarLimits(draft);
                    _compiler.Save(draft, path);
                    if (existing is null) Workspace = workspace with { TapPlanPaths = [.. workspace.TapPlanPaths, path] };
                }
                else if (sidecarOnly || actualDirty.SidecarDirty) _compiler.SaveSidecar(path, PlanCompiler.CloneSidecar(draft.Sidecar));
                document = AuthoringDocumentDto.FromDraft(draft, _documents[planId].Revision,
                    compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
                    compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
                _sourceDocuments[planId] = document;
                document.RequiresCompilation = !savePlan && (actualDirty.PlanDirty || baseline?.RequiresCompilation == true);
                store.Save(document);
                if (document.RequiresCompilation) _uncompiledDocuments.Add(planId);
                else _uncompiledDocuments.Remove(planId);
            }
            catch (Exception ex) when (ex is not IOException && ex is not UnauthorizedAccessException)
            {
                compilationFailure = ex.Message;
                if (ex.Data["AuthoringRecoveryBackups"] is { } backups)
                    compilationFailure += $" Recovery backups: {backups}. {ex.Data["AuthoringRecoveryAction"]}";
            }
        }
        if (compilationFailure is not null)
        {
            document.RequiresCompilation = true;
            store.Save(document);
            _uncompiledDocuments.Add(planId);
        }
        _documents[planId].AcceptSavedContent(draft, plan: !sidecarOnly, sidecar: true);
        RaiseDraftState();
        RecomputeDocumentDirty();
        Status = compilationFailure is null ? $"Saved {Path.GetFileName(path)} and authoring source" : $"Saved authoring draft {planId}; compilation requires attention";
        SavePreviewWarning = compilationFailure;
        Error = compilationFailure;
    }
}
