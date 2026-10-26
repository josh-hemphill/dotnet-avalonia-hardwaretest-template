namespace HardwareTest.Authoring;

internal sealed record AuthoringRecipeAvailabilityInput(Guid Session, AuthoringDocumentSnapshot? Snapshot,
    Guid? SelectedNode, string? RecipeId, string InsertionPosition, string InstrumentSlot, bool Editable);
internal sealed record AuthoringRecipeAvailabilityResult(string? Blocker, ProgramDraft? Prepared);

public sealed partial class AuthoringWorkspaceViewModel
{
    private Action<Action>? _recipeAvailabilityDispatch;
    private Func<bool>? _recipeAvailabilityOwner;
    private Func<AuthoringRecipeAvailabilityInput, AuthoringRecipeAvailabilityResult> _evaluateRecipeAvailability = EvaluateRecipeAvailability;
    private AuthoringRecipeAvailabilityInput? _requestedRecipeInput;
    private AuthoringRecipeAvailabilityInput? _publishedRecipeInput;
    private AuthoringRecipeAvailabilityResult _recipeAvailability = new("Choose a recipe.", null);
    private long _recipeAvailabilityGeneration;
    private bool _recipeAvailabilityQueued, _recipeAvailabilityRunning, _recipeAvailabilityPending, _recipeAvailabilityStopped;
    private TaskCompletionSource _recipeAvailabilityIdle = CompletedRecipeAvailability();

    public void ConfigureRecipeAvailability(Action<Action> dispatch, Func<bool> ownerIsCurrent)
        => ConfigureRecipeAvailability(dispatch, ownerIsCurrent, EvaluateRecipeAvailability);

    internal void ConfigureRecipeAvailability(Action<Action> dispatch, Func<bool> ownerIsCurrent,
        Func<AuthoringRecipeAvailabilityInput, AuthoringRecipeAvailabilityResult> evaluate)
    {
        if (_recipeAvailabilityIdle.Task.IsFaulted) _recipeAvailabilityRunning = false;
        _recipeAvailabilityDispatch = dispatch;
        _recipeAvailabilityOwner = ownerIsCurrent;
        _evaluateRecipeAvailability = evaluate;
        _recipeAvailabilityStopped = false;
        _requestedRecipeInput = null;
        RequestRecipeAvailability();
    }

    public void StopRecipeAvailability()
    {
        _recipeAvailabilityStopped = true;
        _recipeAvailabilityGeneration++;
        _recipeAvailabilityPending = false;
        _recipeAvailabilityIdle.TrySetResult();
    }

    public Task WaitForRecipeAvailabilityAsync() => _recipeAvailabilityIdle.Task;

    private AuthoringRecipeAvailabilityInput CurrentRecipeInput()
        => new(WorkspaceSessionId, SelectedDocument?.Snapshot, SelectedSequence?.NodeId,
            SelectedRecipe?.Id, InsertionPosition, SelectedInstrumentSlot, CanEditSequence);

    private string? CachedRecipeBlocker()
    {
        if (_recipeAvailabilityStopped || _recipeAvailabilityOwner?.Invoke() != true)
            return "Recipe insertion is unavailable for an inactive workspace.";
        var input = CurrentRecipeInput();
        if (!input.Editable) return "Open an editable program before inserting.";
        if (input.RecipeId is null) return "Choose a recipe.";
        return !_recipeAvailabilityStopped && input == _publishedRecipeInput ? _recipeAvailability.Blocker
            : "Checking recipe prerequisites and execution order…";
    }

    private void RequestRecipeAvailability()
    {
        // CLI callers keep their explicit synchronous preflight. Desktop hosts configure
        // this before controls bind so no getter performs graph validation or creates steps.
        if (_recipeAvailabilityDispatch is null || _recipeAvailabilityStopped) return;
        var input = CurrentRecipeInput();
        if (input == _requestedRecipeInput) return;
        _requestedRecipeInput = input;
        _recipeAvailabilityGeneration++;
        _publishedRecipeInput = null;
        _recipeAvailabilityPending = false;
        if (!input.Editable || input.Snapshot is null || input.RecipeId is null)
        {
            _recipeAvailability = new(!input.Editable ? "Open an editable program before inserting." : "Choose a recipe.", null);
            _publishedRecipeInput = input;
            PublishRecipeAvailability();
            CompleteRecipeAvailabilityIfIdle();
            return;
        }
        _recipeAvailabilityPending = true;
        if (_recipeAvailabilityIdle.Task.IsCompleted) _recipeAvailabilityIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PublishRecipeAvailability();
        QueueRecipeAvailability();
    }

    private void QueueRecipeAvailability()
    {
        if (_recipeAvailabilityQueued || _recipeAvailabilityRunning || _recipeAvailabilityStopped) return;
        _recipeAvailabilityQueued = true;
        try
        {
            _recipeAvailabilityDispatch!(() =>
            {
                _recipeAvailabilityQueued = false;
                if (_recipeAvailabilityRunning) return;
                if (_recipeAvailabilityStopped || _recipeAvailabilityOwner?.Invoke() != true)
                { _recipeAvailabilityPending = false; _recipeAvailabilityIdle.TrySetResult(); return; }
                var input = _requestedRecipeInput;
                if (input is not { Editable: true, Snapshot: not null, RecipeId: not null })
                { _recipeAvailabilityPending = false; _recipeAvailabilityIdle.TrySetResult(); return; }
                _recipeAvailabilityPending = false;
                _recipeAvailabilityRunning = true;
                _ = EvaluateRecipeAvailabilityAsync(input, _recipeAvailabilityGeneration, _evaluateRecipeAvailability);
            });
        }
        catch (Exception error)
        {
            _recipeAvailabilityQueued = false;
            _recipeAvailabilityPending = false;
            _recipeAvailability = new($"Recipe prerequisites could not be checked: {error.Message}", null);
            _publishedRecipeInput = _requestedRecipeInput;
            PublishRecipeAvailability();
            CompleteRecipeAvailabilityIfIdle();
        }
    }

    private async Task EvaluateRecipeAvailabilityAsync(AuthoringRecipeAvailabilityInput input, long generation,
        Func<AuthoringRecipeAvailabilityInput, AuthoringRecipeAvailabilityResult> evaluate)
    {
        AuthoringRecipeAvailabilityResult result;
        try { result = await Task.Run(() => evaluate(input)).ConfigureAwait(false); }
        catch (Exception error) { result = new($"Recipe prerequisites could not be checked: {error.Message}", null); }
        try
        {
            _recipeAvailabilityDispatch!(() =>
            {
                _recipeAvailabilityRunning = false;
                if (_recipeAvailabilityStopped || _recipeAvailabilityOwner?.Invoke() != true)
                { _recipeAvailabilityPending = false; _recipeAvailabilityIdle.TrySetResult(); return; }
                if (generation == _recipeAvailabilityGeneration && input == CurrentRecipeInput())
                {
                    _recipeAvailability = result;
                    _publishedRecipeInput = input;
                    PublishRecipeAvailability();
                }
                else RequestRecipeAvailability();
                if (_recipeAvailabilityPending) QueueRecipeAvailability();
                CompleteRecipeAvailabilityIfIdle();
            });
        }
        catch (Exception error)
        {
            // Dispatcher shutdown must release awaiters without publishing from this worker.
            _recipeAvailabilityIdle.TrySetException(error);
        }
    }

    internal static AuthoringRecipeAvailabilityResult EvaluateRecipeAvailability(AuthoringRecipeAvailabilityInput input)
    {
        if (!input.Editable || input.Snapshot is null) return new("Open an editable program before inserting.", null);
        if (input.RecipeId is null) return new("Choose a recipe.", null);
        var draft = input.Snapshot.Restore();
        var rows = AuthoringSequence.Flatten(draft);
        var selected = input.SelectedNode is { } nodeId ? rows.FirstOrDefault(row => row.NodeId == nodeId) : null;
        var target = input.RecipeId == AuthoringRecipeIds.Repeat ? selected
            : IsEmptySequence(draft) || input.InsertionPosition is "End of matching phase" or "End of test steps" or "End of Measure" ? null
            : input.InsertionPosition == "End of section"
                ? input.RecipeId is AuthoringRecipeIds.Identity or AuthoringRecipeIds.Prompt or AuthoringRecipeIds.Input
                    ? null
                    : selected is { Section: SequenceSection.Measure }
                    ? rows.LastOrDefault(row => row.IsSection && row.IndexPath.Count <= selected.IndexPath.Count
                        && selected.IndexPath.Take(row.IndexPath.Count).SequenceEqual(row.IndexPath)) : null
            : selected;
        try
        {
            return new(null, AuthoringSequenceOperations.Insert(draft, input.RecipeId, target,
                input.InsertionPosition == "Before selected", input.InstrumentSlot));
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or NotSupportedException)
        { return new(error.Message, null); }
    }

    private void PublishRecipeAvailability()
    {
        OnPropertyChanged(nameof(CanInsertRecipe));
        OnPropertyChanged(nameof(SelectedRecipePrerequisites));
    }

    private void CompleteRecipeAvailabilityIfIdle()
    {
        if (!_recipeAvailabilityRunning && !_recipeAvailabilityQueued && !_recipeAvailabilityPending)
            _recipeAvailabilityIdle.TrySetResult();
    }

    private static TaskCompletionSource CompletedRecipeAvailability()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
