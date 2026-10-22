namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private sealed record MovementKey(Guid Session, AuthoringDocumentSnapshot? Snapshot, long? Revision, Guid? Node, Guid? Destination, bool Editable);
    private sealed record MovementAvailability(bool Up, string UpReason, bool Down, string DownReason, bool ToSection, string SectionReason)
    {
        public ProgramDraft? UpDraft { get; init; }
        public ProgramDraft? DownDraft { get; init; }
        public ProgramDraft? SectionDraft { get; init; }
    }
    private static MovementAvailability MovementUnavailable(string reason) => new(false, reason, false, reason, false, reason);
    private MovementAvailability _movementAvailability = MovementUnavailable("Select an item to see where it can move.");
    private MovementKey? _movementKey;
    private Action<Action>? _movementDispatch;
    private Func<bool>? _movementOwner;
    private long _movementGeneration;
    private bool _movementRunning;
    private bool _movementPending;
    private bool _movementQueued;
    private bool _movementStopped;
    private TaskCompletionSource _movementIdle = CompletedMovement();

    public bool CanMoveSequenceUp => _movementAvailability.Up;
    public bool CanMoveSequenceDown => _movementAvailability.Down;
    public string MoveSequenceUpReason => _movementAvailability.UpReason;
    public string MoveSequenceDownReason => _movementAvailability.DownReason;
    public string MoveToSectionReason => _movementAvailability.SectionReason;

    public void ConfigureMovementAvailability(Action<Action> dispatch, Func<bool> ownerIsCurrent)
    {
        _movementGeneration++;
        _movementIdle.TrySetResult();
        _movementRunning = false;
        _movementQueued = false;
        _movementPending = false;
        _movementDispatch = dispatch;
        _movementOwner = ownerIsCurrent;
        _movementStopped = false;
        _movementKey = null;
        RequestMovementAvailability();
    }

    public void StopMovementAvailability()
    {
        _movementGeneration++;
        _movementStopped = true;
        _movementPending = false;
        _movementIdle.TrySetResult();
    }

    public Task WaitForMovementAvailabilityAsync() => _movementIdle.Task;

    private MovementKey CurrentMovementKey() => new(WorkspaceSessionId, SelectedDocument?.Snapshot, SelectedDocument?.Revision,
        SelectedSequence?.NodeId, SelectedSectionDestination?.NodeId, CanEditSequence);

    private void RequestMovementAvailability()
    {
        if (_movementStopped) return;
        var key = CurrentMovementKey();
        if (key == _movementKey) return;
        _movementKey = key;
        if (!key.Editable || key.Snapshot is null || key.Node is null)
        {
            _movementPending = false;
            _movementIdle.TrySetResult();
            _movementAvailability = MovementUnavailable(Workspace?.IsReadOnly == true ? "This workspace is read-only."
                : "Select an editable step or section.");
            PublishMovementAvailability();
            return;
        }
        if (_movementDispatch is null)
        {
            _movementAvailability = EvaluateMovement(key);
            PublishMovementAvailability();
            return;
        }
        _movementAvailability = MovementUnavailable("Checking move order and dependencies…");
        _movementPending = true;
        if (_movementIdle.Task.IsCompleted) _movementIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PublishMovementAvailability();
        QueueMovementAvailability();
    }

    private void QueueMovementAvailability()
    {
        if (_movementRunning || _movementQueued || _movementStopped) return;
        _movementQueued = true;
        var generation = _movementGeneration;
        try
        {
            _movementDispatch!(() =>
        {
            if (generation != _movementGeneration) return;
            _movementQueued = false;
            if (_movementStopped || _movementOwner?.Invoke() != true)
            {
                _movementPending = false;
                _movementIdle.TrySetResult();
                return;
            }
            _movementPending = false;
            if (_movementKey is not { Editable: true, Snapshot: not null, Node: not null } key)
            {
                _movementIdle.TrySetResult();
                return;
            }
            _movementRunning = true;
            _ = EvaluateMovementAsync(key, generation, _movementDispatch!);
        });
        }
        catch (Exception error)
        {
            _movementQueued = false;
            _movementPending = false;
            _movementAvailability = MovementUnavailable($"Move check could not start: {error.Message}");
            _movementIdle.TrySetResult();
            PublishMovementAvailability();
        }
    }

    private async Task EvaluateMovementAsync(MovementKey key, long generation, Action<Action> dispatch)
    {
        MovementAvailability result;
        try { result = await Task.Run(() => EvaluateMovement(key)).ConfigureAwait(false); }
        catch (Exception error) { result = MovementUnavailable($"Move availability could not be checked: {error.Message}"); }
        try
        {
            dispatch(() =>
        {
            if (generation != _movementGeneration) return;
            _movementRunning = false;
            if (_movementStopped || _movementOwner?.Invoke() != true) { _movementPending = false; _movementIdle.TrySetResult(); return; }
            if (key == CurrentMovementKey())
            {
                _movementAvailability = result;
                PublishMovementAvailability();
            }
            else RequestMovementAvailability();
            if (_movementPending) QueueMovementAvailability();
            else if (!_movementRunning && !_movementQueued && !_movementPending) _movementIdle.TrySetResult();
        });
        }
        catch
        {
            // The dispatcher may have shut down. A later Configure starts a fresh generation.
            if (generation == _movementGeneration) _movementIdle.TrySetResult();
        }
    }

    private static MovementAvailability EvaluateMovement(MovementKey key)
    {
        if (key.Snapshot is null || !key.Editable) return MovementUnavailable("Select an editable step or section.");
        var draft = key.Snapshot.Restore();
        var rows = AuthoringSequence.Flatten(draft);
        var selected = rows.FirstOrDefault(row => row.NodeId == key.Node);
        if (selected is null) return MovementUnavailable("Select an item that is still in the plan.");
        if (selected.Section == SequenceSection.Cleanup) return MovementUnavailable("Shutdown stays at the end of the plan.");
        var siblings = rows.Where(row => row.IsSelectable && row.Section == selected.Section && row.IndexPath.Count == selected.IndexPath.Count
            && row.IndexPath.SkipLast(1).SequenceEqual(selected.IndexPath.SkipLast(1))).ToArray();
        var index = Array.FindIndex(siblings, row => row.NodeId == key.Node);
        (bool Allowed, string Reason, ProgramDraft? Draft) Direction(int direction)
        {
            var next = index + direction;
            if (next < 0 || next >= siblings.Length)
            {
                var position = direction < 0 ? "first" : "last";
                if (selected.Section == SequenceSection.Setup)
                    return (false, $"Already {position} in Setup.", null);
                if (selected.IndexPath.Count == 1)
                    return (false, $"Already {position} among test steps. {(direction < 0 ? "Setup stays first." : "Shutdown stays last.")}", null);
                var parentPath = selected.IndexPath.SkipLast(1);
                var parent = rows.FirstOrDefault(row => row.Section == selected.Section && row.IndexPath.SequenceEqual(parentPath));
                return (false, $"Already {position} in {parent?.Label ?? "this group"}.", null);
            }
            try
            {
                var moved = AuthoringSequenceOperations.Move(draft, selected, direction);
                return (true, $"Move {(direction < 0 ? "before" : "after")} {siblings[next].Label}.", moved);
            }
            catch (Exception error) when (error is AuthoringWorkspaceException or NotSupportedException) { return (false, error.Message, null); }
        }
        var up = Direction(-1);
        var down = Direction(1);
        var sectionAllowed = false;
        ProgramDraft? sectionDraft = null;
        string sectionReason;
        if (selected.Section != SequenceSection.Measure) sectionReason = "Setup steps stay in Setup; shutdown stays at the end.";
        else
        {
            try
            {
                var destination = key.Destination is { } id ? rows.FirstOrDefault(row => row.NodeId == id) : null;
                var parent = selected.IndexPath.SkipLast(1).ToArray();
                var destinationPath = destination?.IndexPath ?? [];
                if (parent.SequenceEqual(destinationPath) && index == siblings.Length - 1)
                    sectionReason = "Already at the end of this destination. Choose another section.";
                else
                {
                    sectionDraft = AuthoringSequenceOperations.MoveToSection(draft, selected, key.Destination);
                    sectionAllowed = true;
                    sectionReason = "Move to the end of the selected destination; execution order is preserved only when dependencies allow it.";
                }
            }
            catch (Exception error) when (error is AuthoringWorkspaceException or NotSupportedException) { sectionReason = error.Message; }
        }
        return new(up.Allowed, up.Reason, down.Allowed, down.Reason, sectionAllowed, sectionReason)
        { UpDraft = up.Draft, DownDraft = down.Draft, SectionDraft = sectionDraft };
    }

    private void ApplyPreparedMove(int? direction)
    {
        if (_movementStopped || _movementOwner?.Invoke() == false) return;
        if (_movementKey != CurrentMovementKey()) RequestMovementAvailability();
        var prepared = direction switch { -1 => _movementAvailability.UpDraft, 1 => _movementAvailability.DownDraft, _ => _movementAvailability.SectionDraft };
        if (prepared is null)
        {
            var reason = direction switch { -1 => MoveSequenceUpReason, 1 => MoveSequenceDownReason, _ => MoveToSectionReason };
            Error = $"Cannot move{(direction == -1 ? " up" : direction == 1 ? " down" : " to section")}: {reason}";
            return;
        }
        if (direction is null && _movementKey?.Destination is { } destination) _collapsedSectionIds.Remove(destination);
        SequenceTransaction(_ => prepared, "Item moved");
    }

    private void PublishMovementAvailability()
    {
        foreach (var property in new[] { nameof(CanMoveSequenceUp), nameof(CanMoveSequenceDown), nameof(MoveSequenceUpReason),
            nameof(MoveSequenceDownReason), nameof(CanMoveToSection), nameof(MoveToSectionReason) }) OnPropertyChanged(property);
    }

    private static TaskCompletionSource CompletedMovement()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
