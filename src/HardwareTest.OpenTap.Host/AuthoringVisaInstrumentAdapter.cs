using System.Globalization;
using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

// The host owns the closed VISA adapter boundary; authoring only consumes OpenTAP and primitive values.
internal static class AuthoringVisaInstrumentAdapter
{
    internal static Type InstrumentType => typeof(VisaDmmInstrument);

    internal static Instrument Construct(string name, string address, IReadOnlyDictionary<string, string> settings) =>
        new VisaDmmInstrument
        {
            Name = name,
            VisaAddress = address,
            IoTimeoutMilliseconds = settings.TryGetValue("IoTimeoutMilliseconds", out var timeout)
                ? int.Parse(timeout, CultureInfo.InvariantCulture) : IviVisaSessionFactory.DefaultIoTimeoutMilliseconds
        };

    internal static string Address(Instrument instrument) => ((VisaDmmInstrument)instrument).VisaAddress;

    internal static Dictionary<string, string> Settings(Instrument instrument) => new()
    {
        ["IoTimeoutMilliseconds"] = ((VisaDmmInstrument)instrument).IoTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture)
    };
}
