using System.Globalization;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// SCPI DC voltmeter over IVI VISA (real hardware path).
[Display("VISA DMM", Groups: ["HardwareTest"], Description: "SCPI DC voltmeter via VisaAddress.")]
public sealed class VisaDmmInstrument : HardwareDmm
{
    private global::Ivi.Visa.IMessageBasedSession? _session;
    private string _visaAddress = string.Empty;

    public VisaDmmInstrument()
    {
        Name = "VISA DMM";
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

        var raw = global::Ivi.Visa.GlobalResourceManager.Open(VisaAddress);
        if (raw is not global::Ivi.Visa.IMessageBasedSession message)
        {
            raw.Dispose();
            throw new InvalidOperationException($"Resource '{VisaAddress}' is not message-based.");
        }

        message.TimeoutMilliseconds = Math.Clamp(IoTimeoutMilliseconds, 100, 120_000);
        _session = message;
        base.Open();
    }

    public override void Close()
    {
        _session?.Dispose();
        _session = null;
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

    private void Write(string command)
    {
        var session = RequireSession();
        var payload = command.EndsWith('\n') ? command : command + "\n";
        session.FormattedIO.Write(payload);
    }

    private string Query(string command)
    {
        Write(command);
        return RequireSession().FormattedIO.ReadString().TrimEnd('\r', '\n');
    }

    private global::Ivi.Visa.IMessageBasedSession RequireSession()
        => _session ?? throw new InvalidOperationException("VISA DMM is not open.");
}
