using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Builds instrument slot metadata from a plan without binding the live OpenTAP session.
public static class InstrumentSlotCollector
{
    public static IReadOnlyList<OpenTapInstrumentSlot> FromPlan(TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.SharePlanResources(plan);
        return InstrumentResourceAccess.CollectFromPlan(plan)
            .Select(i => new OpenTapInstrumentSlot
            {
                Name = string.IsNullOrWhiteSpace(i.Name) ? i.GetType().Name : i.Name,
                TypeName = i is global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument registered
                    ? (global::HardwareTest.OpenTap.Plugins.Instruments.DeviceDriverCatalog.All.FirstOrDefault(info => info.Id == registered.DriverId)?.Summary ?? registered.DriverId) + " · " + PublishedInstrumentComponents.Version
                    : i.GetType().Name,
                RoleHint = i is global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument device
                    ? global::HardwareTest.OpenTap.Plugins.Instruments.DeviceDriverCatalog.All.FirstOrDefault(info => info.Id == device.DriverId)?.Families ?? "device" : GuessRole(i.Name),
                ResourceName = InstrumentResourceAccess.GetResource(i),
                ConfigurationIssue = ConfigurationIssue(i),
                SupportsIdentityQuery = IdentitySupport(i),
            })
            .ToList();
    }

    private static bool? IdentitySupport(Instrument instrument)
    {
        if (instrument is not global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument registered) return null;
        try { return registered.AvailableOperations.Contains("QueryIdentity", StringComparer.Ordinal); }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or global::InstrumentComponents.Drivers.DriverException) { return false; }
    }

    private static string ConfigurationIssue(Instrument instrument)
    {
        if (instrument is not global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredInstrument registered) return string.Empty;
        try { _ = registered.AvailableOperations; return string.Empty; }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or global::InstrumentComponents.Drivers.DriverException)
        { return error.Message; }
    }

    /// Materializes a catalog entry as an in-memory plan (no session mutation).
    public static TestPlan CreatePlan(ProgramCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        OpenTapPluginSearch.SearchSerialized();

        return entry.LoadKind switch
        {
            ProgramLoadKind.FactorySample => SampleProgramFactory.Create(),
            ProgramLoadKind.FactoryBoardDemo => BoardDemoProgramFactory.Create(),
            ProgramLoadKind.FactorySweepDemo => SweepDemoProgramFactory.Create(),
            ProgramLoadKind.FactoryTimingDemo => TimingDemoProgramFactory.Create(),
            ProgramLoadKind.FactoryEnvelopeSweepDemo => EnvelopeSweepDemoProgramFactory.Create(),
            ProgramLoadKind.FactoryStationHealthDemo => StationHealthDemoProgramFactory.Create(),
            ProgramLoadKind.TapPlanFile => LoadTapPlanFile(entry.Path),
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.LoadKind, "Unknown program load kind."),
        };
    }

    private static TestPlan LoadTapPlanFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Test plan not found.", path);
        }

        var plan = TestPlan.Load(path);
        LogicalInstrumentBindings.Canonicalize(plan, path);
        return plan;
    }

    private static string GuessRole(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "dmm";
        }

        var n = name.Trim().ToLowerInvariant();
        if (n.Contains("scope", StringComparison.Ordinal))
        {
            return "scope";
        }

        if (n.Contains("supply", StringComparison.Ordinal) || n.Contains("psu", StringComparison.Ordinal))
        {
            return "psu";
        }

        return "dmm";
    }
}
