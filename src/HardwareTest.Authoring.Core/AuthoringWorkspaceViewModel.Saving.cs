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
    private string? _saveCompilationWarning;
    private string? _saveEnvironmentPreviewWarning;
    private bool _savePreviewHasHomePathWarning;
    private void ClearResolvedHomePreviewWarning()
    {
        if (!_savePreviewHasHomePathWarning || EnvironmentPathError is not null) return;
        var previous = _saveEnvironmentPreviewWarning;
        _saveEnvironmentPreviewWarning = null; _savePreviewHasHomePathWarning = false;
        UpdateSavePreviewWarning();
        if (previous is not null && Error is { } error)
            Error = error == previous ? null : error.Replace(Environment.NewLine + previous, string.Empty, StringComparison.Ordinal)
                .Replace(previous + Environment.NewLine, string.Empty, StringComparison.Ordinal);
    }
    private void UpdateSavePreviewWarning()
    {
        var warnings = new[] { _saveCompilationWarning, _saveEnvironmentPreviewWarning }.OfType<string>().Distinct().ToArray();
        SavePreviewWarning = warnings.Length == 0 ? null : string.Join(Environment.NewLine, warnings);
    }
    private void ResetSavePreviewWarnings()
    {
        _saveCompilationWarning = null; _saveEnvironmentPreviewWarning = null; _savePreviewHasHomePathWarning = false; SavePreviewWarning = null;
    }
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
        var verificationWarnings = new List<string>();
        var dirtyPrograms = DirtyProgramIds.ToArray();
        var canPublish = true;
        WorkspaceCatalogSaveFailure = null;
        if (dirtyPrograms.Length > 0 || WorkspaceCatalogDirty || HasCurrentFindingCheck)
        {
            try { VerifySavedInputsForSave(dirtyPrograms); }
            catch (Exception ex)
            {
                canPublish = false;
                foreach (var id in dirtyPrograms) failures.Add(new(id, PersistenceError(ex)));
                if (WorkspaceCatalogDirty) WorkspaceCatalogSaveFailure = $"Workspace catalog could not be saved; retry Save All: {PersistenceError(ex)}";
                else if (dirtyPrograms.Length == 0) failures.Add(new("Workspace", PersistenceError(ex)));
            }
        }
        foreach (var id in canPublish ? dirtyPrograms : [])
        {
            try
            {
                var verificationWarning = SaveProgramCore(id, forcePlan: false, sidecarOnly: false);
                saved.Add(id);
                if (verificationWarning is not null) verificationWarnings.Add($"{id}: {verificationWarning}");
            }
            catch (Exception ex) { failures.Add(new(id, PersistenceError(ex))); }
        }
        var catalogSaved = false;
        if (canPublish && WorkspaceCatalogDirty && failures.Count == 0 && DirtyProgramIds.Count == 0)
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
            .Concat(SavePreviewWarning is { } warning ? [warning] : [])
            .Concat(verificationWarnings).ToArray();
        Error = messages.Length == 0 ? null : string.Join(Environment.NewLine, messages);
        return result;
    }

    public void Apply() => SaveProgramWithPreview(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before applying."), forcePlan: true, sidecarOnly: false);

    public void SaveSidecar() => SaveProgramWithPreview(SelectedProgram?.PlanId
        ?? throw new AuthoringWorkspaceException("Select a program before saving."), forcePlan: false, sidecarOnly: true);

    private void SaveProgramWithPreview(string planId, bool forcePlan, bool sidecarOnly)
    {
        var verificationWarning = SaveProgramCore(planId, forcePlan, sidecarOnly);
        RefreshSavePreview();
        var warnings = new[] { SavePreviewWarning, verificationWarning }.OfType<string>().ToArray();
        Error = warnings.Length == 0 ? null : string.Join(Environment.NewLine, warnings);
    }

    private void RefreshSavePreview()
    {
        try
        {
            RefreshPackPreview();
            _savePreviewHasHomePathWarning = EnvironmentPathError is not null;
            _saveEnvironmentPreviewWarning = EnvironmentPathError is { } pathError ? $"Packaging preview could not refresh because the OpenTAP home setting is invalid: {pathError}" : null;
            if (!HasUncompiledSources) _saveCompilationWarning = null;
        }
        catch (Exception ex)
        {
            _savePreviewHasHomePathWarning = false;
            _saveEnvironmentPreviewWarning = $"Packaging preview could not refresh. Check the OpenTAP home setting and workspace paths, then retry: {ex.Message}";
        }
        UpdateSavePreviewWarning();
    }

    private string? SaveProgramCore(string planId, bool forcePlan, bool sidecarOnly)
    {
        VerifySavedInputsForSave([planId]);
        string? verificationWarning = null;
        try { PublishProgramCore(planId, forcePlan, sidecarOnly); }
        finally { verificationWarning = InvalidateFindingsAfterSave(); }
        return verificationWarning;
    }

    private void PublishProgramCore(string planId, bool forcePlan, bool sidecarOnly)
    {
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace before saving.");
        if (workspace.IsReadOnly) throw new AuthoringWorkspaceException("Workspace is read-only; cannot save.");
        var draft = Programs.FirstOrDefault(p => string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase))
            ?? throw new AuthoringWorkspaceException($"Unknown program '{planId}'.");
        var existing = TryExistingTapPlanPath(planId);
        var path = existing ?? ResolveTapPlanPath(planId);
        var actualDirty = _documents[planId].GetDirtyState(draft);
        _sourceDocuments.TryGetValue(planId, out var baseline);
        var savePlan = !sidecarOnly && (forcePlan || actualDirty.PlanDirty || existing is null || baseline?.RequiresCompilation == true);
        if (sidecarOnly && existing is null) throw new AuthoringWorkspaceException($"No TapPlan path for '{planId}'.");
        var store = new AuthoringDocumentStore(workspace.Root);
        store.ValidatePath(path);
        store.ValidatePath(PlanCompiler.SidecarPath(path));
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
        string? savedCompilationDiagnostic = null;
        if (_compiledConflicts.Contains(planId)) compilationFailure = "External compiled edits require reconciliation before export.";
        else if (AuthoringFormulaDeployment.Project(draft).AuthoringState.IncompleteNumericText.Count > 0)
            compilationFailure = "Incomplete numeric input was saved as an authoring draft.";
        else
        {
            try
            {
                if (savePlan)
                {
                    AuthoringRecipeCatalog.EnsureScalarLimits(AuthoringFormulaDeployment.Project(draft));
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
                compilationFailure = PersistenceError(ex);
                savedCompilationDiagnostic = compilationFailure;
            }
        }
        if (compilationFailure is not null)
        {
            document.RequiresCompilation = true;
            store.Save(document);
            _uncompiledDocuments.Add(planId);
        }
        RecordSavedCompilationDiagnostic(planId, savedCompilationDiagnostic, store);
        _documents[planId].AcceptSavedContent(draft, plan: !sidecarOnly, sidecar: true);
        RaiseDraftState();
        RecomputeDocumentDirty();
        Status = compilationFailure is null ? $"Saved {Path.GetFileName(savePlan ? path : PlanCompiler.SidecarPath(path))} and authoring source" : $"Saved authoring draft {planId}; compilation requires attention";
        _saveCompilationWarning = compilationFailure;
        UpdateSavePreviewWarning();
        Error = compilationFailure;
    }
}
