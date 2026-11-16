using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Plugins.Instruments;
using InstrumentComponents.Drivers;
using InstrumentComponents.Scpi;

namespace HardwareTest.OpenTap.Host;

/// Adapts complete SCPI message bodies onto the process-owned broker; no vendor resource manager.
internal sealed class BrokerDeviceConnector(IVisaBroker broker, TimeSpan timeout) : IScpiConnector
{
    private static readonly global::OpenTap.SessionLocal<CancellationToken> Cancellation = new(CancellationToken.None, autoDispose: false);
    private static readonly global::OpenTap.SessionLocal<Action<string>?> DeviceFailure = new(null, autoDispose: false);
    internal static Action<string>? OnDeviceFailure { get => DeviceFailure.Value; set => DeviceFailure.Value = value; }
    internal static CancellationToken RunCancellation { get => Cancellation.Value; set => Cancellation.Value = value; }
    private int TimeoutMilliseconds => Math.Clamp((int)timeout.TotalMilliseconds,
        IviVisaSessionFactory.MinIoTimeoutMilliseconds, IviVisaSessionFactory.MaxIoTimeoutMilliseconds);

    internal static void Register(IVisaBroker broker)
        => RegisteredDevices.Connector = timeout => new BrokerDeviceConnector(broker, timeout);

    public IMessageIo Open(DriverOpenContext context)
    {
        if (context.Ownership != ConnectionOwnership.Owned)
            throw new ArgumentException("The HardwareTest broker connector requires an owned lease.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(RunCancellation);
        cancellation.CancelAfter(TimeoutMilliseconds);
        IVisaSession session;
        try { session = broker.OpenAsync(context.Candidate.Endpoint.Address, cancellation.Token).GetAwaiter().GetResult(); }
        catch (Exception error) { ReportFailure(context.Candidate.Endpoint.Address, "open", error, RunCancellation, TimeoutMilliseconds); throw; }
        try
        {
            session.IoTimeoutMilliseconds = TimeoutMilliseconds;
            return new Message(session, TimeoutMilliseconds, RunCancellation);
        }
        catch (Exception error)
        {
            ReportFailure(session.ResourceName, "configure timeout", error, RunCancellation, TimeoutMilliseconds);
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception) { }
            throw;
        }
    }

    private static void ReportFailure(string resource, string operation, Exception error, CancellationToken runCancellation, int timeoutMilliseconds)
    {
        if (error is OperationCanceledException && runCancellation.IsCancellationRequested) return;
        var reason = error is OperationCanceledException ? $"timed out after {timeoutMilliseconds} ms" : error.Message;
        OnDeviceFailure?.Invoke($"{resource} ({operation}): {reason}");
    }

    private sealed class Message(IVisaSession session, int timeoutMilliseconds, CancellationToken runCancellation) : IMessageIo
    {
        private int _disposed;
        public void Write(string message)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runCancellation);
            cancellation.CancelAfter(timeoutMilliseconds);
            try { session.WriteAsync(message, cancellation.Token).GetAwaiter().GetResult(); }
            catch (Exception error) { ReportFailure(session.ResourceName, "write", error, runCancellation, timeoutMilliseconds); throw; }
        }
        public string Query(string message)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(runCancellation);
            cancellation.CancelAfter(timeoutMilliseconds);
            try { return session.QueryAsync(message, cancellation.Token).GetAwaiter().GetResult(); }
            catch (Exception error) { ReportFailure(session.ResourceName, "query", error, runCancellation, timeoutMilliseconds); throw; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) { ReportFailure(session.ResourceName, "close", error, CancellationToken.None, timeoutMilliseconds); throw; }
        }
    }
}
