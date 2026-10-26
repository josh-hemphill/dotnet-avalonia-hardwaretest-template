namespace HardwareTest.Authoring;

internal sealed record AuthoringEditingIssueInput(Guid Session, long Generation, AuthoringWorkspace? Owner,
    AuthoringWorkspace? DetachedWorkspace, string? HomeOverride,
    IReadOnlyList<(string Package, string Version)> Dependencies,
    IReadOnlyList<(string PlanId, long Revision, AuthoringDocumentSnapshot Snapshot)> Documents);

public sealed partial class AuthoringWorkspaceViewModel
{
    private Action<Action>? _editingIssueDispatch;
    private Func<bool>? _editingIssueOwner;
    private Func<AuthoringEditingIssueInput, IReadOnlyList<AuthoringEditingIssue>> _editingIssueEvaluate = EvaluateEditingIssues;
    private IReadOnlyList<AuthoringEditingIssue> _editingIssueCache = [];
    private long _editingIssueGeneration;
    private bool _editingIssueQueued;
    private bool _editingIssueRunning;
    private bool _editingIssuePending;
    private bool _editingIssueStopped;
    private bool _editingIssuePublishing;
    private bool _editingIssuesBusy;
    private TaskCompletionSource _editingIssueIdle = CompletedEditingIssues();

    public bool EditingIssuesBusy { get => _editingIssuesBusy; private set => SetField(ref _editingIssuesBusy, value); }
    public Task WaitForEditingIssuesAsync(CancellationToken cancellationToken = default)
        => _editingIssueIdle.Task.WaitAsync(cancellationToken);

    /// Enable before binding desktop controls. Headless callers keep synchronous issue evaluation.
    public void ConfigureEditingIssueProcessing(Action<Action> dispatch, Func<bool> ownerIsCurrent)
        => ConfigureEditingIssueProcessing(dispatch, ownerIsCurrent, EvaluateEditingIssues);

    internal void ConfigureEditingIssueProcessing(Action<Action> dispatch, Func<bool> ownerIsCurrent,
        Func<AuthoringEditingIssueInput, IReadOnlyList<AuthoringEditingIssue>> evaluate)
    {
        _editingIssueDispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _editingIssueOwner = ownerIsCurrent ?? throw new ArgumentNullException(nameof(ownerIsCurrent));
        _editingIssueEvaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _editingIssueStopped = false;
        RequestEditingIssueRefresh();
    }

    public void StopEditingIssueProcessing()
    {
        _editingIssueStopped = true;
        _editingIssueGeneration++;
        _editingIssuePending = false;
        _editingIssueCache = [];
        EditingIssuesBusy = false;
        _editingIssueIdle.TrySetResult();
    }

    private void RequestEditingIssueRefresh()
    {
        if (_editingIssueDispatch is null || _editingIssueStopped || _editingIssuePublishing) return;
        _editingIssueGeneration++;
        _editingIssuePending = true;
        _editingIssueCache = [];
        if (_editingIssueIdle.Task.IsCompleted) _editingIssueIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EditingIssuesBusy = true;
        if (_editingIssueQueued || _editingIssueRunning) return;
        _editingIssueQueued = true;
        _editingIssueDispatch(StartEditingIssueEvaluation);
    }

    private void StartEditingIssueEvaluation()
    {
        _editingIssueQueued = false;
        if (_editingIssueStopped || _editingIssueOwner?.Invoke() != true)
        {
            _editingIssuePending = false;
            EditingIssuesBusy = false;
            _editingIssueIdle.TrySetResult();
            return;
        }
        _editingIssuePending = false;
        _editingIssueRunning = true;
        // Snapshots are immutable owned values. Clone and filesystem inspection belong
        // to the worker; capture only session references and small configuration values here.
        var workspace = Workspace;
        var input = new AuthoringEditingIssueInput(_workspaceSession, _editingIssueGeneration, workspace,
            workspace is null ? null : new(workspace.Root, new AuthoringManifest(), []), Prefs.OpenTapHomeOverride,
            workspace?.Manifest.Dependencies.Select(dependency => (dependency.Package, dependency.Version)).ToArray() ?? [],
            _documents.Select(pair => (pair.Key, pair.Value.Revision, pair.Value.Snapshot)).ToArray());
        _ = EvaluateEditingIssuesAsync(input, _editingIssueDispatch!, _editingIssueEvaluate);
    }

    private bool EditingIssueInputCurrent(AuthoringEditingIssueInput input)
        => input.Session == _workspaceSession && input.Generation == _editingIssueGeneration
            && ReferenceEquals(input.Owner, Workspace) && input.HomeOverride == Prefs.OpenTapHomeOverride
            && input.Dependencies.SequenceEqual(Workspace?.Manifest.Dependencies.Select(dependency => (dependency.Package, dependency.Version)) ?? [])
            && input.Documents.Count == _documents.Count
            && input.Documents.All(document => _documents.TryGetValue(document.PlanId, out var current)
                && document.Revision == current.Revision && ReferenceEquals(document.Snapshot, current.Snapshot));

    private async Task EvaluateEditingIssuesAsync(AuthoringEditingIssueInput input, Action<Action> dispatch,
        Func<AuthoringEditingIssueInput, IReadOnlyList<AuthoringEditingIssue>> evaluate)
    {
        IReadOnlyList<AuthoringEditingIssue> issues;
        try { issues = await Task.Run(() => Array.AsReadOnly(evaluate(input).ToArray())).ConfigureAwait(false); }
        catch (Exception error)
        {
            issues = [new("EDITING_ISSUES_UNAVAILABLE", $"Draft checks could not finish: {error.Message}", input.Documents.FirstOrDefault().PlanId ?? string.Empty, Guid.Empty)];
        }
        dispatch(() =>
        {
            _editingIssueRunning = false;
            if (_editingIssueStopped || _editingIssueOwner?.Invoke() != true)
            {
                EditingIssuesBusy = false;
                _editingIssueIdle.TrySetResult();
                return;
            }
            var current = EditingIssueInputCurrent(input);
            if (current)
            {
                _editingIssueCache = issues;
                _editingIssuePublishing = true;
                try
                {
                    OnPropertyChanged(nameof(EditingIssues));
                    OnPropertyChanged(nameof(IssuesSummary));
                }
                finally { _editingIssuePublishing = false; }
                // A subscriber can edit the document while receiving publication.
                // Recheck references after notifications before marking the worker idle.
                current = EditingIssueInputCurrent(input);
            }
            if (_editingIssuePending || !current) RequestEditingIssueRefresh();
            else { EditingIssuesBusy = false; _editingIssueIdle.TrySetResult(); }
        });
    }

    internal static IReadOnlyList<AuthoringEditingIssue> EvaluateEditingIssues(AuthoringEditingIssueInput input)
    {
        OpenTapHome? home = null;
        string? error = null;
        if (input.DetachedWorkspace is { } workspace
            && WorkspacePackPlan.TryResolveHomePath(workspace, input.HomeOverride, out var path, out error)) home = new(path!);
        var packages = input.Dependencies.Select(dependency => dependency.Package).ToArray();
        return input.Documents.SelectMany(document => AuthoringIssueService.GetIssues(document.Snapshot.Restore(), home, error, packages)).ToArray();
    }

    private static TaskCompletionSource CompletedEditingIssues()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
