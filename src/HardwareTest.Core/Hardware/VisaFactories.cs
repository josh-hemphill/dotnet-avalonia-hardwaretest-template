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
            ["*OPC?"] = "1",
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

            // Explicit alpha demo replies; these are simulated values, never device qualification.
            if (key == ":MEAS:VOLT:DC? DEF,DEF")
            {
                return Task.FromResult(ResourceName switch
                {
                    "MOCK::AGILENT-OVERLOAD" => "+9.90000000E+37",
                    "MOCK::AGILENT-ZERO" => "+0.00000000E+00",
                    _ => "+1.25000000E+00"
                });
            }

            if (key.Equals(":SOUR:FREQ?", StringComparison.OrdinalIgnoreCase) || key.Equals(":MEASure:FREQuency?", StringComparison.OrdinalIgnoreCase)) return Task.FromResult("1000");
            if (key.StartsWith(":MEASure:VPP? CHAN", StringComparison.OrdinalIgnoreCase)) return Task.FromResult("1.25");
            if (key.StartsWith(":MEAS:VOLT", StringComparison.OrdinalIgnoreCase) || key.StartsWith("MEAS:VOLT", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("READ?", StringComparison.OrdinalIgnoreCase))
            {
                _phase += 0.2;
                var value = 1.0 + (Math.Sin(_phase) * 0.25);
                return Task.FromResult(value.ToString("F6", CultureInfo.InvariantCulture));
            }
        }

        return Task.FromResult(string.Empty);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class MockVisaSessionFactory(VisaSessionGate gate) : IVisaSessionFactory
{
    private readonly VisaSessionGate _gate = gate;

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IVisaSession session = new TracingVisaSession(new MockVisaSession(resourceName), _gate);
        return Task.FromResult(session);
    }
}

/// Opens real IVI VISA message-based sessions when a vendor runtime is installed.
public sealed class IviVisaSessionFactory(VisaSessionGate gate, int ioTimeoutMilliseconds = IviVisaSessionFactory.DefaultIoTimeoutMilliseconds) : IVisaSessionFactory
{
    public const int DefaultIoTimeoutMilliseconds = 5000;
    public const int MinIoTimeoutMilliseconds = 100;
    public const int MaxIoTimeoutMilliseconds = 120_000;

    private readonly VisaSessionGate _gate = gate;
    private readonly int _ioTimeoutMilliseconds = Math.Clamp(ioTimeoutMilliseconds, MinIoTimeoutMilliseconds, MaxIoTimeoutMilliseconds);

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var raw = Ivi.Visa.GlobalResourceManager.Open(resourceName);
            if (raw is not Ivi.Visa.IMessageBasedSession messageSession)
            {
                raw.Dispose();
                throw new InvalidOperationException(
                    $"Resource '{resourceName}' is not a message-based VISA session.");
            }

            try
            {
                messageSession.TimeoutMilliseconds = _ioTimeoutMilliseconds;
                messageSession.SendEndEnabled = true;
                messageSession.TerminationCharacter = (byte)'\n';
                messageSession.TerminationCharacterEnabled = true;
                IVisaSession session = new TracingVisaSession(new IviMessageVisaSession(messageSession), _gate);
                return Task.FromResult(session);
            }
            catch
            {
                try { messageSession.Dispose(); } catch (Exception) { }
                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to open VISA resource '{resourceName}'. Ensure a vendor VISA runtime is installed, or enable UseMockVisa.",
                ex);
        }
    }
}

internal sealed class IviMessageVisaSession(Ivi.Visa.IMessageBasedSession session) : IVisaSession
{
    private readonly Ivi.Visa.IMessageBasedSession _session = session;

    public string ResourceName { get; } = session.ResourceName;

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
        _session.RawIO.Write(System.Text.Encoding.UTF8.GetBytes(payload));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = command.EndsWith('\n') ? command : command + "\n";
        _session.RawIO.Write(System.Text.Encoding.UTF8.GetBytes(payload));
        cancellationToken.ThrowIfCancellationRequested();
        // One bounded complete message, preserving spaces in identities and CSV bodies.
        // A count-limited formatted ReadString stops at whitespace and can split an IDN.
        const int maximumResponseBytes = 1_048_576;
        var bytes = _session.RawIO.Read(maximumResponseBytes + 1L, out var status);
        if (bytes.Length > maximumResponseBytes || status is not (Ivi.Visa.ReadStatus.EndReceived or Ivi.Visa.ReadStatus.TerminationCharacterEncountered))
            throw new IOException("VISA response exceeded the complete-message bound. Close the connection and resynchronize before retrying.");
        var response = new System.Text.UTF8Encoding(false, true).GetString(bytes).TrimEnd('\r', '\n');
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
public sealed class ConfigurableVisaSessionFactory(bool useMock, VisaSessionGate gate) : IVisaSessionFactory
{
    private readonly IVisaSessionFactory _inner = useMock
            ? new MockVisaSessionFactory(gate)
            : new IviVisaSessionFactory(gate);

    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        => _inner.OpenAsync(resourceName, cancellationToken);
}
