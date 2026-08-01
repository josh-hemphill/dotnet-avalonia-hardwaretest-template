using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.Core.Runs;

namespace HardwareTest.OpenTap.Host;

public enum ProgramLoadKind
{
    FactorySample,
    FactoryBoardDemo,
    FactorySweepDemo,
    TapPlanFile,
}

/// One runnable program shown on Run / Instruments.
public sealed class ProgramCatalogEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public string DutFamily { get; init; } = "generic";
    public ProgramRequirements Requirements { get; init; } = ProgramRequirements.Sample;
    public ProgramLoadKind LoadKind { get; init; } = ProgramLoadKind.TapPlanFile;
    public bool IsBuiltIn { get; init; }
    /// Report kinds to generate after a run (default status only).
    public IReadOnlyList<string> ReportKinds { get; init; } = [HardwareTest.Core.Runs.ReportKinds.Status];
    /// Kind opened by Results double-click / primary open (default status).
    public string DefaultReportKind { get; init; } = HardwareTest.Core.Runs.ReportKinds.Status;
    /// When true (default), Run Selected keeps SafeShutdown enabled. Set false only for software-only selection scopes.
    public bool SelectionIncludesCleanup { get; init; } = true;
}

/// Optional sidecar beside a .TapPlan: `{planId}.program.json`.
public sealed class ProgramSidecar
{
    public string? DisplayName { get; set; }
    public string? DutFamily { get; set; }
    public bool? RequireSerial { get; set; }
    public bool? RequirePartNumber { get; set; }
    public bool? RequireRevision { get; set; }
    public bool? RequireOperator { get; set; }
    public string[]? ReportKinds { get; set; }
    public string? DefaultReportKind { get; set; }
    /// When false, Run Selected excludes SafeShutdownStep (suite-scoped cleanup only). Default true.
    public bool? SelectionIncludesCleanup { get; set; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProgramSidecar))]
public partial class ProgramCatalogJsonContext : JsonSerializerContext;

/// Discovers built-in factories and on-disk TapPlans for the shell.
public static class ProgramCatalog
{
    public static IReadOnlyList<ProgramCatalogEntry> Enumerate(IEnumerable<string>? extraDirectories = null)
    {
        var byId = new Dictionary<string, ProgramCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var builtIn in BuiltIns())
        {
            byId[builtIn.Id] = builtIn;
        }

        foreach (var dir in EnumerateDirectories(extraDirectories))
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.TapPlan"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (byId.ContainsKey(id))
                {
                    continue;
                }

                byId[id] = FromTapPlanFile(file, id);
            }
        }

        return byId.Values
            .OrderBy(CatalogSortKey)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Resolve report kinds for a plan id (catalog entry or default status).
    public static IReadOnlyList<string> ResolveReportKinds(string? planId, IEnumerable<string>? extraDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return [HardwareTest.Core.Runs.ReportKinds.Status];
        }

        var entry = Enumerate(extraDirectories)
            .FirstOrDefault(e => string.Equals(e.Id, planId, StringComparison.OrdinalIgnoreCase));
        if (entry?.ReportKinds is { Count: > 0 })
        {
            return entry.ReportKinds;
        }

        return [HardwareTest.Core.Runs.ReportKinds.Status];
    }

    /// Resolve which report kind Results should open on double-click.
    public static string ResolveDefaultReportKind(string? planId, IEnumerable<string>? extraDirectories = null)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return HardwareTest.Core.Runs.ReportKinds.Status;
        }

        var entry = Enumerate(extraDirectories)
            .FirstOrDefault(e => string.Equals(e.Id, planId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return HardwareTest.Core.Runs.ReportKinds.Status;
        }

        if (!string.IsNullOrWhiteSpace(entry.DefaultReportKind))
        {
            return entry.DefaultReportKind.Trim();
        }

        return entry.ReportKinds.FirstOrDefault() ?? HardwareTest.Core.Runs.ReportKinds.Status;
    }

    private static int CatalogSortKey(ProgramCatalogEntry entry)
        => entry.Id switch
        {
            "sample" => 0,
            "board-demo" => 1,
            "sweep-demo" => 2,
            _ => entry.IsBuiltIn ? 3 : 4,
        };

    public static IEnumerable<string> EnumerateDirectories(IEnumerable<string>? extraDirectories = null)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Programs");
        var repoPlans = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plans", "opentap"));
        if (Directory.Exists(repoPlans))
        {
            yield return repoPlans;
        }

        if (extraDirectories is null)
        {
            yield break;
        }

        foreach (var dir in extraDirectories)
        {
            if (!string.IsNullOrWhiteSpace(dir))
            {
                yield return dir;
            }
        }
    }

    private static IEnumerable<ProgramCatalogEntry> BuiltIns()
    {
        yield return new ProgramCatalogEntry
        {
            Id = "sample",
            DisplayName = "Sample Hardware Suite",
            Path = SampleProgramFactory.EmbeddedName,
            DutFamily = "demo",
            Requirements = ProgramRequirements.Sample,
            LoadKind = ProgramLoadKind.FactorySample,
            IsBuiltIn = true,
            ReportKinds = [HardwareTest.Core.Runs.ReportKinds.Status, HardwareTest.Core.Runs.ReportKinds.Certification],
        };
        yield return new ProgramCatalogEntry
        {
            Id = "board-demo",
            DisplayName = BoardDemoProgramFactory.DisplayName,
            Path = BoardDemoProgramFactory.EmbeddedName,
            DutFamily = "demo",
            Requirements = ProgramRequirements.Sample,
            LoadKind = ProgramLoadKind.FactoryBoardDemo,
            IsBuiltIn = true,
            ReportKinds = [HardwareTest.Core.Runs.ReportKinds.Status, HardwareTest.Core.Runs.ReportKinds.Certification],
        };
        yield return new ProgramCatalogEntry
        {
            Id = "sweep-demo",
            DisplayName = SweepDemoProgramFactory.DisplayName,
            Path = SweepDemoProgramFactory.EmbeddedName,
            DutFamily = "demo",
            Requirements = ProgramRequirements.Sample,
            LoadKind = ProgramLoadKind.FactorySweepDemo,
            IsBuiltIn = true,
            ReportKinds = [HardwareTest.Core.Runs.ReportKinds.Status],
        };
    }

    private static ProgramCatalogEntry FromTapPlanFile(string file, string id)
    {
        var sidecar = TryLoadSidecar(file, id);
        var family = string.IsNullOrWhiteSpace(sidecar?.DutFamily) ? "generic" : sidecar!.DutFamily!.Trim();
        var requirements = BuildRequirements(sidecar, family);
        var kinds = sidecar?.ReportKinds is { Length: > 0 }
            ? (IReadOnlyList<string>)sidecar.ReportKinds
            : [HardwareTest.Core.Runs.ReportKinds.Status];
        var defaultKind = string.IsNullOrWhiteSpace(sidecar?.DefaultReportKind)
            ? kinds[0]
            : sidecar!.DefaultReportKind!.Trim();
        return new ProgramCatalogEntry
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(sidecar?.DisplayName) ? id : sidecar!.DisplayName!.Trim(),
            Path = file,
            DutFamily = family,
            Requirements = requirements,
            LoadKind = ProgramLoadKind.TapPlanFile,
            IsBuiltIn = false,
            ReportKinds = kinds,
            DefaultReportKind = defaultKind,
            SelectionIncludesCleanup = sidecar?.SelectionIncludesCleanup ?? true,
        };
    }

    private static ProgramRequirements BuildRequirements(ProgramSidecar? sidecar, string family)
    {
        var baseline = ProgramRequirements.FromFamily(family);
        if (sidecar is null)
        {
            return baseline;
        }

        return new ProgramRequirements
        {
            RequireSerial = sidecar.RequireSerial ?? baseline.RequireSerial,
            RequirePartNumber = sidecar.RequirePartNumber ?? baseline.RequirePartNumber,
            RequireRevision = sidecar.RequireRevision ?? baseline.RequireRevision,
            RequireOperator = sidecar.RequireOperator ?? baseline.RequireOperator,
        };
    }

    private static ProgramSidecar? TryLoadSidecar(string tapPlanPath, string id)
    {
        var sidecarPath = Path.Combine(Path.GetDirectoryName(tapPlanPath) ?? string.Empty, $"{id}.program.json");
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(sidecarPath),
                ProgramCatalogJsonContext.Default.ProgramSidecar);
        }
        catch
        {
            return null;
        }
    }
}
