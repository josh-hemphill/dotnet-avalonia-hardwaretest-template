namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private AuthoringOperationCoordinator? _operations;
    private Action<Action>? _operationDispatch;
    private long _operationGeneration;
    private bool _operationBusy;
    private int _operationLogNotificationPending;
    private string? _operationStage;
    public bool OperationBusy { get => _operationBusy; private set { if (SetField(ref _operationBusy, value)) RaisePackGuardProperties(); RaiseBoardProperties(); } }
    public bool OperationCleanupPending => _operations?.HasPendingCleanup == true;
    public string? OperationStage { get => _operationStage; private set => SetField(ref _operationStage, value); }
    public IReadOnlyList<AuthoringOperationLog> OperationLogs => _operations?.Logs ?? [];
    public string OperationLogText => string.Join("", OperationLogs.Select(log => $"[{log.Stream}] {log.Text}"));

    public void ConfigureOperations(AuthoringChildProcessRunner runner, Action<Action> dispatch)
        => ConfigureOperations(new AuthoringOperationCoordinator(runner), dispatch);

    internal void ConfigureOperations(AuthoringOperationCoordinator coordinator, Action<Action> dispatch)
    {
        _operations?.Dispose();
        _operations = coordinator;
        _operationDispatch = dispatch;
        coordinator.LogsChanged += () =>
        {
            if (Interlocked.Exchange(ref _operationLogNotificationPending, 1) != 0) return;
            var generation = _operationGeneration;
            dispatch(() =>
            {
                try
                {
                    if (!ReferenceEquals(_operations, coordinator) || _operationGeneration != generation) return;
                    RaiseOperationLogs();
                }
                finally { Volatile.Write(ref _operationLogNotificationPending, 0); }
            });
        };
    }
    public void CancelOperation() => _operations?.Cancel();
    public void StopOperations() { _operationGeneration++; _operations?.Dispose(); }
    public Task StopOperationsAsync()
    {
        _operationGeneration++;
        return _operations?.StopAsync() ?? Task.CompletedTask;
    }
    private void ReplaceOperationWorkspace()
    {
        _workspaceSession = Guid.NewGuid();
        ClearCompletedBuilds();
        _lastFindingCheck = null;
        OnPropertyChanged(nameof(IssuesCheckState));
        _operationGeneration++;
        _operations?.ReplaceWorkspace();
        OperationStage = null;
        RaiseOperationLogs();
    }

    public async Task<AuthoringOperationResult> RunOperationAsync(AuthoringOperationKind kind, string? outputDirectory = null, string? offlinePackagePath = null)
    {
        var coordinator = _operations ?? throw new InvalidOperationException("Configure authoring child operations first.");
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace first.");
        if (OperationBusy) throw new InvalidOperationException("An authoring operation is already running.");
        if (kind == AuthoringOperationKind.Pack && !CanPack) throw new AuthoringWorkspaceException(PackGuardText);
        if (kind == AuthoringOperationKind.Validate && HasUnsavedChanges) throw new AuthoringWorkspaceException(ValidationScope);
        var generation = _operationGeneration;
        var operationHome = AuthoringHomeText;
        var checkedBuildIdentity = kind == AuthoringOperationKind.Pack ? CurrentBuildIdentity() : null;
        OperationBusy = true;
        Error = null;
        try
        {
            var checkedState = kind == AuthoringOperationKind.Validate ? PrepareFindingCheck() : null;
            var result = await coordinator.RunAsync(kind, workspace.Root, outputDirectory, Prefs.OpenTapHomeOverride,
                progress: update => _operationDispatch!(() =>
                {
                    if (_operationGeneration == generation) { OperationStage = update.Stage; OnPropertyChanged(nameof(BuildReadinessText)); RaiseOperationLogs(); }
                }), offlinePackagePath: offlinePackagePath).ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                if (_operationGeneration != generation) return;
                if (result.Validation is { } report)
                {
                    AcceptFindings(report, checkedState!);
                    SetFindingValidationStatus(report);
                }
                else if (result.Build is { } build)
                {
                    RetainPackPreflight(new(build.Receipt.RequiredChecks, Home: new OpenTapHome(result.Home!), TuiHome: new OpenTapHome(result.Home!))
                    { IncludedFiles = build.Manifest.Files, ExcludedPlans = build.Receipt.ExcludedPlans });
                    _packCheckedClone = true;
                    OnPropertyChanged(nameof(PackPreflightHomeText));
                    _packPreview = WorkspacePackPlan.WithLastPack(WorkspacePackPlan.Describe(workspace, Prefs.OpenTapHomeOverride), build.Manifest, outputDirectory!);
                    RetainCompletedBuild(build, outputDirectory!, result.Home!, checkedBuildIdentity);
                    RaisePackPreviewProperties();
                    if (AuthoringHomeText == operationHome) Status = $"Packed {build.Manifest.PackageName} {build.Manifest.Version}";
                }
                else { if (AuthoringHomeText == operationHome) Status = $"OpenTAP home {result.Home}"; RefreshPackPreview(); }
            }).ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            await DispatchAsync(() =>
            {
                if (_operationGeneration == generation && AuthoringHomeText == operationHome)
                {
                    Status = error is OperationCanceledException ? "Authoring operation cancelled" : "Authoring operation failed";
                    Error = error is OperationCanceledException ? null : error.Message;
                }
            }).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await DispatchAsync(() => { OperationBusy = false; OperationStage = null; RaiseOperationLogs(); }).ConfigureAwait(false);
        }
    }

    private void RaiseOperationLogs()
    {
        OnPropertyChanged(nameof(OperationLogs));
        OnPropertyChanged(nameof(OperationLogText));
    }

    private Task DispatchAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationDispatch!(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }
}
