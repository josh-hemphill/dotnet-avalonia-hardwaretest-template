using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host;

/// CTS, pause gate, and operator-interaction gate for one execute (no OpenTAP types).
/// <see cref="OpenTapRunContext"/> and <c>FakeOpenTapSession</c> compose this instead of forking gates.
public sealed class OpenTapRunControlState : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private bool _paused;
    private readonly ManualResetEventSlim _pauseGate = new(true);
    private readonly ManualResetEventSlim _interactionGate = new(false);
    private bool _disposed;

    public bool IsPaused => _paused;

    public bool IsAwaitingOperator { get; set; }

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

    /// <param name="startPaused">
    /// When true, WaitIfPaused at execute-start blocks (session Pause before Run).
    /// </param>
    public void BeginRun(CancellationToken externalToken, bool startPaused)
    {
        lock (_sync)
        {
            DisposeCts_NoLock();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            if (startPaused)
            {
                _paused = true;
                _pauseGate.Reset();
            }
            else
            {
                _paused = false;
                _pauseGate.Set();
            }

            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            LastInteractionResponse = null;
            _interactionGate.Reset();
            _cts.Token.Register(OnRunCancelled);
        }

        RaiseOperatorState();
    }

    public void Pause()
    {
        _paused = true;
        _pauseGate.Reset();
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        lock (_sync)
        {
            if (PendingInteraction is not null)
            {
                LastInteractionResponse = response
                    ?? OperatorInteractionResponse.Continue(PendingInteraction.Id);
            }

            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _interactionGate.Set();
        }

        RaiseOperatorState();
        _paused = false;
        _pauseGate.Set();
    }

    public void Abort()
    {
        lock (_sync)
        {
            if (PendingInteraction is not null)
            {
                LastInteractionResponse = OperatorInteractionResponse.Cancel(PendingInteraction.Id);
            }

            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _interactionGate.Set();
        }

        RaiseOperatorState();
        _paused = false;
        _pauseGate.Set();
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    public void WaitIfPaused()
    {
        while (true)
        {
            Token.ThrowIfCancellationRequested();
            if (!_paused)
            {
                return;
            }

            _pauseGate.Wait(50);
        }
    }

    public bool WaitPauseGate(int millisecondsTimeout) => _pauseGate.Wait(millisecondsTimeout);

    public bool WaitInteractionGate(int millisecondsTimeout) => _interactionGate.Wait(millisecondsTimeout);

    public void ResetInteractionGate() => _interactionGate.Reset();

    public void OpenInteractionGate() => _interactionGate.Set();

    public void BeginPendingInteraction(OperatorInteractionRequest request)
    {
        lock (_sync)
        {
            PendingInteraction = request;
            LastInteractionResponse = null;
            _interactionGate.Reset();
            IsAwaitingOperator = true;
            OperatorPromptMessage = request.Message;
        }

        Pause();
        RaiseOperatorState();
    }

    public OperatorInteractionResponse WaitForInteractionResponse(string requestId)
    {
        while (!_interactionGate.Wait(50))
        {
            Token.ThrowIfCancellationRequested();
        }

        OperatorInteractionResponse response;
        lock (_sync)
        {
            response = LastInteractionResponse
                       ?? OperatorInteractionResponse.Cancel(requestId);
            PendingInteraction = null;
            LastInteractionResponse = null;
            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
        }

        RaiseOperatorState();
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
            IsAwaitingOperator = false;
            OperatorPromptMessage = null;
            PendingInteraction = null;
            _interactionGate.Set();
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
            _paused = false;
            _pauseGate.Set();
            _interactionGate.Set();
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

    private void RaiseOperatorState() => OperatorStateChanged?.Invoke();
}
