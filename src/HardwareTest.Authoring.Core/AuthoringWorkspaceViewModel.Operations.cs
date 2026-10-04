namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private AuthoringOperationCoordinator? _operations;
    private Action<Action>? _operationDispatch;
    private long _operationGeneration;
    private bool _operationBusy;
    private int _operationLogNotificationPending;
    private string? _operationStage;
    public bool OperationBusy { get => _operationBusy; private set { if (SetField(ref _operationBusy, value)) RaisePackGuardProperties(); } }
    public string? OperationStage { get => _operationStage; private set => SetField(ref _operationStage, value); }
    public IReadOnlyList<AuthoringOperationLog> OperationLogs => _operations?.Logs ?? [];
    public string OperationLogText => string.Join("", OperationLogs.Select(log => $"[{log.Stream}] {log.Text}"));

    public void ConfigureOperations(AuthoringChildProcessRunner runner, Action<Action> dispatch)
    {
        _operations?.Dispose();
        var coordinator = new AuthoringOperationCoordinator(runner);
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
        _operationGeneration++;
        _operations?.ReplaceWorkspace();
        OperationStage = null;
        RaiseOperationLogs();
    }

    public async Task<AuthoringOperationResult> RunOperationAsync(AuthoringOperationKind kind, string? outputDirectory = null)
    {
        var coordinator = _operations ?? throw new InvalidOperationException("Configure authoring child operations first.");
        var workspace = Workspace ?? throw new AuthoringWorkspaceException("Open a workspace first.");
        if (OperationBusy) throw new InvalidOperationException("An authoring operation is already running.");
        if (kind == AuthoringOperationKind.Pack && !CanPack) throw new AuthoringWorkspaceException(PackGuardText);
        if (kind == AuthoringOperationKind.Validate)
        {
            if (HasUnsavedChanges) throw new AuthoringWorkspaceException(ValidationScope);
            RefreshSourceReadiness();
            if (HasUncompiledSources || _compiledConflicts.Count > 0)
                throw new AuthoringWorkspaceException("Compile saved drafts and reconcile external edits before validating compiled plans.");
        }
        var generation = _operationGeneration;
        OperationBusy = true;
        Error = null;
        try
        {
            var result = await coordinator.RunAsync(kind, workspace.Root, outputDirectory, Prefs.OpenTapHomeOverride,
                progress: update => _operationDispatch!(() =>
                {
                    if (_operationGeneration == generation) { OperationStage = update.Stage; RaiseOperationLogs(); }
                })).ConfigureAwait(false);
            await DispatchAsync(() =>
            {
                if (_operationGeneration != generation) return;
                if (result.Validation is { } report)
                {
                    Findings = report.Plans.SelectMany(p => p.Findings).ToArray();
                    FindingRows = report.Plans.SelectMany(plan => plan.Findings.Select(finding => new AuthoringFindingRow(
                        Path.GetFileNameWithoutExtension(plan.TargetPath), plan.TargetPath, finding,
                        Programs.Any(program => string.Equals(program.PlanId, Path.GetFileNameWithoutExtension(plan.TargetPath), StringComparison.OrdinalIgnoreCase))))).ToArray();
                    Status = report.HasErrors ? $"{report.ErrorCount} contract error(s)" : $"{report.WarningCount} contract warning(s)";
                    Error = report.HasErrors ? Status : null;
                }
                else if (result.Build is { } build)
                {
                    RetainPackPreflight(new(build.Receipt.RequiredChecks, Home: new OpenTapHome(result.Home!), TuiHome: new OpenTapHome(result.Home!))
                    { IncludedFiles = build.Manifest.Files, ExcludedPlans = build.Receipt.ExcludedPlans });
                    _packCheckedClone = true;
                    OnPropertyChanged(nameof(PackPreflightHomeText));
                    _packPreview = WorkspacePackPlan.WithLastPack(WorkspacePackPlan.Describe(workspace, result.Home), build.Manifest, outputDirectory!);
                    RaisePackPreviewProperties();
                    Status = $"Packed {build.Manifest.PackageName} {build.Manifest.Version}";
                }
                else { Status = $"OpenTAP home {result.Home}"; RefreshPackPreview(); }
            }).ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            await DispatchAsync(() =>
            {
                if (_operationGeneration == generation)
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
