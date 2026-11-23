namespace HardwareTest.Authoring;

/// Owns isolated current content, saved baselines, selection and history for one program.
public sealed class AuthoringDocumentSession
{
    private Guid? _selectedNodeId;

    public AuthoringDocumentSession(ProgramDraft draft, bool isSaved = true, bool isReadOnly = false, long revision = 0)
    {
        Snapshot = AuthoringDocumentSnapshot.Capture(draft);
        IsReadOnly = isReadOnly;
        Revision = revision;
        if (isSaved) MarkSaved();
    }

    public ProgramDraft Draft => Snapshot.Restore();
    public AuthoringDocumentSnapshot Snapshot { get; private set; }
    public AuthoringHistory History { get; } = new();
    public bool IsReadOnly { get; }
    public long Revision { get; private set; }
    public string? SavedPlanIdentity { get; private set; }
    public string? SavedSidecarIdentity { get; private set; }
    public bool IsPlanDirty => SavedPlanIdentity != Snapshot.PlanIdentity;
    public bool IsSidecarDirty => SavedSidecarIdentity != Snapshot.SidecarIdentity;
    public bool IsDirty => IsPlanDirty || IsSidecarDirty;
    public bool CanUndo => !IsReadOnly && History.PeekUndo() is { } entry && Snapshot.ContentEquals(entry.After);
    public bool CanRedo => !IsReadOnly && History.PeekRedo() is { } entry && Snapshot.ContentEquals(entry.Before);

    /// Compares a compatibility draft against saved baselines without changing history or read-only content.
    public (bool PlanDirty, bool SidecarDirty) GetDirtyState(ProgramDraft draft)
    {
        var actual = AuthoringDocumentSnapshot.Capture(draft);
        if (draft.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot compare a foreign program.");
        return (SavedPlanIdentity != actual.PlanIdentity, SavedSidecarIdentity != actual.SidecarIdentity);
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
        if (Snapshot.ContentEquals(after)) return false;
        var identities = AuthoringDependencyIndex.Build(after.Restore()).Nodes.Select(node => node.NodeId).ToArray();
        if (identities.Any(id => id == Guid.Empty) || identities.Distinct().Count() != identities.Length)
            throw new InvalidOperationException("An edit must preserve unique, nonempty node identities.");
        var afterSelection = _selectedNodeId is { } selected && ContainsNode(after.Restore(), selected) ? selected : (Guid?)null;
        History.Commit(new AuthoringHistory.Entry(description, Snapshot, after, _selectedNodeId, afterSelection));
        Snapshot = after;
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
        Snapshot = entry.Before;
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
        Snapshot = entry.After;
        _selectedNodeId = entry.AfterSelection;
        History.DidRedo();
        Revision++;
        return true;
    }

    /// Call only after persistence succeeds; sidecar-only saves leave the plan baseline intact.
    public void MarkSaved(bool plan = true, bool sidecar = true)
    {
        if (plan) SavedPlanIdentity = Snapshot.PlanIdentity;
        if (sidecar) SavedSidecarIdentity = Snapshot.SidecarIdentity;
    }

    /// Accepts normalized persisted content only after a successful save, preserving adjacent history boundaries.
    public void AcceptSavedContent(ProgramDraft persisted, bool plan = true, bool sidecar = true)
    {
        EnsureWritable();
        if (persisted.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot accept a foreign saved program.");
        var after = AuthoringDocumentSnapshot.Capture(persisted);
        if (!Snapshot.ContentEquals(after))
        {
            History.RebaseCurrent(Snapshot, after);
            Snapshot = after;
            Revision++;
        }
        MarkSaved(plan, sidecar);
    }

    public void RestoreExternal(ProgramDraft draft, Guid? selection)
    {
        EnsureWritable();
        if (draft.PlanId != Draft.PlanId) throw new InvalidOperationException("Cannot restore a foreign program.");
        var next = AuthoringDocumentSnapshot.Capture(draft);
        if (!Snapshot.ContentEquals(next))
        {
            Snapshot = next;
            Revision++;
        }
        SelectedNodeId = selection;
    }

    public void ClearHistory() => History.Clear();
    internal void CompleteEditSelection() => History.CompleteSelection(Snapshot, _selectedNodeId);
    private void EnsureWritable()
    {
        if (IsReadOnly) throw new InvalidOperationException("This program is read-only.");
    }
    private static InvalidOperationException StaleHistory()
        => new("History cannot be restored over intervening edits. Undo the newer workspace operation first.");

    internal static bool ContainsNode(ProgramDraft draft, Guid id)
        => draft.Setup.Any(action => action.NodeId == id) || ContainsNode(draft.Measure, id) || draft.Cleanup.NodeId == id;

    private static bool ContainsNode(IEnumerable<MeasureNode> nodes, Guid id)
        => nodes.Any(node => node.NodeId == id || (node is RepeatNode repeat && ContainsNode(repeat.Children, id)) || (node is SectionNode section && ContainsNode(section.Children, id)));
}
