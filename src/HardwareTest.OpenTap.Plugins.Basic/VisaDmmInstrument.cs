using System.Globalization;
using HardwareTest.Core.Hardware;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// SCPI DC voltmeter over the process IVisaBroker (mock or real).
[Display("VISA DMM", Groups: ["HardwareTest"], Description: "SCPI DC voltmeter via VisaAddress.")]
public sealed class VisaDmmInstrument : HardwareDmm
{
    private readonly IVisaBroker? _injected;
    private IVisaSession? _session;
    private string _visaAddress = string.Empty;

    public VisaDmmInstrument()
    {
        Name = "VISA DMM";
    }

    /// Test / host constructor — skips the session-local binding.
    public VisaDmmInstrument(IVisaBroker broker)
        : this()
    {
        _injected = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    [Display("Visa Address", Order: 1)]
    public string VisaAddress
    {
        get => _visaAddress;
        set => _visaAddress = value ?? string.Empty;
    }

    [Display("IO Timeout (ms)", Order: 2)]
    public int IoTimeoutMilliseconds { get; set; } = 5000;

    public override void Open()
    {
        if (string.IsNullOrWhiteSpace(VisaAddress))
        {
            Log.Warning("VISA DMM VisaAddress is empty; open skipped.");
            base.Open();
            return;
        }

        using var cts = new CancellationTokenSource(ClampTimeout());
        _session = ResolveBroker().OpenAsync(VisaAddress, cts.Token).GetAwaiter().GetResult();
        base.Open();
    }

    public override void Close()
    {
        var session = _session;
        _session = null;
        session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Close();
    }

    public override string QueryIdn() => Query("*IDN?");

    public override void ConfigureDcVolts() => Write("CONF:VOLT:DC");

    public override double ReadVoltage()
    {
        var raw = Query("READ?");
        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"VISA DMM READ? returned non-numeric '{raw}'.");
        }

        return value;
    }

    public override void OutputOff()
    {
        // Many DMMs have no output stage; treat as no-op so SafeShutdown stays portable.
    }

    public override void Reset() => Write("*RST");

    private IVisaBroker ResolveBroker() => _injected ?? VisaBrokerHost.Require();

    private int ClampTimeout() => Math.Clamp(IoTimeoutMilliseconds, 100, 120_000);

    private void Write(string command)
    {
        using var cts = new CancellationTokenSource(ClampTimeout());
        RequireSession().WriteAsync(command, cts.Token).GetAwaiter().GetResult();
    }

    private string Query(string command)
    {
        using var cts = new CancellationTokenSource(ClampTimeout());
        return RequireSession().QueryAsync(command, cts.Token).GetAwaiter().GetResult();
    }

    private IVisaSession RequireSession()
        => _session ?? throw new InvalidOperationException("VISA DMM is not open.");
}
