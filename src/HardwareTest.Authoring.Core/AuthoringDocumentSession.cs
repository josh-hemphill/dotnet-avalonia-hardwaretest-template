namespace HardwareTest.Authoring;

/// Owns isolated current content, saved baselines, selection and history for one program.
public sealed class AuthoringDocumentSession
{
    private AuthoringDocumentSnapshot _current;
    private string? _savedPlan;
    private string? _savedSidecar;
    private Guid? _selectedNodeId;

    public AuthoringDocumentSession(ProgramDraft draft, bool isSaved = true, bool isReadOnly = false)
    {
        _current = AuthoringDocumentSnapshot.Capture(draft);
        IsReadOnly = isReadOnly;
        if (isSaved) MarkSaved();
    }

    public ProgramDraft Draft => _current.Restore();
    public AuthoringDocumentSnapshot Snapshot => _current;
    public AuthoringHistory History { get; } = new();
    public bool IsReadOnly { get; }
    public long Revision { get; private set; }
    public bool IsPlanDirty => _savedPlan != _current.PlanIdentity;
    public bool IsSidecarDirty => _savedSidecar != _current.SidecarIdentity;
    public bool IsDirty => IsPlanDirty || IsSidecarDirty;
    public bool CanUndo => !IsReadOnly && History.PeekUndo() is { } entry && _current.ContentEquals(entry.After);
    public bool CanRedo => !IsReadOnly && History.PeekRedo() is { } entry && _current.ContentEquals(entry.Before);

    /// Compares a compatibility draft against saved baselines without changing history or read-only content.
    public (bool PlanDirty, bool SidecarDirty) GetDirtyState(ProgramDraft draft)
    {
        var actual = AuthoringDocumentSnapshot.Capture(draft);
        if (draft.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot compare a foreign program.");
        return (_savedPlan != actual.PlanIdentity, _savedSidecar != actual.SidecarIdentity);
    }

    public Guid? SelectedNodeId
    {
        get => _selectedNodeId;
        set => _selectedNodeId = value is { } id && ContainsNode(Draft, id) ? id : null;
    }

    public bool ApplyEdit(string description, Func<ProgramDraft, ProgramDraft> edit, Guid? targetNodeId = null, long? expectedRevision = null)
        => AuthoringEditService.Apply(this, description, edit, targetNodeId, expectedRevision);

    internal bool CommitEdit(string description, Func<ProgramDraft, ProgramDraft> edit, Guid? targetNodeId, long? expectedRevision)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(edit);
        if (expectedRevision is { } revision && revision != Revision)
            throw new InvalidOperationException("The program changed after this edit was prepared. Refresh the edit target and try again.");
        var draft = Draft;
        if (targetNodeId is { } target && !ContainsNode(draft, target))
            throw new InvalidOperationException("The edit target was deleted or belongs to another program.");
        var after = AuthoringDocumentSnapshot.Capture(edit(draft));
        if (after.Restore().PlanId != draft.PlanId)
            throw new InvalidOperationException("An edit cannot change the document's program identity.");
        if (_current.ContentEquals(after)) return false;
        var identities = AuthoringDependencyIndex.Build(after.Restore()).Nodes.Select(node => node.NodeId).ToArray();
        if (identities.Any(id => id == Guid.Empty) || identities.Distinct().Count() != identities.Length)
            throw new InvalidOperationException("An edit must preserve unique, nonempty node identities.");
        var afterSelection = _selectedNodeId is { } selected && ContainsNode(after.Restore(), selected) ? selected : (Guid?)null;
        History.Commit(new AuthoringHistory.Entry(description, _current, after, _selectedNodeId, afterSelection));
        _current = after;
        _selectedNodeId = afterSelection;
        Revision++;
        return true;
    }

    public bool Undo()
    {
        EnsureWritable();
        if (!History.CanUndo) return false;
        if (!CanUndo) throw StaleHistory();
        var entry = History.PeekUndo()!;
        _current = entry.Before;
        _selectedNodeId = entry.BeforeSelection;
        History.DidUndo();
        Revision++;
        return true;
    }

    public bool Redo()
    {
        EnsureWritable();
        if (!History.CanRedo) return false;
        if (!CanRedo) throw StaleHistory();
        var entry = History.PeekRedo()!;
        _current = entry.After;
        _selectedNodeId = entry.AfterSelection;
        History.DidRedo();
        Revision++;
        return true;
    }

    /// Call only after persistence succeeds; sidecar-only saves leave the plan baseline intact.
    public void MarkSaved(bool plan = true, bool sidecar = true)
    {
        if (plan) _savedPlan = _current.PlanIdentity;
        if (sidecar) _savedSidecar = _current.SidecarIdentity;
    }

    /// Accepts normalized persisted content only after a successful save, preserving adjacent history boundaries.
    public void AcceptSavedContent(ProgramDraft persisted, bool plan = true, bool sidecar = true)
    {
        EnsureWritable();
        if (persisted.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot accept a foreign saved program.");
        var after = AuthoringDocumentSnapshot.Capture(persisted);
        if (!_current.ContentEquals(after))
        {
            History.RebaseCurrent(_current, after);
            _current = after;
            Revision++;
        }
        MarkSaved(plan, sidecar);
    }

    public void RestoreExternal(ProgramDraft draft, Guid? selection)
    {
        EnsureWritable();
        if (draft.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot restore a foreign program.");
        var next = AuthoringDocumentSnapshot.Capture(draft);
        if (!_current.ContentEquals(next))
        {
            _current = next;
            Revision++;
        }
        SelectedNodeId = selection;
    }

    public void ClearHistory() => History.Clear();
    internal void CompleteEditSelection() => History.CompleteSelection(_current, _selectedNodeId);
    private void EnsureWritable()
    {
        if (IsReadOnly) throw new InvalidOperationException("This program is read-only.");
    }
    private static InvalidOperationException StaleHistory()
        => new("History cannot be restored over intervening edits. Undo the newer workspace operation first.");

    internal static bool ContainsNode(ProgramDraft draft, Guid id)
        => draft.Setup.Any(action => action.NodeId == id) || ContainsNode(draft.Measure, id) || draft.Cleanup.NodeId == id;

    private static bool ContainsNode(IEnumerable<MeasureNode> nodes, Guid id)
        => nodes.Any(node => node.NodeId == id || node is RepeatNode repeat && ContainsNode(repeat.Children, id));
}
