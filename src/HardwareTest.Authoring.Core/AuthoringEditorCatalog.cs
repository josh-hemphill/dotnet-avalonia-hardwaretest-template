using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Closed inspector lists (display roles, TF methods, gates) plus well-known catalog defaults.
public static class AuthoringEditorCatalog
{
    public static IReadOnlyList<string> DisplayRoles { get; } =
    [
        PresentationRoles.Scalar,
        PresentationRoles.Passband,
        PresentationRoles.Timeseries,
        PresentationRoles.Timing,
    ];

    public static IReadOnlyList<string> YUnits { get; } =
        ["V", "ms", "%"];

    public static IReadOnlyList<string> TfMethods { get; } =
        ["filter", "filtfilt"];

    public static IReadOnlyList<string> ReportKinds { get; } =
        ["status", "certification"];

    public static IReadOnlyList<string> ProgramKinds { get; } =
        ["dut", "stationHealth"];

    public static IReadOnlyList<string> StationHealthGates { get; } =
        ["warn", "block"];

    public static IReadOnlyList<string> ChannelKeys(ProgramDraft? draft)
    {
        if (draft is null)
        {
            return [];
        }

        return AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure)
            .Select(metric => metric.ChannelKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
