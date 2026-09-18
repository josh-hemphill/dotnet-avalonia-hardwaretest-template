namespace HardwareTest.Features.RunTest;

/// Live-chart sample window. Null duration means the full buffered series (not axis zoom).
public sealed record ChartTimeWindow(string Key, string Label, TimeSpan? Duration)
{
    public const string OperatorTip =
        "Limits which samples are plotted on the elapsed axis. This is not zoom — pick All samples to see the whole run.";

    public static ChartTimeWindow ThirtySeconds { get; } = new("30s", "Last 30 sec", TimeSpan.FromSeconds(30));
    public static ChartTimeWindow TwoMinutes { get; } = new("2m", "Last 2 min", TimeSpan.FromMinutes(2));
    public static ChartTimeWindow All { get; } = new("all", "All samples", null);

    public static IReadOnlyList<ChartTimeWindow> AllWindows { get; } =
        [ThirtySeconds, TwoMinutes, All];
}
