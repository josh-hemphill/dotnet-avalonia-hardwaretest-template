using HardwareTest.OpenTap.Plugins.Mixins;

namespace HardwareTest.OpenTap.Host;

/// Tile kinds for DisplayRole mapping (Avalonia-free).
public enum PresentationTileKind
{
    Timeseries,
    Scalar,
    Passband,
    Timing,
    Text,
}

/// Maps Mixins DisplayRole strings to tile kinds without Avalonia or ReactiveUI.
public static class PresentationRoles
{
    public const string Timeseries = PresentationDisplayRoles.Timeseries;
    public const string Scalar = PresentationDisplayRoles.Scalar;
    public const string Passband = PresentationDisplayRoles.Passband;
    public const string Timing = PresentationDisplayRoles.Timing;

    /// Resolves a known role to a tile kind; unknown roles return null (text-only degradation).
    public static PresentationTileKind? TryMapRole(string? displayRole)
    {
        if (string.IsNullOrWhiteSpace(displayRole))
        {
            return null;
        }

        if (string.Equals(displayRole, Timeseries, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Timeseries;
        }

        if (string.Equals(displayRole, Scalar, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Scalar;
        }

        if (string.Equals(displayRole, Passband, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Passband;
        }

        if (string.Equals(displayRole, Timing, StringComparison.OrdinalIgnoreCase))
        {
            return PresentationTileKind.Timing;
        }

        return null;
    }
}
