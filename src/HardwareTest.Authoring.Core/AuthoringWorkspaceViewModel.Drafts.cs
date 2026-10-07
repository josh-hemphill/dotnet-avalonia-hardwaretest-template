namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly Dictionary<string, AuthoringDocumentDto> _sourceDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AuthoringDocumentDto> _recoverableDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _compiledConflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _uncompiledDocuments = new(StringComparer.OrdinalIgnoreCase);
    private AuthoringRecoveryCheckpointService? _recovery;
    private long _recoveryGeneration;
    private Action<Action> _recoveryDispatch = action => action();
    public string? SelectedRecoveryPlanId { get; set; }
    public IReadOnlyList<string> RecoverableProgramIds => _recoverableDocuments.Keys.ToArray();
    public IReadOnlyList<string> CompiledConflictProgramIds => _compiledConflicts.ToArray();
    public bool HasUncompiledSources => _uncompiledDocuments.Count > 0;
    public string DraftStateSummary => string.Join("; ",
        (Workspace?.IsReadOnly == true ? new[] { "Workspace is read-only; future source bytes are preserved." } : [])
        .Concat(_recoverableDocuments.Count > 0 ? new[] { $"Recovery available: {string.Join(", ", RecoverableProgramIds)}" } : [])
        .Concat(_compiledConflicts.Count > 0 ? [$"External compiled changes: {string.Join(", ", CompiledConflictProgramIds)}"] : [])
        .Concat(HasUncompiledSources ? ["Saved drafts require compilation before validation or packing."] : []));

    public void ConfigureRecoveryDispatch(Action<Action> dispatch) => _recoveryDispatch = dispatch;
    public void StopRecovery() { _recoveryGeneration++; _recovery?.Dispose(); _recovery = null; }

    private DraftWorkspace LoadWithSources(string root) => AuthoringSourceWorkspaceLoader.Load(root, _compiler);

    private void RefreshSourceReadiness()
    {
        var store = new AuthoringDocumentStore(Workspace!.Root);
        var documents = new Dictionary<string, AuthoringDocumentDto>(StringComparer.OrdinalIgnoreCase);
        var uncompiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in store.ListDocumentIds())
        {
            var source = store.Load(id);
            if (source.Document is not { } document)
            {
                uncompiled.Add(id);
                continue;
            }
            documents[id] = document;
            if (document.RequiresCompilation || document.CompiledPlanHash is null) uncompiled.Add(id);
            if (CompiledChanged(document)) conflicts.Add(id);
        }
        foreach (var previous in _sourceDocuments)
        {
            if ((previous.Value.RequiresCompilation || previous.Value.CompiledPlanHash is null)
                && _documents.ContainsKey(previous.Key) && !documents.ContainsKey(previous.Key))
            {
                documents[previous.Key] = previous.Value;
                uncompiled.Add(previous.Key);
                if (CompiledChanged(previous.Value)) conflicts.Add(previous.Key);
            }
        }
        _sourceDocuments.Clear();
        foreach (var document in documents) _sourceDocuments.Add(document.Key, document.Value);
        _uncompiledDocuments.Clear(); _uncompiledDocuments.UnionWith(uncompiled);
        _compiledConflicts.Clear(); _compiledConflicts.UnionWith(conflicts);
        RaiseDraftState();
    }

    private void InitializeSourceState()
    {
        StopRecovery();
        _sourceDocuments.Clear(); _recoverableDocuments.Clear(); _compiledConflicts.Clear(); _uncompiledDocuments.Clear();
        var store = new AuthoringDocumentStore(Workspace!.Root);
        foreach (var draft in Programs)
        {
            var document = store.Load(draft.PlanId).Document;
            if (document is not null)
            {
                _sourceDocuments[draft.PlanId] = document;
                if (CompiledChanged(document)) _compiledConflicts.Add(draft.PlanId);
                if (document.RequiresCompilation || document.CompiledPlanHash is null) _uncompiledDocuments.Add(draft.PlanId);
            }
            var recovered = store.LoadAtPath(store.GetRecoveryPath(draft.PlanId));
            if (recovered.Document is { } checkpoint && (document is null || checkpoint.SavedAtUtc > document.SavedAtUtc)
                && !AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(checkpoint.ToDraft())))
                _recoverableDocuments[draft.PlanId] = checkpoint;
            if (recovered.Error is { } error) Error = $"Recovery could not load: {error}";
        }
        foreach (var id in store.ListRecoveryIds())
        {
            if (_documents.ContainsKey(id)) continue;
            var recovered = store.LoadAtPath(store.GetRecoveryPath(id));
            if (recovered.Document is { } checkpoint) _recoverableDocuments[id] = checkpoint;
            else if (recovered.Error is { } error) Error = $"Recovery could not load: {error}";
        }
        var generation = _recoveryGeneration;
        if (!Workspace.IsReadOnly) _recovery = new AuthoringRecoveryCheckpointService(action => _recoveryDispatch(action), result =>
        {
            if (generation != _recoveryGeneration || !_documents.TryGetValue(result.PlanId, out var active) || !active.IsDirty) return;
            if (result.IsSuccess) Status = $"Recovery checkpoint saved for {result.PlanId}; source remains unsaved.";
            else ReportError($"Recovery checkpoint failed; your draft remains open: {result.Error}");
        });
        RaiseDraftState();
    }

    private bool CompiledChanged(AuthoringDocumentDto document)
    {
        var path = TryExistingTapPlanPath(document.PlanId);
        return document.CompiledPlanHash != AuthoringDocumentStore.ComputeHash(path)
            || document.CompiledSidecarHash != AuthoringDocumentStore.ComputeHash(path is null ? null : PlanCompiler.SidecarPath(path));
    }

    private void ScheduleRecovery()
    {
        if (_recovery is null || Workspace is null || Workspace.IsReadOnly) return;
        foreach (var draft in Programs)
        {
            if (!_documents.TryGetValue(draft.PlanId, out var session)) continue;
            var dirty = session.GetDirtyState(draft);
            if (!dirty.PlanDirty && !dirty.SidecarDirty)
            {
                _recovery.Cancel(Workspace.Root, draft.PlanId);
                if (!_recoverableDocuments.ContainsKey(draft.PlanId))
                {
                    try { new AuthoringDocumentStore(Workspace.Root).DeleteRecovery(draft.PlanId); }
                    catch (Exception error) { ReportError($"Stale recovery checkpoint could not be removed: {error.Message}"); }
                }
                continue;
            }
            _sourceDocuments.TryGetValue(draft.PlanId, out var baseline);
            _recovery.Schedule(Workspace.Root, AuthoringDocumentDto.FromDraft(draft, session.Revision,
                compiledPlanHash: baseline?.CompiledPlanHash, compiledSidecarHash: baseline?.CompiledSidecarHash));
        }
    }

    public void AcceptRecovery(string planId)
    {
        EnsureWritableWorkspace("recover a draft");
        if (!_recoverableDocuments.Remove(planId, out var checkpoint)) return;
        if (!_documents.TryGetValue(planId, out var session))
        {
            session = new AuthoringDocumentSession(checkpoint.ToDraft(), isSaved: false, revision: checkpoint.Revision);
            _documents.Add(planId, session);
            Programs = [.. Programs, session.Draft];
            _uncompiledDocuments.Add(planId);
        }
        else session.ApplyEdit("Recover checkpoint", _ => checkpoint.ToDraft());
        PresentSession(_documents[planId]);
        RaiseDraftState();
    }

    public void DiscardRecovery(string planId)
    {
        EnsureWritableWorkspace("discard recovery");
        var store = new AuthoringDocumentStore(Workspace!.Root);
        store.DeleteRecovery(planId);
        _recoverableDocuments.Remove(planId);
        RaiseDraftState();
    }

    public void ReconcileCompiled(string planId, bool useCompiledContent)
    {
        EnsureWritableWorkspace("reconcile external edits");
        if (!_compiledConflicts.Contains(planId)) return;
        var path = TryExistingTapPlanPath(planId);
        if (useCompiledContent && path is null) throw new AuthoringWorkspaceException("Compiled plan was removed; retain the source to export it again.");
        if (useCompiledContent)
        {
            _documents[planId].ApplyEdit("Import external compiled changes", _ => _compiler.Load(path!));
            PresentSession(_documents[planId]);
        }
        _sourceDocuments[planId] = AuthoringDocumentDto.FromDraft(_documents[planId].Draft, _documents[planId].Revision,
            compiledPlanHash: AuthoringDocumentStore.ComputeHash(path), compiledSidecarHash: AuthoringDocumentStore.ComputeHash(path is null ? null : PlanCompiler.SidecarPath(path)));
        if (!useCompiledContent)
        {
            _sourceDocuments[planId].RequiresCompilation = true;
            new AuthoringDocumentStore(Workspace!.Root).Save(_sourceDocuments[planId]);
            _uncompiledDocuments.Add(planId);
        }
        _compiledConflicts.Remove(planId);
        RaiseDraftState();
    }

    private void RaiseDraftState()
    {
        if (SelectedRecoveryPlanId is null || !_recoverableDocuments.ContainsKey(SelectedRecoveryPlanId))
            SelectedRecoveryPlanId = _recoverableDocuments.Keys.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRecoveryPlanId));
        OnPropertyChanged(nameof(RecoverableProgramIds)); OnPropertyChanged(nameof(CompiledConflictProgramIds));
        OnPropertyChanged(nameof(HasUncompiledSources)); OnPropertyChanged(nameof(DraftStateSummary));
        RaisePackGuardProperties();
    }
}
