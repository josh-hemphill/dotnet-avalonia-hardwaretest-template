using System.Collections.Frozen;
using System.Globalization;
using InstrumentComponents.Drivers;
using InstrumentComponents.Drivers.Catalog;
using InstrumentComponents.Drivers.Keysight;
using InstrumentComponents.Drivers.Tektronix;
using InstrumentComponents.Runtime;
using InstrumentComponents.Scpi;

namespace HardwareTest.OpenTap.Plugins.Instruments;

public sealed record DeviceDriverInfo(string Id, string Manufacturer, string Model, string Package,
    IReadOnlyList<string> Operations, string Commissioning = "")
{
    public string EndpointScheme { get; init; } = "scpi";
    public string ConfigurationExample { get; init; } = string.Empty;
    public string DisplayName => $"{Manufacturer} {Model}";
    public string Families => string.Join(", ", Operations.Select(name => Enum.Parse<CatalogOperation>(name).Describe().Kind)
        .Where(kind => kind is not ("Device" or "Common")).Distinct().Select(kind => kind switch { "Dmm" => "DMM", "DcPowerSupply" => "DC power supply", "FunctionGenerator" => "Function generator", "SpectrumAnalyzer" => "Spectrum analyzer", "PowerMeter" => "Power meter", "Oscilloscope" => "Oscilloscope", _ => kind }));
    public string Summary => $"{DisplayName} · {Families}";
}

public sealed record DeviceBinding(string DriverId, string Address, string Channels = "", string Outputs = "",
    string Commissioning = "", int TimeoutMilliseconds = 5000);

/// Explicit public-package composition. Catalog inspection never opens a connector.
public static partial class DeviceDriverCatalog
{
    public const string Version = "0.3.0-alpha.1";
    public const string RegisteredTypeId = "HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument";
    private static readonly Endpoint CatalogEndpoint = new("scpi", "catalog-only");
    public static IReadOnlyList<DeviceDriverInfo> All { get; } = BuildCatalog();
    public static IReadOnlyList<string> OperationNames { get; } = Enum.GetNames<CatalogOperation>();

    public static DeviceDriverInfo Find(string id) => All.FirstOrDefault(item => item.Id == id)
        ?? throw new ArgumentException($"Choose a registered device model; '{id}' is not in the bundled {Version} catalog.");

    private static IReadOnlyList<IDriverFactory> GenericFactories(IScpiConnector connector) =>
    [
        .. global::InstrumentComponents.Drivers.Keysight.Drivers.Factories(connector),
        .. global::InstrumentComponents.Drivers.Keithley.Drivers.Factories(connector),
        .. global::InstrumentComponents.Drivers.Rigol.Drivers.Factories(connector),
        .. global::InstrumentComponents.Drivers.Siglent.Drivers.Factories(connector),
        .. global::InstrumentComponents.Drivers.Tektronix.Drivers.Factories(connector)
    ];

    private static IReadOnlyList<DeviceDriverInfo> BuildCatalog()
    {
        var result = GenericFactories(new PassiveConnector()).Select(factory => Info(factory.Descriptor)).ToList();
        foreach (var model in Enum.GetValues<QualifiedScpiModel>())
        {
            var commissioning = model == QualifiedScpiModel.Agilent33120A
                ? QualifiedScpiCommissioning.ContinuousSine50OhmVppOutputChangesAuthorized : QualifiedScpiCommissioning.GpibScpiConfirmed;
            var factory = new QualifiedScpiFactory(model, [new(CatalogEndpoint, commissioning)], new PassiveConnector());
            result.Add(Info(factory.Descriptor) with { Commissioning = commissioning.ToString() });
        }
        result.Add(Info(new Tds1002Factory([new(CatalogEndpoint, new(Tds1002CommunicationModule.Tds2Cm, Tds1002Probe.X1, Tds1002Probe.X1, Tds1002Commissioning.QualifiedGpibNormalModeConfirmed))], new PassiveConnector()).Descriptor)
            with
        { Commissioning = nameof(Tds1002Commissioning.QualifiedGpibNormalModeConfirmed) });
        result.AddRange(SpecializedCatalog());
        return result.OrderBy(item => item.Manufacturer, StringComparer.Ordinal).ThenBy(item => item.Model, StringComparer.Ordinal).ToArray();
    }

    private static DeviceDriverInfo Info(DriverDescriptor descriptor)
    {
        var identity = descriptor.Rules.OfType<ExactIdentityRule>().FirstOrDefault();
        return new(descriptor.Id, identity?.Manufacturer ?? descriptor.Id.Split('.')[0], identity?.Model ?? descriptor.Id,
            descriptor.Id.Split('.')[0] switch
            {
                "keysight" => "InstrumentComponents.Drivers.Keysight",
                "keithley" => "InstrumentComponents.Drivers.Keithley",
                "rigol" => "InstrumentComponents.Drivers.Rigol",
                "siglent" => "InstrumentComponents.Drivers.Siglent",
                _ => "InstrumentComponents.Drivers.Tektronix"
            }, Operations(descriptor.Operations));
    }

    private static string[] Operations(IReadOnlySet<Operation> operations) => Enum.GetValues<CatalogOperation>()
        .Where(operation => operations.Contains(operation.Runtime())).Select(operation => operation.ToString()).ToArray();

    public static IDriverFactory CreateFactory(DeviceBinding binding, IScpiConnector connector)
    {
        var info = Find(binding.DriverId);
        if (string.IsNullOrWhiteSpace(binding.Address)) throw new ArgumentException("Enter the device resource address.");
        if (binding.TimeoutMilliseconds is < 100 or > 120000) throw new ArgumentException("I/O timeout must be between 100 and 120000 milliseconds.");
        if (SpecializedFactory(binding, connector) is { } specialized) return specialized;
        if (info.Commissioning.Length > 0)
        {
            if (binding.DriverId != "tektronix.tds1002" && binding.Commissioning != info.Commissioning)
                throw new ArgumentException($"{info.DisplayName} requires explicit commissioning: {info.Commissioning}. Confirm the transport and device configuration before running.");
            var endpoint = new Endpoint("scpi", binding.Address);
            if (binding.DriverId == "tektronix.tds1002")
                return new Tds1002Factory([new(endpoint, TdsConfiguration(binding.Commissioning))], connector);
            var model = binding.DriverId switch
            {
                "keysight.34401a" => QualifiedScpiModel.Agilent34401A,
                "keysight.33120a" => QualifiedScpiModel.Agilent33120A,
                _ => QualifiedScpiModel.Agilent53132A
            };
            return new QualifiedScpiFactory(model, [new(endpoint, Enum.Parse<QualifiedScpiCommissioning>(info.Commissioning))], connector);
        }
        if (!string.IsNullOrWhiteSpace(binding.Commissioning)) throw new ArgumentException("This model does not accept a commissioned-factory assertion.");
        var profile = BoundProfile(binding);
        var descriptor = GenericFactories(connector).Single(factory => factory.Descriptor.Id == binding.DriverId).Descriptor;
        return new ScpiFactory(profile, "scpi", connector, descriptor.Rules);
    }

    private static Tds1002Configuration TdsConfiguration(string text)
    {
        var parts = text.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || parts[0] != nameof(Tds1002Commissioning.QualifiedGpibNormalModeConfirmed))
            throw new ArgumentException("TDS1002 commissioning: QualifiedGpibNormalModeConfirmed;Tds2Cm;X10;X10 (use the actual module and both probe factors).");
        return new(Enum.Parse<Tds1002CommunicationModule>(parts[1]), Enum.Parse<Tds1002Probe>(parts[2]), Enum.Parse<Tds1002Probe>(parts[3]), Tds1002Commissioning.QualifiedGpibNormalModeConfirmed);
    }

    private static IEnumerable<ScpiProfile> Profiles() =>
    [
        .. global::InstrumentComponents.Drivers.Keysight.Drivers.Profiles(),
        .. global::InstrumentComponents.Drivers.Keithley.Drivers.Profiles(),
        .. global::InstrumentComponents.Drivers.Rigol.Drivers.Profiles(),
        .. global::InstrumentComponents.Drivers.Siglent.Drivers.Profiles(),
        .. global::InstrumentComponents.Drivers.Tektronix.Drivers.Profiles()
    ];

    public static IReadOnlyList<string> AvailableOperations(DeviceBinding binding)
    {
        // Metadata describes inventory, independent of a station's eventual address.
        // The real address is still mandatory in Open/CreateFactory before allocation.
        var metadata = string.IsNullOrWhiteSpace(binding.Address) ? binding with { Address = "UNBOUND" } : binding;
        return Operations(CreateFactory(metadata, new PassiveConnector()).Descriptor.Operations);
    }

    public static DeviceHandle Open(DeviceBinding binding, IScpiConnector connector, IReadOnlySet<Operation>? required = null)
        => new DriverRegistry([CreateFactory(binding, connector)]).Open(new(new(Find(binding.DriverId).EndpointScheme, binding.Address), new()), new(binding.DriverId), required: required);

    private static uint[]? ParseChannels(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var values = text.Split(',', StringSplitOptions.TrimEntries).Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length > 4096 || values.Any(value => value == 0) || values.Distinct().Count() != values.Length)
            throw new ArgumentException("Channels must be unique positive numbers separated by commas.");
        return values;
    }

    private static Output[]? ParseOutputs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        return text.Split(',', StringSplitOptions.TrimEntries).Select(value => value == "fgen"
            ? new Output(OutputKind.FunctionGenerator)
            : value.StartsWith("psu:", StringComparison.Ordinal) && uint.TryParse(value.AsSpan(4), out var channel) && channel > 0
                ? new Output(OutputKind.DcPowerSupply, channel)
                : throw new ArgumentException("Outputs: use none, fgen, or psu:1,psu:2 after confirming every physical output.")).ToArray();
    }

    private sealed class PassiveConnector : IScpiConnector
    {
        public IMessageIo Open(DriverOpenContext context) => throw new InvalidOperationException("Catalog inspection must not open hardware.");
    }
}
