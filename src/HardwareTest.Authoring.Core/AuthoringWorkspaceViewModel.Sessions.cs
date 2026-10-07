namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private readonly Dictionary<string, AuthoringDocumentSession> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly AuthoringWorkspaceHistory _workspaceHistory = new();
    private string? _savedManifestIdentity;
    private bool _catalogTransaction;

    public AuthoringDocumentSession? SelectedDocument
        => SelectedProgram is { } draft && _documents.TryGetValue(draft.PlanId, out var session) ? session : null;
    public bool CanUndo => Workspace is { IsReadOnly: false } && SelectedDocument?.CanUndo == true && PresentedDocumentMatchesSession();
    public bool CanRedo => Workspace is { IsReadOnly: false } && SelectedDocument?.CanRedo == true && PresentedDocumentMatchesSession();
    public bool CanUndoWorkspace => Workspace is { IsReadOnly: false } && _workspaceHistory.CanUndo(CaptureWorkspace());
    public bool CanRedoWorkspace => Workspace is { IsReadOnly: false } && _workspaceHistory.CanRedo(CaptureWorkspace());
    public string WorkspaceHistoryHint => "Workspace Undo/Redo restores a catalog operation and its affected programs together. Intervening edits must be undone first.";
    public IReadOnlyList<AuthoringEditingIssue> EditingIssues => Programs.SelectMany(AuthoringIssueService.GetIssues).ToArray();

    public void Undo()
    {
        EnsureWritableWorkspace("undo a program edit");
        var session = SelectedDocument ?? throw new AuthoringWorkspaceException("Select a program before Undo.");
        EnsurePresentedHistoryCurrent();
        session.Undo();
        PresentSession(session);
    }

    public void Redo()
    {
        EnsureWritableWorkspace("redo a program edit");
        var session = SelectedDocument ?? throw new AuthoringWorkspaceException("Select a program before Redo.");
        EnsurePresentedHistoryCurrent();
        session.Redo();
        PresentSession(session);
    }

    public void UndoWorkspace()
    {
        EnsureWritableWorkspace("undo a workspace catalog operation");
        RestoreWorkspace(_workspaceHistory.Undo(CaptureWorkspace()));
    }

    public void RedoWorkspace()
    {
        EnsureWritableWorkspace("redo a workspace catalog operation");
        RestoreWorkspace(_workspaceHistory.Redo(CaptureWorkspace()));
    }

    private bool PresentedDocumentMatchesSession()
        => SelectedProgram is { } draft && SelectedDocument is { } session
            && session.Snapshot.ContentEquals(AuthoringDocumentSnapshot.Capture(draft));

    private void EnsurePresentedHistoryCurrent()
    {
        if (!PresentedDocumentMatchesSession())
            throw new AuthoringWorkspaceException("Program content changed outside its edit history; save or reopen it before restoring history.");
    }

    private void PresentSession(AuthoringDocumentSession session)
    {
        var draft = session.Draft;
        Programs = Programs.Select(p => string.Equals(p.PlanId, draft.PlanId, StringComparison.OrdinalIgnoreCase) ? draft : p).ToArray();
        _selectedProgram = draft;
        _selectedSequenceKey = null;
        OnPropertyChanged(nameof(SelectedProgram));
        RefreshMeasurePresentation();
        RestoreNodeSelection(session.SelectedNodeId);
        RaiseSidecarProperties();
        RecomputeDocumentDirty();
        Findings = [];
        FindingRows = [];
    }

    private void RestoreNodeSelection(Guid? nodeId)
    {
        if (nodeId is null) return;
        var index = _sequenceItems.ToList().FindIndex(row => row.NodeId == nodeId);
        if (index >= 0) SelectSequence(index);
    }

    private void RememberNodeSelection()
    {
        if (SelectedDocument is { } session) session.SelectedNodeId = SelectedSequence?.NodeId;
    }

    private void InitializeDocuments(IReadOnlyList<ProgramDraft> drafts)
    {
        _documents.Clear();
        _workspaceHistory.Clear();
        foreach (var draft in drafts)
            _documents.Add(draft.PlanId, new AuthoringDocumentSession(draft, isSaved: true, isReadOnly: Workspace!.IsReadOnly));
        Programs = drafts.Select(d => _documents[d.PlanId].Draft).ToArray();
        _savedManifestIdentity = ManifestIdentity();
    }

    private bool CommitDocument(ProgramDraft draft, string description = "Edit program")
    {
        EnsureWritableWorkspace("edit a program");
        if (!_documents.TryGetValue(draft.PlanId, out var session))
            throw new AuthoringWorkspaceException($"Program '{draft.PlanId}' no longer exists in this session.");
        if (session.Snapshot.ContentEquals(AuthoringDocumentSnapshot.Capture(draft))) return false;
        if (_catalogTransaction)
        {
            session.RestoreExternal(draft, session.SelectedNodeId);
            InvalidateContractFindings();
            return true;
        }
        var changed = session.ApplyEdit(description, _ => draft);
        if (changed) InvalidateContractFindings();
        return changed;
    }

    private void InvalidateContractFindings()
    {
        Findings = [];
        FindingRows = [];
    }

    private void RecomputeDocumentDirty()
    {
        ObserveContentDirty();
        OnPropertyChanged(nameof(WorkspaceCatalogDirty));
        RefreshDirtyState();
        RaiseHistoryProperties();
        OnPropertyChanged(nameof(EditingIssues));
    }

    private void ObserveContentDirty()
    {
        _dirtyPlans.Clear();
        _dirtySidecars.Clear();
        foreach (var draft in Programs)
        {
            if (!_documents.TryGetValue(draft.PlanId, out var session)) continue;
            var dirty = session.GetDirtyState(draft);
            if (dirty.PlanDirty) _dirtyPlans.Add(draft.PlanId);
            if (dirty.SidecarDirty) _dirtySidecars.Add(draft.PlanId);
        }
        _workspaceCatalogDirty = Workspace is not null && ManifestIdentity() != _savedManifestIdentity;
    }

    private void RaiseHistoryProperties()
    {
        OnPropertyChanged(nameof(SelectedDocument));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanUndoWorkspace));
        OnPropertyChanged(nameof(CanRedoWorkspace));
    }

    private string? ManifestIdentity() => Workspace is null ? null : System.Text.Json.JsonSerializer.Serialize(
        Workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest);

    private AuthoringWorkspaceState CaptureWorkspace()
        => AuthoringWorkspaceState.Capture(Workspace!.Manifest, Programs);

    private void RestoreWorkspace(AuthoringWorkspaceState state)
    {
        InvalidateContractFindings();
        var selected = SelectedProgram?.PlanId;
        RememberNodeSelection();
        Workspace = Workspace! with { Manifest = state.Manifest };
        Programs = state.Drafts;
        foreach (var draft in Programs)
            _documents[draft.PlanId].RestoreExternal(draft, _documents[draft.PlanId].SelectedNodeId);
        _selectedProgram = Programs.FirstOrDefault(p => string.Equals(p.PlanId, selected, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedProgram));
        RefreshMeasurePresentation();
        RestoreNodeSelection(SelectedDocument?.SelectedNodeId);
        RaiseSidecarProperties();
        RecomputeDocumentDirty();
    }

    private void RunCatalogEdit(string description, Action edit)
    {
        EnsureWritableWorkspace("edit workspace catalogs");
        var before = CaptureWorkspace();
        _catalogTransaction = true;
        try
        {
            edit();
            if (_workspaceHistory.Commit(description, before, CaptureWorkspace())) InvalidateContractFindings();
        }
        catch
        {
            if (!before.ContentEquals(CaptureWorkspace())) RestoreWorkspace(before);
            throw;
        }
        finally
        {
            _catalogTransaction = false;
            RecomputeDocumentDirty();
        }
    }
}
