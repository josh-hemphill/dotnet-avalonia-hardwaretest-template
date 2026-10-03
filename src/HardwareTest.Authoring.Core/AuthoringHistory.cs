namespace HardwareTest.Authoring;

/// Isolated before/after values for a single program's edit history.
public sealed class AuthoringHistory
{
    internal sealed record Entry(string Description, AuthoringDocumentSnapshot Before,
        AuthoringDocumentSnapshot After, Guid? BeforeSelection, Guid? AfterSelection);

    private readonly Stack<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.TryPeek(out var entry) ? entry.Description : null;
    public string? RedoDescription => _redo.TryPeek(out var entry) ? entry.Description : null;

    internal void Commit(Entry entry)
    {
        _undo.Push(entry);
        _redo.Clear();
    }

    internal Entry? PeekUndo() => _undo.TryPeek(out var entry) ? entry : null;
    internal Entry? PeekRedo() => _redo.TryPeek(out var entry) ? entry : null;
    internal void DidUndo() => _redo.Push(_undo.Pop());
    internal void DidRedo() => _undo.Push(_redo.Pop());
    internal void CompleteSelection(AuthoringDocumentSnapshot current, Guid? selection)
    {
        if (_undo.TryPeek(out var entry) && entry.After.ContentEquals(current))
        {
            _undo.Pop();
            _undo.Push(entry with { AfterSelection = selection });
        }
    }
    internal void RebaseCurrent(AuthoringDocumentSnapshot current, AuthoringDocumentSnapshot persisted)
    {
        if (_undo.TryPeek(out var undo) && undo.After.ContentEquals(current))
        {
            _undo.Pop();
            _undo.Push(undo with { After = persisted });
        }
        if (_redo.TryPeek(out var redo) && redo.Before.ContentEquals(current))
        {
            _redo.Pop();
            _redo.Push(redo with { Before = persisted });
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
