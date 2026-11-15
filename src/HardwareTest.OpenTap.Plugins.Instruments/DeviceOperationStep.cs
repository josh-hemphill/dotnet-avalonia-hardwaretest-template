using InstrumentComponents.Drivers.Catalog;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Instruments;

[Display("Device operation", Groups: ["HardwareTest", "Instrument Components"], Description: "Executes an explicitly advertised operation on a shared registered device.")]
public sealed class DeviceOperationStep : TestStep, IFormatName
{
    [Display("Instrument", Order: 1)]
    public RegisteredInstrument Instrument { get; set; } = null!;
    [Display("Operation", Order: 2)]
    public string Operation { get; set; } = nameof(CatalogOperation.DmmMeasureVoltageDc);
    [Display("Arguments", Order: 3, Description: "One name=value per line. Omit optional arguments to preserve the device default; unknown or duplicate names are rejected.")]
    public string Arguments { get; set; } = string.Empty;
    [Display("Result channel", Order: 4)]
    public string Channel { get; set; } = "measurement";

    [Display("Limit low", Group: "Criteria")]
    public double? LimitLow { get; set; }
    [Display("Limit high", Group: "Criteria")]
    public double? LimitHigh { get; set; }

    public DeviceOperationStep()
    {
        Rules.Add(() => Instrument is not null, "Choose an instrument.", nameof(Instrument));
        Rules.Add(() => ValidationIssue() is null, () => ValidationIssue() ?? string.Empty, nameof(Arguments));
    }

    public string GetFormattedName() => $"{Name} @ {Instrument?.Name ?? "unbound"}";

    public string? ValidationIssue()
    {
        try
        {
            if (LimitLow is { } low && !double.IsFinite(low) || LimitHigh is { } high && !double.IsFinite(high) || LimitLow > LimitHigh)
                return "Limits must be finite and low must not exceed high.";
            var request = DeviceOperationRequests.Create(Operation, Arguments);
            if (Instrument is not null && !Instrument.AvailableOperations.Contains(Operation, StringComparer.Ordinal))
                return $"{DeviceDriverCatalog.Find(Instrument.DriverId).DisplayName} does not advertise {Operation} for this inventory. Choose another operation or confirm its inventory in Hardware.";
            if (Instrument is not null) DeviceDriverCatalog.ValidateRequest(Instrument.Binding, request);
            return null;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or global::InstrumentComponents.Drivers.DriverException)
        { return error.Message; }
    }

    public override void Run()
    {
        RegisteredDevices.WaitForExecution?.Invoke();
        if (Instrument is null || ValidationIssue() is { })
            throw new InvalidOperationException(ValidationIssue() ?? "Choose an instrument before running.");
        var reply = Instrument.Invoke(DeviceOperationRequests.Create(Operation, Arguments));
        switch (reply)
        {
            case CatalogReply.Numeric { Value.Status: global::InstrumentComponents.Drivers.MeasurementStatus.Overload } overloaded:
                Results.Publish("MeasurementOutcome", ["Channel", "Status", "Unit", "Resource", "Driver", "Operation"],
                    Channel, "Overload", Symbol(overloaded.Value.Unit), Instrument.VisaAddress, Instrument.DriverId, Operation);
                UpgradeVerdict(Verdict.Fail);
                return;
            case CatalogReply.Numeric numeric:
                Results.Publish("Scalar", ["Name", "Value", "Unit", "LimitLow", "LimitHigh"], Channel, numeric.Value.Value, Symbol(numeric.Value.Unit), LimitLow, LimitHigh);
                break;
            case CatalogReply.State state:
                Results.Publish("Scalar", ["Name", "Value", "Unit", "LimitLow", "LimitHigh"], Channel, state.Value ? 1.0 : 0.0, "", LimitLow, LimitHigh);
                break;
            case CatalogReply.Count count:
                Results.Publish("Scalar", ["Name", "Value", "Unit", "LimitLow", "LimitHigh"], Channel, (double)count.Value, "count", LimitLow, LimitHigh);
                break;
            case CatalogReply.Identity identity:
                Results.Publish("Identity", ["Idn"], string.Join(",", identity.Value.Manufacturer, identity.Value.Model, identity.Value.Serial, identity.Value.Revision));
                break;
            case CatalogReply.Trace trace:
                for (var i = 0; i < trace.Value.Values.Count; i++)
                    Results.Publish("Sample", ["Channel", "Index", "Value", "Unit", "ElapsedMs"], Channel, i, trace.Value.Values[i], Symbol(trace.Value.Unit), trace.Value.SampleIntervalS is { } interval ? i * interval * 1000 : (double?)null);
                break;
        }
        var scalar = reply switch { CatalogReply.Numeric numeric => numeric.Value.Value, CatalogReply.State state => state.Value ? 1.0 : 0.0, CatalogReply.Count count => (double)count.Value, _ => (double?)null };
        UpgradeVerdict(scalar is { } value && (value < LimitLow || value > LimitHigh) ? Verdict.Fail : Verdict.Pass);
    }

    private static string Symbol(Unit unit) => unit switch
    {
        Unit.Volt => "V",
        Unit.Ampere => "A",
        Unit.Ohm => "Ω",
        Unit.Hertz => "Hz",
        Unit.Second => "s",
        Unit.Watt => "W",
        Unit.Dbm => "dBm",
        Unit.Celsius => "°C",
        Unit.Kelvin => "K",
        Unit.Count => "count",
        _ => unit.ToString()
    };
}
