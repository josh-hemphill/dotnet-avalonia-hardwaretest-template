using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.Core.Hardware;

namespace HardwareTest.Core.Engine;

public interface IRunControl : INotifyPropertyChanged
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    bool IsSafetyStopping { get; }
    bool WasSafetyStopRequested { get; }

    /// Registers the active run CTS so Pause/Cancel/SafetyStop can control it.
    void AttachRun(CancellationTokenSource runCts);

    /// Clears run state after execution finishes (including safety shutdown).
    void DetachRun();

    void Pause();
    void Resume();
    void RequestCancel();

    /// Critical path: resume pause, preempt VISA waiters, cancel run, mark safety stop.
    void RequestSafetyStop();

    /// Second Safety click cancels in-progress safe-shutdown I/O.
    void CancelSafetyShutdown();

    CancellationToken SafetyShutdownToken { get; }

    Task WaitIfPausedAsync(CancellationToken cancellationToken = default);
}

/// Coordinates pause, soft cancel, and prioritized safety stop across engines and UI.
public sealed class RunControl : IRunControl
{
    private readonly VisaSessionGate _gate;
    private readonly ManualResetEventSlim _pauseEvent = new(initialState: true);
    private readonly object _sync = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _safetyCts;

    public RunControl(VisaSessionGate gate)
    {
        _gate = gate;
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsSafetyStopping { get; private set; }
    public bool WasSafetyStopRequested { get; private set; }

    public CancellationToken SafetyShutdownToken
    {
        get
        {
            lock (_sync)
            {
                return _safetyCts?.Token ?? CancellationToken.None;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachRun(CancellationTokenSource runCts)
    {
        lock (_sync)
        {
            _runCts = runCts;
            _safetyCts?.Dispose();
            _safetyCts = new CancellationTokenSource();
            WasSafetyStopRequested = false;
            IsSafetyStopping = false;
            IsPaused = false;
            IsRunning = true;
            _pauseEvent.Set();
        }

        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(IsSafetyStopping));
        Raise(nameof(WasSafetyStopRequested));
    }

    public void DetachRun()
    {
        lock (_sync)
        {
            _runCts = null;
            IsRunning = false;
            IsPaused = false;
            IsSafetyStopping = false;
            _pauseEvent.Set();
        }

        Raise(nameof(IsRunning));
        Raise(nameof(IsPaused));
        Raise(nameof(IsSafetyStopping));
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (!IsRunning || WasSafetyStopRequested)
            {
                return;
            }

            IsPaused = true;
            _pauseEvent.Reset();
        }

        Raise(nameof(IsPaused));
    }

    public void Resume()
    {
        lock (_sync)
        {
            IsPaused = false;
            _pauseEvent.Set();
        }

        Raise(nameof(IsPaused));
    }

    public void RequestCancel()
    {
        Resume();
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _runCts;
        }

        cts?.Cancel();
    }

    public void RequestSafetyStop()
    {
        lock (_sync)
        {
            WasSafetyStopRequested = true;
            IsSafetyStopping = true;
            IsPaused = false;
            _pauseEvent.Set();
        }

        Raise(nameof(WasSafetyStopRequested));
        Raise(nameof(IsSafetyStopping));
        Raise(nameof(IsPaused));

        _gate.PreemptWaiters();

        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _runCts;
        }

        cts?.Cancel();
    }

    public void CancelSafetyShutdown()
    {
        CancellationTokenSource? safety;
        lock (_sync)
        {
            safety = _safetyCts;
        }

        safety?.Cancel();
        _gate.PreemptWaiters();
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        while (!_pauseEvent.IsSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => _pauseEvent.Wait(millisecondsTimeout: 50), cancellationToken).ConfigureAwait(false);
        }
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
