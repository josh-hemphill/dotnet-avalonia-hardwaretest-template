using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed record AuthoringInstrumentAvailability(bool Available, string? Reason);

/// A closed adapter contract: installed OpenTAP types are not implicitly creatable.
public sealed record AuthoringInstrumentAdapter(
    string TypeId, string DisplayName, string RequiredPackage, string AssemblyFile,
    IReadOnlyList<string> AddressFields, IReadOnlyList<string> ConfigurationFields, IReadOnlyList<string> CompatibleFunctions,
    bool SupportsIdentity, bool SupportsShutdown, Func<InstrumentRef, Instrument> Construct,
    Func<Instrument, InstrumentRef> Serialize)
{
    public IReadOnlyList<string> RequiredPayloadFiles { get; init; } = [];

    public AuthoringInstrumentAvailability Availability(OpenTapHome? home = null)
    {
        if (home is null) return new(true, null);
        return AuthoringAdapterPayloadInspection.Inspect(this, home);
    }
}

public static class AuthoringInstrumentCatalog
{
    private static readonly string[] DmmFunctions =
    [AuthoringFunctionIds.BasicAcquireVoltage, AuthoringFunctionIds.BasicBitSweepAcquire, AuthoringFunctionIds.BasicMeanGte];

    public static IReadOnlyList<AuthoringInstrumentAdapter> All { get; } =
    [
        new(typeof(MockDmmInstrument).FullName!, "Mock DMM", "HardwareTest Basic", "HardwareTest.OpenTap.Plugins.Basic.dll",
            ["VisaAddress", "ResourceName"], [], DmmFunctions, true, true,
            slot => new MockDmmInstrument { Name = slot.SlotName, VisaAddress = slot.VisaAddress, ResourceName = slot.VisaAddress },
            resource => new(resource.Name, typeof(MockDmmInstrument).FullName!, ((MockDmmInstrument)resource).VisaAddress)),
        new(AuthoringVisaInstrumentAdapter.InstrumentType.FullName!, "VISA DMM", "HardwareTest VISA", "HardwareTest.OpenTap.Plugins.Visa.dll",
            ["VisaAddress"], ["IoTimeoutMilliseconds"], DmmFunctions, true, true,
            slot => AuthoringVisaInstrumentAdapter.Construct(slot.SlotName, slot.VisaAddress, slot.Settings),
            resource => new(resource.Name, AuthoringVisaInstrumentAdapter.InstrumentType.FullName!, AuthoringVisaInstrumentAdapter.Address(resource))
            { Settings = AuthoringVisaInstrumentAdapter.Settings(resource) })
        { RequiredPayloadFiles = ["HardwareTest.Core.dll", "Ivi.Visa.dll"] }
    ];

    public static bool DeclaresVisa(AuthoringWorkspace workspace) => workspace.Manifest.Dependencies.Any(dependency =>
        string.Equals(dependency.Package, OpenTapHomeBootstrapper.VisaPackageName, StringComparison.OrdinalIgnoreCase));

    internal static bool IsDeclaredVisaPackage(string packageXml) => IsPackageNamed(packageXml, OpenTapHomeBootstrapper.VisaPackageName);

    internal static bool IsPackageNamed(string packageXml, string expectedName)
    {
        try
        {
            return string.Equals((string?)XDocument.Load(packageXml).Root?.Attribute("Name"),
                expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return false; }
    }

    public static bool TryGet(string typeId, out AuthoringInstrumentAdapter adapter)
    {
        adapter = All.FirstOrDefault(item => string.Equals(item.TypeId, typeId, StringComparison.Ordinal))!;
        return adapter is not null;
    }

    public static Instrument Create(InstrumentRef slot, OpenTapHome? home = null)
    {
        if (!TryGet(slot.TypeId, out var adapter))
            throw new AuthoringWorkspaceException($"INSTRUMENT_UNAVAILABLE: '{slot.TypeId}' has no registered authoring adapter. Preserve imported source or explicitly choose a supported type.");
        var availability = adapter.Availability(home);
        if (!availability.Available) throw new AuthoringWorkspaceException($"INSTRUMENT_UNAVAILABLE: {availability.Reason}");
        if (slot.Settings is null || slot.Settings.Keys.Any(key => !adapter.ConfigurationFields.Contains(key, StringComparer.Ordinal)))
            throw new AuthoringWorkspaceException($"INSTRUMENT_CONFIGURATION: '{adapter.DisplayName}' contains unsupported configuration fields. Preserve source and use the declared adapter fields.");
        try { return adapter.Construct(slot); }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new AuthoringWorkspaceException($"INSTRUMENT_CONFIGURATION: Invalid configuration for '{adapter.DisplayName}': {error.Message}");
        }
    }

    public static void EnsureCompatible(string typeId, string functionId)
    {
        if (!TryGet(typeId, out var adapter)) throw new AuthoringWorkspaceException($"INSTRUMENT_UNAVAILABLE: '{typeId}' has no registered authoring adapter.");
        var compatible = functionId switch
        {
            AuthoringFunctionIds.BasicIdentityCheck => adapter.SupportsIdentity,
            AuthoringFunctionIds.BasicSafeShutdown => adapter.SupportsShutdown,
            _ => adapter.CompatibleFunctions.Contains(functionId, StringComparer.Ordinal)
        };
        if (!compatible) throw new AuthoringWorkspaceException($"INSTRUMENT_INCOMPATIBLE: '{adapter.DisplayName}' does not support '{functionId}'. Choose a compatible instrument or function.");
    }

    public static bool CanReplace(ProgramDraft draft, string fromSlot, InstrumentRef replacement)
    {
        if (!TryGet(replacement.TypeId, out var adapter)) return false;
        if (replacement.Settings is null || replacement.Settings.Keys.Any(key => !adapter.ConfigurationFields.Contains(key, StringComparer.Ordinal))) return false;
        if (draft.Setup.OfType<IdentitySetup>().Any(x => Same(x.InstrumentSlot, fromSlot)) && !adapter.SupportsIdentity) return false;
        if (draft.Cleanup.IncludeSafeShutdown && AuthoringCleanup.ResolveSlots(draft).Any(x => Same(x, fromSlot)) && !adapter.SupportsShutdown) return false;
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
        {
            var function = metric.Source switch
            {
                MeasureSource m when Same(m.InstrumentSlot, fromSlot) => m.FunctionId,
                AlgorithmSource a when AuthoringFunctionCatalog.HasInstrumentDependency(a.AlgorithmId) && Same(a.InstrumentSlot, fromSlot) => a.AlgorithmId,
                _ => null
            };
            if (function is not null && !adapter.CompatibleFunctions.Contains(function, StringComparer.Ordinal)) return false;
        }
        return true;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
