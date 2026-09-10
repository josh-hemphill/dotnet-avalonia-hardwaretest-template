using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Queries mock station cal and publishes age + offset Scalars (executed, never skipped).
[Display(
    "Report Station Health",
    Groups: ["HardwareTest", "Station"],
    Description: "Query mock daily self-cal and publish cal.dc.offset plus cal.age.hours.")]
public sealed class ReportStationHealthStep : RuntimeAwareTestStep
{
    public const string OffsetMetric = "cal.dc.offset";
    public const string AgeMetric = "cal.age.hours";
    public const string MeasuredSource = "measured";
    public const string CachedSource = "cached";

    [Display("Offset volts", Order: 1, Description: "Mock DC offset (queried cal).")]
    public double OffsetVolts { get; set; } = 0.002;

    [Display("Offset limit low", Order: 2)]
    public double OffsetLimitLow { get; set; } = -0.01;

    [Display("Offset limit high", Order: 3)]
    public double OffsetLimitHigh { get; set; } = 0.01;

    [Display("Age hours", Order: 4, Description: "Hours since the mock cal. 0 = just queried.")]
    public double AgeHours { get; set; }

    [Display("Max age hours", Order: 5)]
    public double MaxAgeHours { get; set; } = 24;

    [Display(
        "Result source",
        Order: 6,
        Description: "measured (queried instrument) or cached (recalled file).")]
    public string ResultSource { get; set; } = MeasuredSource;

    [Display(
        "Mock cal path",
        Order: 7,
        Description: "Optional JSON { offsetVolts, ageHours, resultSource } overlay.")]
    public string MockCalPath { get; set; } = string.Empty;

    [Display("Fail when out of band", Order: 8)]
    public bool FailWhenOutOfBand { get; set; } = true;

    public override void Run()
    {
        WaitIfPaused();
        ApplyMockFileIfPresent();

        var source = NormalizeResultSource(ResultSource);
        PublishScalar(OffsetMetric, OffsetVolts, "V", OffsetLimitLow, OffsetLimitHigh, source);
        PublishScalar(AgeMetric, AgeHours, "h", double.NaN, MaxAgeHours, source);

        if (!FailWhenOutOfBand)
        {
            UpgradeVerdict(Verdict.Pass);
            return;
        }

        if (OffsetVolts < OffsetLimitLow || OffsetVolts > OffsetLimitHigh)
        {
            Log.Error("{0}={1} outside [{2},{3}]", OffsetMetric, OffsetVolts, OffsetLimitLow, OffsetLimitHigh);
            UpgradeVerdict(Verdict.Fail);
            return;
        }

        if (AgeHours > MaxAgeHours)
        {
            Log.Error("{0}={1} > LimitHigh={2}", AgeMetric, AgeHours, MaxAgeHours);
            UpgradeVerdict(Verdict.Fail);
            return;
        }

        UpgradeVerdict(Verdict.Pass);
    }

    private void PublishScalar(
        string name,
        double value,
        string unit,
        double limitLow,
        double limitHigh,
        string resultSource)
    {
        Results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow", "LimitHigh", "ResultSource" },
            name,
            value,
            unit,
            limitLow,
            limitHigh,
            resultSource);
    }

    private void ApplyMockFileIfPresent()
    {
        if (string.IsNullOrWhiteSpace(MockCalPath) || !File.Exists(MockCalPath))
        {
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(MockCalPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("offsetVolts", out var offset))
            {
                OffsetVolts = offset.GetDouble();
            }

            if (root.TryGetProperty("ageHours", out var age))
            {
                AgeHours = age.GetDouble();
            }

            if (root.TryGetProperty("resultSource", out var source)
                && source.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                ResultSource = source.GetString() ?? ResultSource;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read mock cal at {0}: {1}", MockCalPath, ex.Message);
        }
    }

    private static string NormalizeResultSource(string? source)
        => string.Equals(source, CachedSource, StringComparison.OrdinalIgnoreCase)
            ? CachedSource
            : MeasuredSource;
}
