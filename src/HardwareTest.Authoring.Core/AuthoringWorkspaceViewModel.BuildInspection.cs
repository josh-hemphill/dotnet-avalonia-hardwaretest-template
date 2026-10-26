using System.Text.Json;

namespace HardwareTest.Authoring;

internal sealed record AuthoringBuildInspectionInput(Guid Session, long Generation, AuthoringWorkspace? Owner,
    AuthoringWorkspace? Workspace, string? Home, IReadOnlyList<(string Id, long Revision, AuthoringDocumentSnapshot Snapshot)> Documents,
    IReadOnlyList<DirtyProgramSummary> Dirty, bool CatalogDirty, IReadOnlyList<string> Uncompiled, IReadOnlyList<string> Conflicts);
internal sealed record AuthoringBuildInspectionResult(WorkspacePackPreview Preview, IReadOnlyList<AuthoringBuildProgramLine> Programs,
    bool Blockers, bool Writable, string? Identity);

public sealed partial class AuthoringWorkspaceViewModel
{
    private Action<Action>? _buildInspectionDispatch;
    private Func<bool>? _buildInspectionOwner;
    private Func<AuthoringBuildInspectionInput, AuthoringBuildInspectionResult> _inspectBuild = InspectBuild;
    private long _buildInspectionGeneration;
    private bool _buildInspectionRunning, _buildInspectionQueued, _buildInspectionPending, _buildInspectionStopped;
    private bool _buildInspectionBlockers = true, _buildInspectionWritable, _buildInspectionBusy;
    private IReadOnlyList<AuthoringBuildProgramLine> _buildProgramCache = [];
    private string? _buildInspectionIdentity;
    private TaskCompletionSource _buildInspectionIdle = CompletedBuildInspection();
    public bool BuildInspectionBusy { get => _buildInspectionBusy; private set => SetField(ref _buildInspectionBusy, value); }
    public Task WaitForBuildInspectionAsync(CancellationToken cancellationToken = default) => _buildInspectionIdle.Task.WaitAsync(cancellationToken);
    public void ConfigureBuildInspection(Action<Action> dispatch, Func<bool> ownerIsCurrent)
        => ConfigureBuildInspection(dispatch, ownerIsCurrent, InspectBuild);
    internal void ConfigureBuildInspection(Action<Action> dispatch, Func<bool> ownerIsCurrent,
        Func<AuthoringBuildInspectionInput, AuthoringBuildInspectionResult> inspect)
    {
        if (_buildInspectionIdle.Task.IsFaulted) _buildInspectionRunning = false;
        _buildInspectionDispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _buildInspectionOwner = ownerIsCurrent ?? throw new ArgumentNullException(nameof(ownerIsCurrent));
        _inspectBuild = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _buildInspectionStopped = false;
        RequestBuildInspection();
    }
    public void StopBuildInspection()
    {
        _buildInspectionStopped = true;
        _buildInspectionGeneration++;
        _buildInspectionPending = false;
        BuildInspectionBusy = false;
        _buildInspectionIdle.TrySetResult();
    }
    public void RequestBuildInspection()
    {
        if (_buildInspectionDispatch is null || _buildInspectionStopped) return;
        _buildInspectionGeneration++;
        _buildInspectionPending = true;
        if (_buildInspectionIdle.Task.IsCompleted) _buildInspectionIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BuildInspectionBusy = true;
        if (_buildInspectionRunning || _buildInspectionQueued) return;
        _buildInspectionQueued = true;
        _buildInspectionDispatch(StartBuildInspection);
    }
    private void StartBuildInspection()
    {
        _buildInspectionQueued = false;
        if (_buildInspectionRunning) return;
        if (_buildInspectionStopped || _buildInspectionOwner?.Invoke() != true)
        {
            _buildInspectionPending = false;
            BuildInspectionBusy = false;
            _buildInspectionIdle.TrySetResult();
            return;
        }
        _buildInspectionPending = false;
        _buildInspectionRunning = true;
        var workspace = Workspace;
        // The manifest is small configuration; program content remains owned immutable snapshots.
        var detached = workspace is null ? null : workspace with
        {
            Manifest = JsonSerializer.Deserialize(JsonSerializer.Serialize(workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest),
                AuthoringJsonContext.Default.AuthoringManifest)!,
            TapPlanPaths = workspace.TapPlanPaths.ToArray()
        };
        var input = new AuthoringBuildInspectionInput(_workspaceSession, _buildInspectionGeneration, workspace, detached,
            Prefs.OpenTapHomeOverride, _documents.Select(pair => (pair.Key, pair.Value.Revision, pair.Value.Snapshot)).ToArray(),
            _dirtyPlans.Union(_dirtySidecars).Select(id => new DirtyProgramSummary(id, _dirtyPlans.Contains(id), _dirtySidecars.Contains(id))).ToArray(),
            WorkspaceCatalogDirty, _uncompiledDocuments.ToArray(), _compiledConflicts.ToArray());
        _ = InspectBuildAsync(input, _buildInspectionDispatch!, _inspectBuild);
    }
    private bool BuildInspectionCurrent(AuthoringBuildInspectionInput input)
        => input.Session == _workspaceSession && input.Generation == _buildInspectionGeneration && ReferenceEquals(input.Owner, Workspace)
        && input.Home == Prefs.OpenTapHomeOverride && input.Documents.Count == _documents.Count
        && input.Documents.All(document => _documents.TryGetValue(document.Id, out var current)
            && current.Revision == document.Revision && ReferenceEquals(current.Snapshot, document.Snapshot));
    private async Task InspectBuildAsync(AuthoringBuildInspectionInput input, Action<Action> dispatch,
        Func<AuthoringBuildInspectionInput, AuthoringBuildInspectionResult> inspect)
    {
        var completion = _buildInspectionIdle;
        AuthoringBuildInspectionResult? result = null;
        string? failure = null;
        try { result = await Task.Run(() => inspect(input)).ConfigureAwait(false); }
        catch (Exception error) { failure = error.Message; }
        try
        {
            dispatch(() =>
        {
            try
            {
                _buildInspectionRunning = false;
                if (!_buildInspectionStopped && _buildInspectionOwner?.Invoke() == true && BuildInspectionCurrent(input))
                {
                    _buildInspectionBlockers = result?.Blockers ?? true;
                    _buildInspectionWritable = result?.Writable ?? false;
                    _buildInspectionIdentity = result?.Identity;
                    _buildProgramCache = result?.Programs ?? [];
                    if (result is not null)
                    {
                        _packPreview = result.Preview;
                        if (LastCompletedBuild is { } completed)
                            _packPreview = WorkspacePackPlan.WithLastPack(_packPreview, completed.Result.Manifest, completed.OutputDirectory);
                        ClearResolvedHomePreviewWarning();
                    }
                    if (failure is not null) ReportError($"Build readiness could not refresh: {failure}");
                    BuildInspectionBusy = false;
                    RaisePackPreviewProperties();
                    RaisePackGuardProperties();
                }
                else if (!_buildInspectionStopped && _buildInspectionOwner?.Invoke() == true) _buildInspectionPending = true;
                if (_buildInspectionPending && !_buildInspectionStopped)
                {
                    if (!_buildInspectionQueued)
                    {
                        _buildInspectionQueued = true;
                        dispatch(StartBuildInspection);
                    }
                }
                else { BuildInspectionBusy = false; _buildInspectionIdle.TrySetResult(); }
            }
            catch (Exception error)
            {
                // Subscriber failures must not strand a pending open/close wait.
                _buildInspectionRunning = false;
                _buildInspectionBusy = false;
                _buildInspectionPending = false;
                completion.TrySetException(error);
            }
        });
        }
        catch (Exception error)
        {
            // A rejected dispatch cannot publish UI state. Surface it through the owned wait.
            completion.TrySetException(error);
        }
    }
    private static AuthoringBuildInspectionResult InspectBuild(AuthoringBuildInspectionInput input)
    {
        if (input.Workspace is not { } workspace) return new(WorkspacePackPlan.Empty, [], true, false, null);
        var preview = WorkspacePackPlan.Describe(workspace, input.Home);
        var store = new AuthoringDocumentStore(workspace.Root);
        var sources = new Dictionary<string, AuthoringDocumentDto>(StringComparer.OrdinalIgnoreCase);
        var uncompiled = input.Uncompiled.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in store.ListDocumentIds())
        {
            if (!AuthoringBuildInclusion.Includes(workspace.Manifest, id)) continue;
            var loaded = store.Load(id);
            if (loaded.Document is { } document)
            {
                sources[id] = document;
                if (document.RequiresCompilation || document.CompiledPlanHash is null) uncompiled.Add(id);
            }
            else uncompiled.Add(id);
        }
        var programs = AuthoringBuildInclusion.ProgramIds(workspace).Select(id => new AuthoringBuildProgramLine(id,
            AuthoringBuildInclusion.Includes(workspace.Manifest, id), sources.GetValueOrDefault(id)?.Revision,
            !AuthoringBuildInclusion.Includes(workspace.Manifest, id) ? "Excluded from build" : uncompiled.Contains(id) ? sources.ContainsKey(id) ? "Saved source requires compilation" : "Saved source unavailable or unsupported"
                : input.Conflicts.Contains(id) ? "Source conflict" : "Saved compiled input",
            !workspace.IsReadOnly && AuthoringBuildInclusion.AllowsProgram(workspace.Manifest, id))).ToArray();
        var identity = AuthoringContentFingerprint.Compute(workspace, input.Documents.Select(document => document.Snapshot.Restore()).ToArray(),
            input.Dirty, input.CatalogDirty) + "|" + (string.IsNullOrWhiteSpace(preview.AuthoringHomePath)
                ? "OpenTAP home not bootstrapped yet (.authoring/opentap)" : preview.AuthoringHomePath);
        return new(preview, programs, EvaluateSavedBuildBlockers(workspace, sources, input.Conflicts, uncompiled),
            WorkspacePacker.IsWritableWorkspace(workspace), identity);
    }
    private static TaskCompletionSource CompletedBuildInspection()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
