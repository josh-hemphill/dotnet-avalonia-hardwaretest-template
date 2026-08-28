namespace HardwareTest.Features.RunTest;

/// Live-chart time window. Null duration means the full buffered series.
public sealed record ChartTimeWindow(string Key, string Label, TimeSpan? Duration)
{
    public static ChartTimeWindow ThirtySeconds { get; } = new("30s", "30 sec", TimeSpan.FromSeconds(30));
    public static ChartTimeWindow TwoMinutes { get; } = new("2m", "2 min", TimeSpan.FromMinutes(2));
    public static ChartTimeWindow All { get; } = new("all", "All", null);

    public static IReadOnlyList<ChartTimeWindow> AllWindows { get; } =
        [ThirtySeconds, TwoMinutes, All];
}
