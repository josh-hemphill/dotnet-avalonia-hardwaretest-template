namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private string _recipeSearch = string.Empty;
    private string _sequenceRename = string.Empty;
    private string _insertionPosition = "After selected";
    public IReadOnlyList<string> InsertionPositions { get; } = ["Before selected", "After selected", "End of section"];
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
    public string SequenceOperationTarget => SelectedSequence is { } row ? $"Selected: {row.Label}" : "No selected step";
    public string RemoveSequenceActionTitle => SelectedSequence?.Kind switch
    {
        SequenceRowKind.Repeat => "Remove loop, keep steps",
        SequenceRowKind.Cleanup => "Disable safe shutdown",
        _ => RemoveSelectedTitle
    };
    public bool CanEditSequence => Workspace is { IsReadOnly: false } && SelectedProgram is not null;
    public bool CanRenameSequence => CanEditSequence && (SelectedSequence?.Kind == SequenceRowKind.Metric
        || SelectedSetup is OperatorPromptSetup or OperatorInputSetup);
    public bool CanDuplicateSequence => CanEditSequence && (SelectedSequence?.Kind is SequenceRowKind.Metric or SequenceRowKind.Repeat
        || SelectedSetup is OperatorPromptSetup or OperatorInputSetup);
    public bool CanMoveSequence => CanEditSequence && SelectedSequence?.Section is SequenceSection.Setup or SequenceSection.Measure;
    public bool CanInsertRecipe => CanEditSequence && SelectedRecipe is not null && RecipePreflight() is null;
    public string SelectedRecipePrerequisites => SelectedRecipe is null ? "Search and choose a recipe."
        : RecipePreflight() ?? $"Ready: {SelectedRecipe.Summary} Insertion: {InsertionPosition}. {SequenceOperationTarget}.";

    private SequenceRow? InsertionTarget => SelectedRecipe?.Id == AuthoringRecipeIds.Repeat || InsertionPosition != "End of section"
        ? SelectedSequence : null;

    private string? RecipePreflight()
    {
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

    public void InsertSelectedRecipe() => SequenceTransaction(draft => AuthoringSequenceOperations.Insert(draft,
        SelectedRecipe?.Id ?? throw new AuthoringWorkspaceException("Choose a recipe."),
        InsertionTarget,
        InsertionPosition == "Before selected", SelectedInstrumentSlot), "Recipe inserted");
    public void RenameSelectedSequence() => SequenceTransaction(draft => AuthoringSequenceOperations.Rename(draft,
        SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step."), SequenceRename), "Step renamed");
    public void DuplicateSelectedSequence() => SequenceTransaction(draft => AuthoringSequenceOperations.Duplicate(draft,
        SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step.")), "Step duplicated");
    public void MoveSelectedSequence(int direction) => SequenceTransaction(draft => AuthoringSequenceOperations.Move(draft,
        SelectedSequence ?? throw new AuthoringWorkspaceException("Select a step."), direction), "Step moved");

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

    private void RaiseSequenceOperations()
    {
        OnPropertyChanged(nameof(SequenceOperationTarget));
        OnPropertyChanged(nameof(RemoveSequenceActionTitle));
        OnPropertyChanged(nameof(CanEditSequence));
        OnPropertyChanged(nameof(CanRenameSequence));
        OnPropertyChanged(nameof(CanDuplicateSequence));
        OnPropertyChanged(nameof(CanMoveSequence));
        OnPropertyChanged(nameof(CanInsertRecipe));
        OnPropertyChanged(nameof(SelectedRecipePrerequisites));
    }
}
