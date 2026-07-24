using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Mixins;

/// Well-known DisplayRole values for presentation (string + AvailableValues; no UI types).
public static class PresentationDisplayRoles
{
    public const string Timeseries = "timeseries";
    public const string Scalar = "scalar";
    public const string Passband = "passband";
}

/// Declares metric identity and display role for shell charts/gauges (Phase J) without Avalonia types.
public sealed class PresentationMixin : IMixin
{
    [Display("Channel key", Groups: ["Presentation"], Description: "Stable metric id for history and charts (e.g. rail.3v3).", Order: 1)]
    public string ChannelKey { get; set; } = string.Empty;

    [Display("Display role", Groups: ["Presentation"], Description: "How the shell should present this step's metrics.", Order: 2)]
    [AvailableValues(nameof(DisplayRoleChoices))]
    public string DisplayRole { get; set; } = PresentationDisplayRoles.Timeseries;

    public IEnumerable<string> DisplayRoleChoices { get; } =
    [
        PresentationDisplayRoles.Timeseries,
        PresentationDisplayRoles.Scalar,
        PresentationDisplayRoles.Passband,
    ];

    [Display("Y unit", Groups: ["Presentation"], Description: "Unit label for plots/gauges (e.g. V).", Order: 3)]
    public string YUnit { get; set; } = string.Empty;
}
