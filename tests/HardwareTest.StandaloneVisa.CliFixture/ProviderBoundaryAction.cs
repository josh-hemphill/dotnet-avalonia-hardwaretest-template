using HardwareTest.OpenTap.Plugins.Instruments;
using InstrumentComponents.Drivers;
using InstrumentComponents.OpenTap;
using InstrumentComponents.OpenTap.Visa;
using InstrumentComponents.Runtime;
using InstrumentComponents.Scpi;
using OpenTap;
using OpenTap.Cli;

namespace HardwareTest.StandaloneVisa.CliFixture;

[Display("visa-boundary")]
public sealed class ProviderBoundaryAction : ICliAction
{
    public int Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var name in new[] { "InstrumentComponents", "InstrumentComponents.OpenTap", "InstrumentComponents.Visa", "InstrumentComponents.OpenTap.Visa", "Ivi.Visa" })
            _ = System.Reflection.Assembly.Load(name).GetTypes();
        if (OpenTapDevices.Provider is not OpenTapVisaDeviceProvider) throw new InvalidOperationException("Provider was not registered before action dispatch.");
        var preserved = new PreservedProvider();
        OpenTapDevices.Provider = preserved;
        OpenTapVisa.Register(new PreservedProvider());
        if (!ReferenceEquals(preserved, OpenTapDevices.Provider)) throw new InvalidOperationException("Existing provider was replaced.");
        var instrument = new DmmInstrument { VisaAddress = "mock://wrapper-fixture", DriverId = "keysight.34461a" };
        instrument.Open();
        if (instrument.QueryIdentity().Manufacturer != "FAKE") throw new InvalidOperationException("Published instrument identity failed.");
        if (instrument.Dmm.MeasureVoltageDc(null).Value != 1.25) throw new InvalidOperationException("Published instrument measurement failed.");
        instrument.Reset(); instrument.Close();
        if (preserved.Session is not { Closed: true }) throw new InvalidOperationException("Published instrument lease leaked.");
        Console.WriteLine("standalone-published-instrument-opened-with-explicit-driver-and-closed");
        Console.WriteLine("standalone-registered-before-dispatch-existing-provider-preserved");
        return 17;
    }
}

public sealed class PreservedProvider : IOpenTapDeviceProvider, IScpiConnector
{
    public PreservedIo? Session { get; private set; }
    public DeviceHandle Open(string address, TimeSpan timeout, DriverSelection selection)
        => DeviceDriverCatalog.Open(new(selection.DriverId ?? "", address), this);
    public IMessageIo Open(DriverOpenContext context) => Session = new PreservedIo();
}

public sealed class PreservedIo : IMessageIo
{
    public bool Closed { get; private set; }
    public void Write(string command) { }
    public string Query(string command) => command.Trim().ToUpperInvariant() switch { "*IDN?" => "FAKE,DMM,SN-1,0", "*OPC?" => "1", _ => "1.25" };
    public void Dispose() => Closed = true;
}
