using HardwareTest.Core.Runs;
using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// One report-kind row in Program settings (id + whether this program includes it).
public sealed record AuthoringCatalogToggle(string Id, bool Included);

/// Workspace + session union for report kinds, program kinds, slots, and Y units.
/// Well-known ids are suggestions, not a closed demo-program catalog.
public static class AuthoringWorkspaceCatalog
{
    public static IReadOnlyList<string> DefaultReportKinds { get; } =
    [
        ReportKinds.Status,
        ReportKinds.Certification,
    ];

    public static IReadOnlyList<string> DefaultProgramKinds { get; } =
    [
        ProgramKinds.Dut,
        ProgramKinds.StationHealth,
    ];

    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static IReadOnlyList<string> ReportKindOptions(
        AuthoringManifest? manifest,
        IReadOnlyList<ProgramDraft> programs,
        ProgramDraft? selected)
        => Union(
            DefaultReportKinds,
            manifest?.Catalogs?.ReportKinds,
            programs.SelectMany(program => program.Sidecar.ReportKinds ?? []),
            selected?.Sidecar.ReportKinds,
            [selected?.Sidecar.DefaultReportKind]);

    public static IReadOnlyList<string> ProgramKindOptions(
        AuthoringManifest? manifest,
        IReadOnlyList<ProgramDraft> programs,
        ProgramDraft? selected)
        => Union(
            DefaultProgramKinds,
            manifest?.Catalogs?.ProgramKinds,
            programs.Select(program => program.Sidecar.ProgramKind),
            [selected?.Sidecar.ProgramKind]);

    public static IReadOnlyList<string> YUnitOptions(ProgramDraft? selected)
        => Union(
            AuthoringEditorCatalog.YUnits,
            selected is null
                ? []
                : AuthoringRecipeCatalog.EnumerateMetrics(selected.Measure).Select(metric => metric.YUnit));

    public static IReadOnlyList<string> Union(params IEnumerable<string?>?[] sources)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var raw in source)
            {
                var token = Normalize(raw);
                if (token is null || !seen.Add(token))
                {
                    continue;
                }

                result.Add(token);
            }
        }

        return result;
    }

    public static bool Contains(IReadOnlyList<string> items, string? token)
    {
        var normalized = Normalize(token);
        return normalized is not null
               && items.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> Remember(List<string> items, string token)
    {
        if (!Contains(items, token))
        {
            items.Add(token);
        }

        return items;
    }
}
