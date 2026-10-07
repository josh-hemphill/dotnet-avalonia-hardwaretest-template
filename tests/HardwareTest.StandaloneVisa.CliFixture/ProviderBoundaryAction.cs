using InstrumentComponents.OpenTap;
using InstrumentComponents.OpenTap.Visa;
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
        // Resolve actual managed payload references, including any System.Text.Json
        // dependency, without invoking a vendor resource manager.
        foreach (var name in new[] { "InstrumentComponents", "InstrumentComponents.OpenTap", "InstrumentComponents.Visa", "InstrumentComponents.OpenTap.Visa", "Ivi.Visa" })
        {
            var assembly = System.Reflection.Assembly.Load(name);
            _ = assembly.GetTypes();
            foreach (var dependency in assembly.GetReferencedAssemblies().Where(reference => reference.Name == "System.Text.Json"))
                _ = System.Reflection.Assembly.Load(dependency);
        }
        if (OpenTapScpiIo.Provider is not OpenTapVisaScpiIoProvider) throw new InvalidOperationException("Provider was not registered before action dispatch.");
        var preserved = new PreservedProvider();
        OpenTapScpiIo.Provider = preserved;
        OpenTapVisa.Register();
        if (!ReferenceEquals(preserved, OpenTapScpiIo.Provider)) throw new InvalidOperationException("Existing provider was replaced.");
        var instrument = new DmmInstrument { VisaAddress = "mock://wrapper-fixture" };
        instrument.Open();
        if (instrument.QueryIdn().FormatResponse() != "FAKE,DMM,SN-1,0") throw new InvalidOperationException("Published instrument identity failed.");
        if (instrument.Dmm.MeasureVoltageDc() != 1.25) throw new InvalidOperationException("Published instrument measurement failed.");
        instrument.Reset();
        instrument.Close();
        if (preserved.Session is not { Closed: true }) throw new InvalidOperationException("Published instrument lease leaked.");
        Console.WriteLine("standalone-published-instrument-opened-with-embedded-registry-and-closed");
        Console.WriteLine("standalone-registered-before-dispatch-existing-provider-preserved");
        return 17;
    }
}

public sealed class PreservedProvider : IOpenTapScpiIoProvider
{
    public PreservedIo? Session { get; private set; }
    public IScpiIo Open(string visaAddress, TimeSpan ioTimeout) => Session = new PreservedIo { IoTimeout = ioTimeout };
}

public sealed class PreservedIo : IScpiIo
{
    public TimeSpan IoTimeout { get; set; }
    public bool Closed { get; private set; }
    public void Write(string command) { }
    public string Query(string command) => command.Trim().ToUpperInvariant() switch
    {
        "*IDN?" => "FAKE,DMM,SN-1,0",
        "*OPC?" => "1",
        _ => "1.25",
    };
    public void Dispose() => Closed = true;
}
