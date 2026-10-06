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
        Console.WriteLine("standalone-registered-before-dispatch-existing-provider-preserved");
        return 17;
    }
}

public sealed class PreservedProvider : IOpenTapScpiIoProvider
{
    public IScpiIo Open(string visaAddress, TimeSpan ioTimeout) => throw new InvalidOperationException("No physical I/O in this fixture.");
}

