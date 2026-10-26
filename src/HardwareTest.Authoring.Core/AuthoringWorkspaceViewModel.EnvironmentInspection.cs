using System.Text.Json;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private AuthoringEnvironmentInspection _environmentInspection = AuthoringEnvironmentInspection.Pending;
    private bool _environmentInspectionPending = true;
    private Guid? _environmentChoicesSession;
    private long _environmentInspectionRevision;
    internal Func<AuthoringWorkspace, string?, AuthoringEnvironmentInspection> EnvironmentInspector { get; set; }
        = AuthoringEnvironmentInspection.Inspect;

    public bool EnvironmentInspectionPending => Workspace is not null && _environmentInspectionPending;

    /// Explicit synchronous compatibility API for headless callers. UI callers use the async API.
    public void RefreshEnvironmentInspection()
    {
        var request = CaptureEnvironmentInspection();
        if (request is null) return;
        PublishEnvironmentInspection(request, InspectEnvironment(request));
    }

    private Action<Action>? _environmentInspectionDispatch;
    private Func<bool>? _environmentInspectionOwner;
    private bool _environmentInspectionStopped;
    private Task _environmentInspectionTask = Task.CompletedTask;
    private TaskCompletionSource? _environmentInspectionCompletion;
    private readonly SemaphoreSlim _environmentInspectionGate = new(1, 1);
    private CancellationTokenSource? _environmentInspectionCancellation;

    /// Desktop hosts configure this before binding controls. Publication is always dispatched
    /// to the owning UI and abandoned after that owner closes.
    public void ConfigureEnvironmentInspection(Action<Action> dispatch, Func<bool> ownerIsCurrent)
    {
        _environmentInspectionDispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _environmentInspectionOwner = ownerIsCurrent ?? throw new ArgumentNullException(nameof(ownerIsCurrent));
        _environmentInspectionStopped = false;
        if (Workspace is not null) RequestEnvironmentInspection();
    }

    public void StopEnvironmentInspection()
    {
        _environmentInspectionStopped = true;
        ++_environmentInspectionRevision;
        _environmentInspectionCancellation?.Cancel();
        _environmentInspectionCompletion?.TrySetResult();
    }

    public Task WaitForEnvironmentInspectionAsync() => _environmentInspectionTask;

    /// Explicit refresh boundary: synchronous for headless APIs, detached for configured desktop hosts.
    public void RequestEnvironmentInspection()
    {
        if (_environmentInspectionStopped) return;
        if (_environmentInspectionDispatch is null) RefreshEnvironmentInspection();
        else _ = RefreshEnvironmentInspectionAsync();
    }

    public Task RefreshEnvironmentInspectionAsync()
    {
        // Publish the idle task before Capture raises notifications: a subscriber may
        // synchronously request a newer revision and must retain that newer task.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _environmentInspectionCompletion = completion;
        _environmentInspectionTask = completion.Task;
        var revision = _environmentInspectionRevision + 1;
        var dispatch = _environmentInspectionDispatch;
        _ = CompleteEnvironmentInspectionAsync(RefreshEnvironmentInspectionCoreAsync(), completion, revision, dispatch);
        return completion.Task;
    }

    private async Task CompleteEnvironmentInspectionAsync(Task operation, TaskCompletionSource completion,
        long revision, Action<Action>? dispatch)
    {
        try { await operation; }
        catch (Exception error)
        {
            void FailClosed()
            {
                if (_environmentInspectionStopped || revision != _environmentInspectionRevision) return;
                _environmentInspection = FailedEnvironmentInspection(error);
                _environmentInspectionPending = false;
                // A broken notification subscriber must not leave an unobserved background
                // exception or undo the fail-closed state already recorded above.
                try { NotifyEnvironmentInspection(); } catch (Exception) { }
            }
            if (dispatch is null) FailClosed();
            else
            {
                try { dispatch(FailClosed); }
                catch (Exception) { /* A closed dispatcher cannot publish; its owner is stopping. */ }
            }
        }
        finally { completion.TrySetResult(); }
    }

    private async Task RefreshEnvironmentInspectionCoreAsync()
    {
        var request = CaptureEnvironmentInspection();
        if (request is null) return;
        var dispatch = _environmentInspectionDispatch;
        var owner = _environmentInspectionOwner;
        var gate = _environmentInspectionGate;
        AuthoringEnvironmentInspection result;
        try
        {
            result = await Task.Run(async () =>
            {
                await gate.WaitAsync(request.Cancellation).ConfigureAwait(false);
                try
                {
                    request.Cancellation.ThrowIfCancellationRequested();
                    var inspected = InspectEnvironment(request);
                    request.Cancellation.ThrowIfCancellationRequested();
                    return inspected;
                }
                finally { gate.Release(); }
            }, request.Cancellation).ConfigureAwait(dispatch is null);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { return; }
        if (dispatch is null) { PublishEnvironmentInspection(request, result); return; }
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = request.Cancellation.Register(() => published.TrySetCanceled(request.Cancellation));
        request.Cancellation.ThrowIfCancellationRequested();
        dispatch(() =>
        {
            try
            {
                if (owner?.Invoke() == true) PublishEnvironmentInspection(request, result);
                published.TrySetResult();
            }
            catch (Exception error) { published.TrySetException(error); }
        });
        try { await published.Task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
    }

    private sealed record EnvironmentInspectionRequest(long Revision, Guid Session,
        AuthoringWorkspace Owner, AuthoringWorkspace Detached, string ManifestJson, string? HomeOverride,
        Func<AuthoringWorkspace, string?, AuthoringEnvironmentInspection> Inspector, CancellationToken Cancellation);

    private EnvironmentInspectionRequest? CaptureEnvironmentInspection()
    {
        var revision = ++_environmentInspectionRevision;
        _environmentInspectionCancellation?.Cancel();
        _environmentInspectionCancellation?.Dispose();
        _environmentInspectionCancellation = new CancellationTokenSource();
        // Keep the editor's selection stable during a rescan, but never carry choices
        // into a different workspace session. Availability and build guards remain pending.
        var choices = Workspace is not null && _environmentChoicesSession == _workspaceSession
            ? _environmentInspection.Choices : Array.Empty<AuthoringInstrumentAdapter>();
        _environmentInspection = AuthoringEnvironmentInspection.Pending with { Choices = choices };
        _environmentInspectionPending = Workspace is not null;
        if (Workspace is not { } owner)
        {
            NotifyEnvironmentInspection();
            return null;
        }
        var json = JsonSerializer.Serialize(owner.Manifest, AuthoringJsonContext.Default.AuthoringManifest);
        var detached = owner with
        {
            Manifest = JsonSerializer.Deserialize(json, AuthoringJsonContext.Default.AuthoringManifest)!,
            TapPlanPaths = Array.AsReadOnly(owner.TapPlanPaths.ToArray()),
        };
        var request = new EnvironmentInspectionRequest(revision, _workspaceSession, owner, detached, json,
            Prefs.OpenTapHomeOverride, EnvironmentInspector, _environmentInspectionCancellation.Token);
        NotifyEnvironmentInspection();
        return request;
    }

    private static AuthoringEnvironmentInspection InspectEnvironment(EnvironmentInspectionRequest request)
    {
        try { return request.Inspector(request.Detached, request.HomeOverride); }
        catch (Exception error) when (!request.Cancellation.IsCancellationRequested)
        {
            return FailedEnvironmentInspection(error);
        }
    }

    private static AuthoringEnvironmentInspection FailedEnvironmentInspection(Exception error)
        => AuthoringEnvironmentInspection.Empty(error.Message) with
        {
            Error = error.Message,
            Blockers = Array.AsReadOnly(new[] { new PackPreflightFinding("PACK_ENVIRONMENT_INSPECTION", error.Message, true) }),
        };

    private void PublishEnvironmentInspection(EnvironmentInspectionRequest request, AuthoringEnvironmentInspection result)
    {
        if (_environmentInspectionStopped || request.Revision != _environmentInspectionRevision || request.Session != _workspaceSession
            || !ReferenceEquals(Workspace, request.Owner) || Prefs.OpenTapHomeOverride != request.HomeOverride
            || JsonSerializer.Serialize(Workspace!.Manifest, AuthoringJsonContext.Default.AuthoringManifest) != request.ManifestJson)
            return;
        _environmentInspection = result;
        _environmentChoicesSession = request.Session;
        _environmentInspectionPending = false;
        RefreshDeferredSavePreviewWarning();
        OnPropertyChanged(nameof(EditingIssues));
        OnPropertyChanged(nameof(IssuesSummary));
        NotifyEnvironmentInspection();
    }

    private void NotifyEnvironmentInspection()
    {
        RequestPreviewRefresh();
        OnPropertyChanged(nameof(EnvironmentInspectionPending));
        RaiseEnvironmentProperties();
        RaiseHardwareProperties();
    }
}
