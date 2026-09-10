using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Walks mock config bits while publishing Sample + Event on one ElapsedMs clock.
[Display(
    "Bit Sweep Acquire",
    Groups: ["HardwareTest", "Measure"],
    Description: "Publish Event+Sample per bit. Optional series compliance against LimitLow/LimitHigh.")]
public sealed class BitSweepAcquireStep : RuntimeAwareTestStep
{
    [Display("Instrument", Order: 1)]
    public HardwareDmm Instrument { get; set; } = null!;

    [Display("Channel", Order: 2)]
    public string Channel { get; set; } = "rail.x";

    [Display("Bit count", Order: 3)]
    public int BitCount { get; set; } = 4;

    [Display("Interval ms", Order: 4)]
    public int IntervalMs { get; set; } = 5;

    [Display("Limit low", Order: 5)]
    public double? LimitLow { get; set; }

    [Display("Limit high", Order: 6)]
    public double? LimitHigh { get; set; }

    [Display("Series compliance", Order: 7)]
    [AvailableValues(nameof(SeriesComplianceChoices))]
    public string SeriesCompliance { get; set; } = SeriesComplianceModes.None;

    public IEnumerable<string> SeriesComplianceChoices { get; } = SeriesComplianceModes.Choices;

    [Display("Dwell limit ms", Order: 8)]
    public double? DwellLimitMs { get; set; }

    [Display("Fail when out of band", Order: 9)]
    public bool FailWhenOutOfBand { get; set; } = true;

    [Display(
        "Scripted values",
        Order: 10,
        Description: "Optional comma-separated voltages (demo/CI). Empty = read the instrument.")]
    public string ScriptedValues { get; set; } = string.Empty;

    [Display("Publish summaries", Order: 11, Description: "Off by default so a timeseries mixin on this step does not relabel Scalar summaries.")]
    public bool PublishSummaries { get; set; }

    public override void Run()
    {
        WaitIfPaused();
        var count = Math.Max(1, BitCount);
        var scripted = SeriesComplianceModes.ParseScriptedValues(ScriptedValues);
        var values = new List<double>(count);
        var failed = false;
        var dwellMs = 0.0;

        if (Instrument is not null)
        {
            Instrument.ConfigureDcVolts();
        }

        for (var i = 0; i < count; i++)
        {
            TapThread.ThrowIfAborted();
            WaitIfPaused();
            var elapsed = i * Math.Max(0, IntervalMs);
            var word = 1 << i;
            Results.Publish(
                "Event",
                new List<string> { "Name", "ElapsedMs", "Label", "Value" },
                "cfg",
                (double)elapsed,
                $"bit{i}",
                (double)word);

            var value = i < scripted.Count
                ? scripted[i]
                : Instrument is null
                    ? double.NaN
                    : Instrument.ReadVoltage();
            values.Add(value);
            Results.Publish(
                "Sample",
                new List<string> { "Channel", "Index", "Value", "LimitLow", "LimitHigh", "ElapsedMs" },
                Channel,
                i,
                value,
                LimitLow ?? double.NaN,
                LimitHigh ?? double.NaN,
                (double)elapsed);

            if (SeriesComplianceModes.ShouldFailSample(
                    SeriesCompliance,
                    FailWhenOutOfBand,
                    value,
                    LimitLow,
                    LimitHigh,
                    DwellLimitMs,
                    IntervalMs,
                    ref dwellMs))
            {
                Log.Error(
                    "{0}[{1}]={2} failed {3} [{4} … {5}] dwell={6} ms at {7} ms",
                    Channel,
                    i,
                    value,
                    SeriesCompliance,
                    LimitLow,
                    LimitHigh,
                    dwellMs,
                    elapsed);
                failed = true;
            }

            if (IntervalMs > 0 && i < count - 1 && scripted.Count == 0)
            {
                TapThread.Sleep(IntervalMs);
            }
        }

        if (PublishSummaries)
        {
            PublishSeriesComplianceStep.Publish(Results, values, LimitLow, LimitHigh, IntervalMs);
        }

        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Error);
            return;
        }

        UpgradeVerdict(failed ? Verdict.Fail : Verdict.Pass);
    }
}

/// Publishes series.inband.pct / series.excursion.max / series.outband.ms for a scripted series.
[Display(
    "Publish Series Compliance",
    Groups: ["HardwareTest", "Analyze"],
    Description: "Publish in-band percent, max excursion, and out-of-band dwell for scripted values.")]
public sealed class PublishSeriesComplianceStep : RuntimeAwareTestStep
{
    [Display("Values", Order: 1, Description: "Comma-separated voltages.")]
    public string Values { get; set; } = string.Empty;

    [Display("Limit low", Order: 2)]
    public double? LimitLow { get; set; }

    [Display("Limit high", Order: 3)]
    public double? LimitHigh { get; set; }

    [Display("Interval ms", Order: 4)]
    public int IntervalMs { get; set; } = 5;

    [Display("Fail when out of band", Order: 5)]
    public bool FailWhenOutOfBand { get; set; } = true;

    public override void Run()
    {
        WaitIfPaused();
        var values = SeriesComplianceModes.ParseScriptedValues(Values);
        Publish(Results, values, LimitLow, LimitHigh, IntervalMs);
        var inBand = Math.Abs(SeriesComplianceModes.InBandPercent(values, LimitLow, LimitHigh) - 100) < 1e-9;
        if (FailWhenOutOfBand && !inBand)
        {
            UpgradeVerdict(Verdict.Fail);
            return;
        }

        UpgradeVerdict(Verdict.Pass);
    }

    internal static void Publish(
        ResultSource results,
        IReadOnlyList<double> values,
        double? limitLow,
        double? limitHigh,
        int intervalMs)
    {
        var inBand = SeriesComplianceModes.InBandPercent(values, limitLow, limitHigh);
        var excursion = SeriesComplianceModes.MaxExcursion(values, limitLow, limitHigh);
        var outMs = SeriesComplianceModes.MaxOutOfBandMs(values, limitLow, limitHigh, intervalMs);
        results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow", "LimitHigh" },
            "series.inband.pct",
            inBand,
            "%",
            100.0,
            double.NaN);
        results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow", "LimitHigh" },
            "series.excursion.max",
            excursion,
            string.Empty,
            double.NaN,
            0.0);
        results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow", "LimitHigh" },
            "series.outband.ms",
            outMs,
            "ms",
            double.NaN,
            double.NaN);
    }
}
