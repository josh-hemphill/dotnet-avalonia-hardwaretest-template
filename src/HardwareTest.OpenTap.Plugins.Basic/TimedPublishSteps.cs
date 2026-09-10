using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Publishes one Sample row with optional limits / ElapsedMs and an optional Event mark (Phase M contract).
[Display(
    "Publish Timed Sample",
    Groups: ["HardwareTest", "Measure"],
    Description: "Publish a Sample with optional LimitLow/LimitHigh/ElapsedMs and an Event on the same clock.")]
public sealed class PublishTimedSampleStep : RuntimeAwareTestStep
{
    [Display("Channel", Order: 1)]
    public string Channel { get; set; } = "VDC";

    [Display("Value", Order: 2)]
    public double Value { get; set; }

    [Display("Index", Order: 3)]
    public int Index { get; set; }

    [Display("Limit low", Order: 4)]
    public double? LimitLow { get; set; }

    [Display("Limit high", Order: 5)]
    public double? LimitHigh { get; set; }

    [Display("Elapsed ms", Order: 6, Description: "Plan-owned time from step start. Leave empty to use host ingest time.")]
    public double? ElapsedMs { get; set; }

    [Display("Event name", Order: 7)]
    public string EventName { get; set; } = "cfg";

    [Display("Event label", Order: 8, Description: "When set, also publish an Event row (e.g. bit3).")]
    public string? EventLabel { get; set; }

    [Display("Event value", Order: 9)]
    public double? EventValue { get; set; }

    public override void Run()
    {
        WaitIfPaused();
        Results.Publish(
            "Sample",
            new List<string> { "Channel", "Index", "Value", "LimitLow", "LimitHigh", "ElapsedMs" },
            Channel,
            Index,
            Value,
            LimitLow ?? double.NaN,
            LimitHigh ?? double.NaN,
            ElapsedMs ?? double.NaN);

        if (!string.IsNullOrWhiteSpace(EventLabel) && ElapsedMs is { } elapsed)
        {
            Results.Publish(
                "Event",
                new List<string> { "Name", "ElapsedMs", "Label", "Value" },
                string.IsNullOrWhiteSpace(EventName) ? "cfg" : EventName,
                elapsed,
                EventLabel,
                EventValue ?? double.NaN);
        }

        UpgradeVerdict(Verdict.Pass);
    }
}
