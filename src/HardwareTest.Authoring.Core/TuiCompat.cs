namespace HardwareTest.Authoring;

public static class CatalogSides
{
    public const string AuthoringHome = "AuthoringHome";
    public const string TuiHome = "TuiHome";
}

public static class TuiCompatCodes
{
    public const string TypeUnknown = "TYPE_UNKNOWN";
    public const string MixinDropped = "MIXIN_DROPPED";
    public const string XmlDrift = "XML_DRIFT";
    public const string ContractFail = "CONTRACT_FAIL";
}

public sealed record TuiCompatReport(
    IReadOnlyList<CatalogDelta> Catalog,
    IReadOnlyList<RoundTripFinding> RoundTrips);

public sealed record CatalogDelta(
    string TypeName,
    string DisplayName,
    string MissingOn);

public sealed record RoundTripFinding(
    string PlanPath,
    string Code,
    string Message);

public interface ITuiCompatChecker
{
    TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome);
}

public static class TuiCompatReportExtensions
{
    /// Pack/CI fail closed when authoring types are missing on TUI, or round-trip TYPE_UNKNOWN / MIXIN_DROPPED / CONTRACT_FAIL.
    public static bool BlocksPack(this TuiCompatReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.RoundTrips.Any(IsBlockingRoundTrip))
        {
            return true;
        }

        return report.Catalog.Any(IsBlockingCatalogDelta);
    }

    private static bool IsBlockingRoundTrip(RoundTripFinding finding)
        => finding.Code is TuiCompatCodes.TypeUnknown
            or TuiCompatCodes.MixinDropped
            or TuiCompatCodes.ContractFail;

    private static bool IsBlockingCatalogDelta(CatalogDelta delta)
    {
        if (!string.Equals(delta.MissingOn, CatalogSides.TuiHome, StringComparison.Ordinal))
        {
            return false;
        }

        var typeName = delta.TypeName ?? string.Empty;
        return !typeName.StartsWith("OpenTap.TUI", StringComparison.Ordinal);
    }
}
