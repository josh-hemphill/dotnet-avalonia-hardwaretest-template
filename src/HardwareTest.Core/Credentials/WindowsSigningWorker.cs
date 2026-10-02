using System.Collections.Concurrent;

namespace HardwareTest.Core.Credentials;

/// Retains one STA/OLE apartment throughout native acquisition, signing and release.
internal sealed class WindowsSigningWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    public WindowsSigningWorker(IWindowsSigningNative native)
    {
        _thread = new Thread(() => Run(native)) { IsBackground = true, Name = "Smart-card signing STA" };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }
    private void Run(IWindowsSigningNative native)
    {
        var initialized = false;
        try
        {
            native.InitializeApartment(); initialized = true; _ready.SetResult();
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        }
        catch (Exception ex) { _ready.TrySetException(ex); }
        finally { if (initialized) WindowsOperatorCredentialBroker.DisposeResource(native.UninitializeApartment); }
    }
    public async Task<T> InvokeAsync<T>(Func<T> work)
    {
        await _ready.Task.ConfigureAwait(false);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_queue)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Add(() => { try { completion.SetResult(work()); } catch (Exception ex) { completion.SetException(ex); } });
        }
        return await completion.Task.ConfigureAwait(false);
    }
    public void Dispose()
    {
        lock (_queue) { if (_disposed) return; _disposed = true; _queue.CompleteAdding(); }
        _thread.Join(); _queue.Dispose();
    }
}
