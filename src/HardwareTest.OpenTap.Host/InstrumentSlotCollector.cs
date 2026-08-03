using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Builds instrument slot metadata from a plan without binding the live OpenTAP session.
public static class InstrumentSlotCollector
{
    public static IReadOnlyList<OpenTapInstrumentSlot> FromPlan(TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return InstrumentResourceAccess.CollectFromPlan(plan)
            .Select(i => new OpenTapInstrumentSlot
            {
                Name = string.IsNullOrWhiteSpace(i.Name) ? i.GetType().Name : i.Name,
                TypeName = i.GetType().Name,
                RoleHint = GuessRole(i.Name),
                ResourceName = InstrumentResourceAccess.GetResource(i),
            })
            .ToList();
    }

    /// Materializes a catalog entry as an in-memory plan (no session mutation).
    public static TestPlan CreatePlan(ProgramCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();

        return entry.LoadKind switch
        {
            ProgramLoadKind.FactorySample => SampleProgramFactory.Create(),
            ProgramLoadKind.FactoryBoardDemo => BoardDemoProgramFactory.Create(),
            ProgramLoadKind.FactorySweepDemo => SweepDemoProgramFactory.Create(),
            ProgramLoadKind.FactoryTimingDemo => TimingDemoProgramFactory.Create(),
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

        return TestPlan.Load(path);
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
