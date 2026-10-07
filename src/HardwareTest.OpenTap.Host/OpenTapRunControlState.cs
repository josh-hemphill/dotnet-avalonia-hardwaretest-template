using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host;

/// CTS, pause gate, and request-owned operator completions for one execute (no OpenTAP types).
/// <see cref="OpenTapRunContext"/> and <c>FakeOpenTapSession</c> share these run controls.
public sealed class OpenTapRunControlState : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TaskCompletionSource<OperatorInteractionResponse>> _interactions = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;
    private bool _paused;
    private bool _awaitingOperator;
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private bool _disposed;

    public bool IsPaused { get { lock (_sync) return _paused; } }

    public bool IsAwaitingOperator
    {
        get { lock (_sync) return _awaitingOperator; }
        set { lock (_sync) _awaitingOperator = value; }
    }

    public string? OperatorPromptMessage { get; set; }

    public OperatorInteractionRequest? PendingInteraction { get; set; }

    public OperatorInteractionResponse? LastInteractionResponse { get; set; }

    public CancellationToken Token
    {
        get
        {
            lock (_sync)
            {
                return _cts?.Token ?? CancellationToken.None;
            }
        }
    }

    public bool IsCancellationRequested
    {
        get
        {
            lock (_sync)
            {
                return _cts?.IsCancellationRequested == true;
            }
        }
    }

    public event Action? OperatorStateChanged;

    /// Starts the run CTS and clears the current interaction state. Does not write the pause gate —
    /// Pause/Resume already mutated it, and a snapshot-apply here would overwrite a
    /// Pause or Resume that landed after the session assigned this context (worker IPC
    /// loop vs background Run). Preserve live pause so WaitIfPaused at execute-start
    /// can still block short plans.
    public void BeginRun(CancellationToken externalToken)
    {
        lock (_sync)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            LastInteractionResponse = null;
            _cts.Token.Register(OnRunCancelled);
        }

        RaiseOperatorState();
    }

    public void Pause()
    {
        lock (_sync)
        {
            _paused = true;
            _pauseGate.Reset();
        }
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        lock (_sync)
        {
            // A delayed or duplicate response must not complete a newer request.
            if (response is not null && response.RequestId != PendingInteraction?.Id) return;
            if (PendingInteraction is not null)
            {
                LastInteractionResponse = response
                    ?? OperatorInteractionResponse.Continue(PendingInteraction.Id);
                CompleteInteraction_NoLock(LastInteractionResponse);
            }

            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _paused = false;
            _pauseGate.Set();
        }

        RaiseOperatorState();
    }

    public void Abort()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (PendingInteraction is not null)
            {
                LastInteractionResponse = OperatorInteractionResponse.Cancel(PendingInteraction.Id);
                CompleteInteraction_NoLock(LastInteractionResponse);
            }

            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _paused = false;
            _pauseGate.Set();
            cancellation = _cts;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch
        {
            // ignore
        }
        RaiseOperatorState();
    }

    public void WaitIfPaused()
    {
        while (true)
        {
            Token.ThrowIfCancellationRequested();
            if (!IsPaused)
            {
                return;
            }

            _pauseGate.Wait(50);
        }
    }

    public bool WaitPauseGate(int millisecondsTimeout) => _pauseGate.Wait(millisecondsTimeout);

    public void BeginPendingInteraction(OperatorInteractionRequest request)
    {
        lock (_sync)
        {
            if (!_interactions.TryAdd(request.Id, new(TaskCreationOptions.RunContinuationsAsynchronously)))
                throw new InvalidOperationException("An operator interaction with this ID is already outstanding.");
            PendingInteraction = request;
            LastInteractionResponse = null;
            _paused = true;
            _pauseGate.Reset();
            IsAwaitingOperator = true;
            OperatorPromptMessage = request.Message;
            if (_cts?.IsCancellationRequested == true)
                OnRunCancelled();
        }

        RaiseOperatorState();
    }

    public OperatorInteractionResponse WaitForInteractionResponse(string requestId)
    {
        TaskCompletionSource<OperatorInteractionResponse> completion;
        lock (_sync)
        {
            if (!_interactions.TryGetValue(requestId, out completion!))
                throw new InvalidOperationException("The operator interaction is not outstanding.");
        }

        // A notification may already have published another request while this
        // request's waiter is returning. Consume only this request's completion.
        var response = completion.Task.GetAwaiter().GetResult();
        var stateChanged = false;
        lock (_sync)
        {
            _interactions.Remove(requestId);
            stateChanged = response.Cancelled && PendingInteraction is null;
            if (PendingInteraction?.Id == requestId)
            {
                PendingInteraction = null;
                IsAwaitingOperator = false;
                OperatorPromptMessage = null;
                stateChanged = true;
            }
        }

        if (stateChanged) RaiseOperatorState();
        return response;
    }

    public OperatorInteractionResponse HandleInteraction(OperatorInteractionRequest request)
    {
        BeginPendingInteraction(request);
        return WaitForInteractionResponse(request.Id);
    }

    public void EndRun()
    {
        lock (_sync)
        {
            DisposeCts_NoLock();
        }
    }

    public void CompleteRun()
    {
        lock (_sync)
        {
            CancelInteractions_NoLock();
            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _paused = false;
            _pauseGate.Set();
            DisposeCts_NoLock();
        }

        RaiseOperatorState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompleteRun();
    }

    private void OnRunCancelled()
    {
        try
        {
            lock (_sync)
            {
                CancelInteractions_NoLock();
                IsAwaitingOperator = false;
                OperatorPromptMessage = null;
                PendingInteraction = null;
                _paused = false;
                _pauseGate.Set();
            }
        }
        catch
        {
            // ignored
        }
    }

    private void DisposeCts_NoLock()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private void CompleteInteraction_NoLock(OperatorInteractionResponse response)
    {
        if (_interactions.TryGetValue(response.RequestId, out var completion))
            completion.TrySetResult(response);
    }

    private void CancelInteractions_NoLock()
    {
        foreach (var interaction in _interactions)
            interaction.Value.TrySetResult(OperatorInteractionResponse.Cancel(interaction.Key));
    }

    private void RaiseOperatorState() => OperatorStateChanged?.Invoke();
}
