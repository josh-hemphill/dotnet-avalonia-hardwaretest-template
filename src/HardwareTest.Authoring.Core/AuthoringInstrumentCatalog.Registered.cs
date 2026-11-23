using System.Globalization;
using System.Runtime.CompilerServices;
using HardwareTest.OpenTap.Plugins.Instruments;
using OpenTap;

namespace HardwareTest.Authoring;

public static partial class AuthoringInstrumentCatalog
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AuthoringInstrumentAdapter RegisteredAdapter() => new(DeviceDriverCatalog.RegisteredTypeId,
        "Registered vendor device", LibraryPackage, "HardwareTest.OpenTap.Plugins.Instruments.dll", ["VisaAddress"],
        ["DriverId", "IoTimeoutMilliseconds", "Channels", "Outputs", "Commissioning"],
        DeviceDriverCatalog.OperationNames.Select(operation => "Device." + operation).ToArray(),
        true, true, ConstructRegistered, SerializeRegistered)
    {
        Description = "Choose Keysight / Agilent / HP, Keithley, Rigol, Siglent or Tektronix. All capabilities of a multifunction device share one resource and connection. Software preview; hardware qualification is pending."
    };

    public static void EnsureCompatible(InstrumentRef instrument, string functionId)
    {
        EnsureCompatible(instrument.TypeId, functionId);
        if (instrument.TypeId != DeviceDriverCatalog.RegisteredTypeId) return;
        var operation = functionId switch
        {
            AuthoringFunctionIds.BasicIdentityCheck or AuthoringFunctionIds.IcIdentityQuery => "QueryIdentity",
            AuthoringFunctionIds.BasicSafeShutdown or AuthoringFunctionIds.IcSafeShutdown => "Shutdown",
            _ when functionId.StartsWith("Device.", StringComparison.Ordinal) => functionId[7..],
            _ => functionId
        };
        try
        {
            var resource = (RegisteredInstrument)ConstructRegistered(instrument);
            if (!resource.AvailableOperations.Contains(operation, StringComparer.Ordinal))
                throw new AuthoringWorkspaceException($"{DeviceDriverCatalog.Find(resource.DriverId).DisplayName} does not advertise {operation} for the configured inventory.");
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InstrumentComponents.Drivers.DriverException)
        { throw new AuthoringWorkspaceException(error.Message); }
    }

    internal static DeviceBinding DeviceBindingFor(InstrumentRef instrument) => new(
        instrument.Settings.GetValueOrDefault("DriverId") ?? string.Empty, instrument.VisaAddress,
        instrument.Settings.GetValueOrDefault("Channels") ?? string.Empty, instrument.Settings.GetValueOrDefault("Outputs") ?? string.Empty,
        instrument.Settings.GetValueOrDefault("Commissioning") ?? string.Empty);

    private static Instrument ConstructRegistered(InstrumentRef slot)
    {
        var id = slot.Settings.GetValueOrDefault("DriverId") ?? string.Empty;
        _ = DeviceDriverCatalog.Find(id);
        var timeout = slot.Settings.TryGetValue("IoTimeoutMilliseconds", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 5000;
        if (timeout is < 100 or > 120000) throw new FormatException("I/O timeout must be between 100 and 120000 milliseconds.");
        return new RegisteredInstrument
        {
            Name = slot.SlotName,
            DriverId = id,
            VisaAddress = slot.VisaAddress,
            IoTimeoutMilliseconds = timeout,
            Channels = slot.Settings.GetValueOrDefault("Channels") ?? string.Empty,
            Outputs = slot.Settings.GetValueOrDefault("Outputs") ?? string.Empty,
            Commissioning = slot.Settings.GetValueOrDefault("Commissioning") ?? string.Empty
        };
    }

    private static InstrumentRef SerializeRegistered(Instrument resource)
    {
        var instrument = (RegisteredInstrument)resource;
        return new(instrument.Name, DeviceDriverCatalog.RegisteredTypeId, instrument.VisaAddress)
        {
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DriverId"] = instrument.DriverId,
                ["IoTimeoutMilliseconds"] = instrument.IoTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
                ["Channels"] = instrument.Channels,
                ["Outputs"] = instrument.Outputs,
                ["Commissioning"] = instrument.Commissioning
            }
        };
    }
}
