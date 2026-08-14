using System.Globalization;

namespace HardwareTest.Core.Hardware;

/// Scripted SCPI responses for demos and CI without a vendor VISA runtime.
public sealed class MockVisaSession : IVisaSession
{
    private readonly Dictionary<string, string> _responses;
    private readonly object _sync = new();
    private double _phase;

    public MockVisaSession(string resourceName, IReadOnlyDictionary<string, string>? responses = null)
    {
        ResourceName = resourceName;
        _responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["*IDN?"] = "MOCK,HardwareTestDemo,SN-0001,1.0",
            ["SYST:ERR?"] = "0,\"No error\"",
        };

        if (responses is not null)
        {
            foreach (var pair in responses)
            {
                _responses[pair.Key] = pair.Value;
            }
        }
    }

    public string ResourceName { get; }

    public int IoTimeoutMilliseconds { get; set; } = IviVisaSessionFactory.DefaultIoTimeoutMilliseconds;

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = command.Trim();
        lock (_sync)
        {
            if (_responses.TryGetValue(key, out var response))
            {
                return Task.FromResult(response);
            }

            if (key.StartsWith("MEAS:VOLT", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("READ?", StringComparison.OrdinalIgnoreCase))
            {
                _phase += 0.2;
                var value = 1.0 + Math.Sin(_phase) * 0.25;
                return Task.FromResult(value.ToString("F6", CultureInfo.InvariantCulture));
            }
        }

        return Task.FromResult(string.Empty);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class MockVisaSessionFactory : IVisaSessionFactory
{
    private readonly VisaSessionGate _gate;

    public MockVisaSessionFactory(VisaSessionGate gate)
    {
        _gate = gate;
    }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IVisaSession session = new TracingVisaSession(new MockVisaSession(resourceName), _gate);
        return Task.FromResult(session);
    }
}

/// Opens real IVI VISA message-based sessions when a vendor runtime is installed.
public sealed class IviVisaSessionFactory : IVisaSessionFactory
{
    public const int DefaultIoTimeoutMilliseconds = 5000;
    public const int MinIoTimeoutMilliseconds = 100;
    public const int MaxIoTimeoutMilliseconds = 120_000;

    private readonly VisaSessionGate _gate;
    private readonly int _ioTimeoutMilliseconds;

    public IviVisaSessionFactory(VisaSessionGate gate, int ioTimeoutMilliseconds = DefaultIoTimeoutMilliseconds)
    {
        _gate = gate;
        _ioTimeoutMilliseconds = Math.Clamp(ioTimeoutMilliseconds, MinIoTimeoutMilliseconds, MaxIoTimeoutMilliseconds);
    }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var raw = global::Ivi.Visa.GlobalResourceManager.Open(resourceName);
            if (raw is not global::Ivi.Visa.IMessageBasedSession messageSession)
            {
                raw.Dispose();
                throw new InvalidOperationException(
                    $"Resource '{resourceName}' is not a message-based VISA session.");
            }

            messageSession.TimeoutMilliseconds = _ioTimeoutMilliseconds;
            IVisaSession session = new TracingVisaSession(
                new IviMessageVisaSession(messageSession),
                _gate);
            return Task.FromResult(session);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to open VISA resource '{resourceName}'. Ensure a vendor VISA runtime is installed, or enable UseMockVisa.",
                ex);
        }
    }
}

internal sealed class IviMessageVisaSession : IVisaSession
{
    private readonly global::Ivi.Visa.IMessageBasedSession _session;

    public IviMessageVisaSession(global::Ivi.Visa.IMessageBasedSession session)
    {
        _session = session;
        ResourceName = session.ResourceName;
    }

    public string ResourceName { get; }

    public int IoTimeoutMilliseconds
    {
        get => _session.TimeoutMilliseconds;
        set => _session.TimeoutMilliseconds = Math.Clamp(
            value,
            IviVisaSessionFactory.MinIoTimeoutMilliseconds,
            IviVisaSessionFactory.MaxIoTimeoutMilliseconds);
    }

    public Task WriteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = command.EndsWith('\n') ? command : command + "\n";
        _session.FormattedIO.Write(payload);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = command.EndsWith('\n') ? command : command + "\n";
        _session.FormattedIO.Write(payload);
        cancellationToken.ThrowIfCancellationRequested();
        var response = _session.FormattedIO.ReadString().TrimEnd('\r', '\n');
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(response);
    }

    public ValueTask DisposeAsync()
    {
        _session.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// Chooses mock or real VISA based on settings.
public sealed class ConfigurableVisaSessionFactory : IVisaSessionFactory
{
    private readonly IVisaSessionFactory _inner;

    public ConfigurableVisaSessionFactory(bool useMock, VisaSessionGate gate)
    {
        _inner = useMock
            ? new MockVisaSessionFactory(gate)
            : new IviVisaSessionFactory(gate);
    }

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        => _inner.OpenAsync(resourceName, cancellationToken);
}
