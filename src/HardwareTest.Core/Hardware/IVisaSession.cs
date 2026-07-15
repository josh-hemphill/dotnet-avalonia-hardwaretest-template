namespace HardwareTest.Core.Hardware;

public readonly record struct MeasurementSample(string Channel, DateTimeOffset Timestamp, double Value);

public interface IVisaSession : IAsyncDisposable
{
    string ResourceName { get; }
    Task WriteAsync(string command, CancellationToken cancellationToken = default);
    Task<string> QueryAsync(string command, CancellationToken cancellationToken = default);
}

public interface IVisaSessionFactory
{
    Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default);
}

/// Serializes all VISA I/O through a single gate so sessions are never used concurrently.
/// Safety stop preempts waiting (not mid-call) acquirers; critical scope is used for safe-shutdown I/O.
public sealed class VisaSessionGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<bool> _critical = new();
    private CancellationTokenSource _waiterCts = new();
    private readonly object _waiterSync = new();

    /// Cancels tokens used by low-priority WaitAsync callers so Safety Stop can take the gate ASAP.
    public void PreemptWaiters()
    {
        lock (_waiterSync)
        {
            var previous = _waiterCts;
            _waiterCts = new CancellationTokenSource();
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }
    }

    /// Marks the current async flow as critical (safe-shutdown) and preempts waiters.
    public IDisposable BeginCriticalScope()
    {
        PreemptWaiters();
        _critical.Value = true;
        return new CriticalScope(this);
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// Waits for the gate using only the caller token (after preempting waiters). Prefer BeginCriticalScope for nested session I/O.
    public async Task<T> RunCriticalAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using (BeginCriticalScope())
        {
            return await RunAsync(action, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunCriticalAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using (BeginCriticalScope())
        {
            await RunAsync(action, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        if (_critical.Value)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        CancellationToken waiterToken;
        lock (_waiterSync)
        {
            waiterToken = _waiterCts.Token;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waiterToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    private sealed class CriticalScope(VisaSessionGate owner) : IDisposable
    {
        public void Dispose() => owner._critical.Value = false;
    }
}
