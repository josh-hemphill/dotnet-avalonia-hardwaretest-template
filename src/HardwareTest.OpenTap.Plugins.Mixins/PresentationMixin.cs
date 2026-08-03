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
    [Display(
        "Channel key",
        Groups: ["Presentation"],
        Description: "Stable metric id for history and gauges (e.g. rail.3v3.mean). Keep stable across plan revisions.",
        Order: 1)]
    public string ChannelKey { get; set; } = string.Empty;

    [Display(
        "Display role",
        Groups: ["Presentation"],
        Description: "Band-first: use scalar/passband for pass criteria with limits; timeseries only when operators need the waveform shape.",
        Order: 2)]
    [AvailableValues(nameof(DisplayRoleChoices))]
    public string DisplayRole { get; set; } = PresentationDisplayRoles.Timeseries;

    public IEnumerable<string> DisplayRoleChoices { get; } =
    [
        PresentationDisplayRoles.Timeseries,
        PresentationDisplayRoles.Scalar,
        PresentationDisplayRoles.Passband,
    ];

    [Display("Y unit", Groups: ["Presentation"], Description: "Unit label for plots/gauges (e.g. V or ms).", Order: 3)]
    public string YUnit { get; set; } = string.Empty;

    [Display("History enabled", Groups: ["Presentation"], Description: "Include this metric in DUT history drift checks.", Order: 4)]
    public bool HistoryEnabled { get; set; } = true;

    [Display("History watch %", Groups: ["Presentation"], Description: "Watch when |delta| vs prior mean reaches this percent. Leave empty for shell default (5).", Order: 5)]
    public double? HistoryWatchPercent { get; set; }

    [Display("History alert %", Groups: ["Presentation"], Description: "Alert when |delta| vs prior mean reaches this percent. Leave empty for shell default (10).", Order: 6)]
    public double? HistoryAlertPercent { get; set; }
}
