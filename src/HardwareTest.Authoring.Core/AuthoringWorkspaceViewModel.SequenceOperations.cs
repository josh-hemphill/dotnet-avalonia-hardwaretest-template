namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string _recipeSearch = string.Empty;
    private string _sequenceRename = string.Empty;
    private Guid? _renameSourceId;
    private string? _renameSourceLabel;
    private string _insertionPosition = "After selected";
    public IReadOnlyList<string> InsertionPositions { get; } = ["End of matching phase", "Before selected", "After selected", "End of section", "End of test steps"];
    public string InsertionPosition
    {
        get => _insertionPosition;
        set { if (SetField(ref _insertionPosition, value)) RaiseSequenceOperations(); }
    }
    public string RecipeSearch
    {
        get => _recipeSearch;
        set
        {
            if (!SetField(ref _recipeSearch, value)) return;
            OnPropertyChanged(nameof(Recipes));
            OnPropertyChanged(nameof(SelectedRecipe));
            RaiseSequenceOperations();
        }
    }
    public string SequenceRename
    {
        get => _sequenceRename;
        set => SetField(ref _sequenceRename, value);
    }
    public string SequenceOperationTarget => IsSequenceEmpty ? "No steps yet. The first step goes into its matching phase."
        : SelectedSequence is { } row ? $"Selected: {row.Label}" : "No selected step";
    public string RemoveSequenceActionTitle => SelectedSequence?.Kind switch
    {
        SequenceRowKind.Repeat => "Remove loop, keep steps",
        SequenceRowKind.Section => "Remove section, keep steps",
        SequenceRowKind.Cleanup => "Disable safe shutdown",
        _ => RemoveSelectedTitle
    };
    public bool CanEditSequence => Workspace is { IsReadOnly: false } && SelectedProgram is not null;
    public bool HasSelectedPlan => SelectedProgram is not null;
    public bool NeedsFirstPlan => HasWorkspace && SelectedProgram is null;
    public bool IsSequenceEmpty => IsEmptySequence(SelectedProgram);
    private static bool IsEmptySequence(ProgramDraft? draft) => draft is { Setup.Count: 0, Cleanup.IncludeSafeShutdown: false }
        && (draft.Measure.Count == 0 || draft.Measure is [SectionNode { Name: AuthoringMeasureSections.DefaultSectionName, Children.Count: 0 }]);
    public bool HasSequenceContent => HasSelectedPlan && !IsSequenceEmpty;
    public bool EmptyPlanStepHintVisible => IsSequenceEmpty && SelectedSequence?.Kind != SequenceRowKind.Section;
    public bool CanRenameSequence => CanEditSequence && (SelectedSequence?.Kind is SequenceRowKind.Metric or SequenceRowKind.Section
        || SelectedSetup is OperatorPromptSetup or OperatorInputSetup);
    public bool CanDuplicateSequence => CanEditSequence && (SelectedSequence?.Kind is SequenceRowKind.Metric or SequenceRowKind.Repeat or SequenceRowKind.Section
        || SelectedSetup is OperatorPromptSetup or OperatorInputSetup);
    public bool CanMoveSequence => CanEditSequence && SelectedSequence?.Section is SequenceSection.Setup or SequenceSection.Measure;
    public bool CanInsertRecipe => CanEditSequence && SelectedRecipe is not null && RecipePreflight() is null;
    public string SelectedRecipePrerequisites => SelectedRecipe is null ? "Search and choose a recipe."
        : RecipePreflight() ?? $"Ready: {SelectedRecipe.Summary} Insertion: {InsertionPosition}. {SequenceOperationTarget}.";

    private SequenceRow? InsertionTarget => SelectedRecipe?.Id == AuthoringRecipeIds.Repeat ? SelectedSequence
        : IsEmptySequence(SelectedProgram) || InsertionPosition is "End of matching phase" or "End of test steps" or "End of Measure" ? null
        : InsertionPosition == "End of section"
            ? SelectedRecipe?.Id is AuthoringRecipeIds.Identity or AuthoringRecipeIds.Prompt or AuthoringRecipeIds.Input
                ? null : EndOfSelectedSection()
            : SelectedSequence;

    private string? RecipePreflight()
    {
        if (_recipeAvailabilityDispatch is not null) return CachedRecipeBlocker();
        if (!CanEditSequence) return "Open an editable program before inserting.";
        if (SelectedRecipe is null) return "Choose a recipe.";
        try
        {
            _ = AuthoringSequenceOperations.Insert(SelectedProgram!, SelectedRecipe.Id,
                InsertionTarget,
                InsertionPosition == "Before selected", SelectedInstrumentSlot);
            return null;
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or NotSupportedException)
        {
            return error.Message;
        }
    }

    public void InsertSelectedRecipe()
    {
        if (_recipeAvailabilityDispatch is not null)
        {
            var blocker = CachedRecipeBlocker();
            if (blocker is not null || _recipeAvailability.Prepared is not { } prepared)
            {
                Error = blocker ?? "Choose a recipe and wait for its prerequisites to be checked.";
                return;
            }
            if (InsertionTarget is { IsSection: true, NodeId: { } sectionId }) _collapsedSectionIds.Remove(sectionId);
            SequenceTransaction(_ => prepared, "Recipe inserted");
            return;
        }
        if (InsertionTarget is { IsSection: true, NodeId: { } id }) _collapsedSectionIds.Remove(id);
        SequenceTransaction(draft => AuthoringSequenceOperations.Insert(draft,
        SelectedRecipe?.Id ?? throw new AuthoringWorkspaceException("Choose a recipe."),
        InsertionTarget,
        InsertionPosition == "Before selected", SelectedInstrumentSlot), "Recipe inserted");
    }
    public void RenameSelectedSequence() => SequenceTransaction(draft => AuthoringSequenceOperations.Rename(draft,
        SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step."), SequenceRename), "Step renamed");
    public void DuplicateSelectedSequence() => SequenceTransaction(draft => AuthoringSequenceOperations.Duplicate(draft,
        SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step.")), "Step duplicated");
    public void MoveSelectedSequence(int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        ApplyPreparedMove(direction);
    }

    private long _sequenceDropGeneration;
    private Func<Func<ProgramDraft>, Task<ProgramDraft>> _evaluateSequenceDrop = evaluate => Task.Run(evaluate);
    public Task SequenceMoveTask { get; private set; } = Task.CompletedTask;
    public void ReorderSequence(Guid sourceId, Guid destinationId)
        => SequenceMoveTask = ReorderSequenceAsync(sourceId, destinationId);

    private async Task ReorderSequenceAsync(Guid sourceId, Guid destinationId)
    {
        var generation = ++_sequenceDropGeneration;
        var movementGeneration = _movementGeneration;
        if (!CanEditSequence) { ReportSequenceDropFailure("Open an editable plan before moving items."); return; }
        var source = SequenceItems.FirstOrDefault(row => row.NodeId == sourceId);
        var destination = SequenceItems.FirstOrDefault(row => row.NodeId == destinationId);
        if (source is null || destination is null) { ReportSequenceDropFailure("The dragged item or destination no longer exists."); return; }
        SelectSequence(SequenceItems.ToList().IndexOf(source));
        var session = SelectedDocument!;
        var snapshot = session.Snapshot;
        var revision = session.Revision;
        var owner = WorkspaceSessionId;
        bool Current() => !_movementStopped && _movementOwner?.Invoke() != false
            && movementGeneration == _movementGeneration && generation == _sequenceDropGeneration && owner == WorkspaceSessionId
            && ReferenceEquals(session, SelectedDocument) && ReferenceEquals(snapshot, session.Snapshot) && revision == session.Revision
            && SelectedSequence?.NodeId == sourceId;
        Status = "Checking drop order and dependencies…";
        var dispatch = _movementDispatch;
        ProgramDraft? updated = null;
        Exception? failure = null;
        try
        {
            ProgramDraft Evaluate() => AuthoringSequenceOperations.Reorder(snapshot.Restore(), source, destination);
            updated = dispatch is null ? Evaluate() : await _evaluateSequenceDrop(Evaluate).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        void Publish()
        {
            if (!Current())
            {
                if (!_movementStopped && _movementOwner?.Invoke() != false && owner == WorkspaceSessionId
                    && movementGeneration == _movementGeneration && generation == _sequenceDropGeneration
                    && Status == "Checking drop order and dependencies…")
                    Status = "Drop cancelled because the plan or selection changed.";
                return;
            }
            try
            {
                if (failure is not null) ReportSequenceDropFailure(PersistenceError(failure));
                else SequenceTransaction(_ => updated!, "Item moved");
            }
            catch (Exception error)
            {
                if (Current()) ReportSequenceDropFailure(PersistenceError(error));
            }
        }
        if (dispatch is null) { Publish(); return; }
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            dispatch(() =>
            {
                try { Publish(); }
                finally { published.TrySetResult(); }
            });
            await published.Task.ConfigureAwait(false);
        }
        catch
        {
            // A stopped dispatcher cannot accept UI publication. Leave the current owner untouched.
            published.TrySetResult();
        }
    }

    public void ReportSequenceDropFailure(string reason)
    {
        _sequenceDropGeneration++;
        Status = "Drop not applied.";
        Error = "Cannot drop here: " + reason;
    }

    private void SequenceTransaction(Func<ProgramDraft, ProgramDraft> mutate, string status)
    {
        EnsureWritableWorkspace("edit the sequence");
        var draft = SelectedProgram ?? throw new AuthoringWorkspaceException("Select a program.");
        EnsurePresentedHistoryCurrent();
        try
        {
            var updated = mutate(draft);
            if (AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(updated)))
            {
                Status = "No change to the sequence.";
                Error = null;
                return;
            }
            var existingIds = AuthoringDependencyIndex.Build(draft).Nodes.Select(node => node.NodeId).ToHashSet();
            ReplaceSelected(updated);
            var inserted = SequenceItems.ToList().FindIndex(row => row.NodeId is { } id && !existingIds.Contains(id));
            if (inserted >= 0) SelectSequence(inserted);
            SelectedDocument?.CompleteEditSelection();
            Status = status;
            Error = null;
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or NotSupportedException)
        {
            Error = error.Message;
        }
    }

    private void SynchronizeSequenceRename()
    {
        var selected = SelectedSequence;
        var id = selected?.NodeId;
        var label = selected?.Label;
        if (_renameSourceId == id && _renameSourceLabel == label) return;
        _renameSourceId = id;
        _renameSourceLabel = label;
        SetField(ref _sequenceRename, label ?? string.Empty, nameof(SequenceRename));
    }

    private void RaiseSequenceOperations()
    {
        SynchronizeSequenceRename();
        RequestMovementAvailability();
        RequestRecipeAvailability();
        RaiseSectionProperties();
        OnPropertyChanged(nameof(SequenceOperationTarget));
        OnPropertyChanged(nameof(RemoveSequenceActionTitle));
        OnPropertyChanged(nameof(CanEditSequence));
        OnPropertyChanged(nameof(HasSelectedPlan));
        OnPropertyChanged(nameof(NeedsFirstPlan));
        OnPropertyChanged(nameof(IsSequenceEmpty));
        OnPropertyChanged(nameof(HasSequenceContent));
        OnPropertyChanged(nameof(EmptyPlanStepHintVisible));
        OnPropertyChanged(nameof(CanRenameSequence));
        OnPropertyChanged(nameof(CanDuplicateSequence));
        OnPropertyChanged(nameof(CanMoveSequence));
        OnPropertyChanged(nameof(CanInsertRecipe));
        OnPropertyChanged(nameof(SelectedRecipePrerequisites));
    }
}
