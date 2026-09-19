using HardwareTest.Core.Runs;
using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// One report-kind row in Program settings (id + whether this program includes it).
public sealed record AuthoringCatalogToggle(string Id, bool Included, bool CanRemove = false);

/// One measure/algorithm setting key shown in the inspector.
public sealed record AuthoringSettingRow(
    string Key,
    string Value,
    string Label,
    AuthoringSettingKind Kind = AuthoringSettingKind.Text,
    IReadOnlyList<string>? Choices = null,
    string? ValueTooltip = null,
    string? ValuePlaceholder = null,
    double? Minimum = null,
    bool ChoiceIsEditable = false)
{
    public AuthoringSettingRow(string key, string value)
        : this(AuthoringMetricSettingCatalog.CreateRow("*", key, value, []))
    {
    }

    public bool IsText => Kind == AuthoringSettingKind.Text;

    public bool IsBoolean => Kind == AuthoringSettingKind.Boolean;

    public bool IsChoice => Kind == AuthoringSettingKind.Choice;

    public bool IsInteger => Kind == AuthoringSettingKind.Integer;

    public bool IsDouble => Kind == AuthoringSettingKind.Double;

    public bool IsNumber => IsInteger || IsDouble;

    public bool BoolValue
        => bool.TryParse(Value, out var parsed) && parsed;

    public decimal? NumberValue
        => AuthoringInvariantNumbers.TryParseDecimal(Value, out var number) ? number : null;

    public decimal MinimumValue
        => Minimum is { } minimum ? (decimal)minimum : IsInteger ? 0 : decimal.MinValue;

    public decimal IncrementValue => IsInteger ? 1 : 0.1m;

    public string NumberFormat => IsInteger ? "0" : "0.########";
}

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

    public static IReadOnlyList<string> RequiredFieldOptions(
        AuthoringManifest? manifest,
        IReadOnlyList<ProgramDraft> programs,
        ProgramDraft? selected)
        => Union(
            RequiredFieldIds.Known,
            manifest?.Catalogs?.RequiredFields,
            programs.SelectMany(program => RequiredFieldIds.FromSidecar(program.Sidecar)),
            selected is null ? [] : RequiredFieldIds.FromSidecar(selected.Sidecar));

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

    public static bool Forget(List<string> items, string? token)
    {
        var normalized = Normalize(token);
        return normalized is not null
               && items.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public static bool IsProtectedReportKind(string? kind)
        => Contains(DefaultReportKinds, kind);

    public static bool IsProtectedProgramKind(string? kind)
        => Contains(DefaultProgramKinds, kind);

    public static bool IsProtectedRequiredField(string? id)
        => RequiredFieldIds.IsKnown(id);
}
