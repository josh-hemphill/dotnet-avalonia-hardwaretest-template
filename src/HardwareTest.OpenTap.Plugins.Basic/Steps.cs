using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

[Display("Identity Check", Groups: ["HardwareTest", "Identity"], Description: "Query instrument *IDN?.")]
[AllowAnyChild]
public sealed class IdentityCheckStep : TestStep
{
    [Display("Instrument")]
    public HardwareDmm Instrument { get; set; } = null!;

    [Display("DUT")]
    public HardwareDut Dut { get; set; } = null!;

    public override void Run()
    {
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Error);
            Log.Error("No IDmmInstrument assigned.");
            return;
        }

        var idn = Instrument.QueryIdn();
        Log.Info("IDN={0}; DUT={1}", idn, Dut?.SerialNumber ?? "(none)");
        Results.Publish("Identity", new List<string> { "Idn", "DutSerial" }, idn, Dut?.SerialNumber ?? string.Empty);
        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Acquire Voltage", Groups: ["HardwareTest", "Measure"], Description: "Acquire VDC samples from an IDmmInstrument.")]
public sealed class AcquireVoltageStep : RuntimeAwareTestStep
{
    [Display("Instrument")]
    public HardwareDmm Instrument { get; set; } = null!;

    [Display("Sample Count")]
    public int SampleCount { get; set; } = 32;

    [Display("Interval Ms")]
    public int IntervalMs { get; set; } = 5;

    [Display("Channel")]
    public string Channel { get; set; } = "VDC";

    public override void Run()
    {
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Error);
            return;
        }

        Instrument.ConfigureDcVolts();
        var values = new List<double>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            TapThread.ThrowIfAborted();
            WaitIfPaused();
            var v = Instrument.ReadVoltage();
            values.Add(v);
            Results.Publish("Sample", new List<string> { "Channel", "Index", "Value" }, Channel, i, v);
            if (IntervalMs > 0 && i < SampleCount - 1)
            {
                TapThread.Sleep(IntervalMs);
            }
        }

        StepRun.Parameters["SampleCount"] = values.Count.ToString();
        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Mean GTE", Groups: ["HardwareTest", "Analyze"], Description: "Pass if mean of last published samples meets threshold.")]
public sealed class MeanGteStep : RuntimeAwareTestStep
{
    [Display("Instrument")]
    public HardwareDmm Instrument { get; set; } = null!;

    [Display("Sample Count")]
    public int SampleCount { get; set; } = 8;

    [Display("Threshold")]
    public double Threshold { get; set; }

    public override void Run()
    {
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Error);
            return;
        }

        var values = new List<double>(SampleCount);
        for (var i = 0; i < SampleCount; i++)
        {
            TapThread.ThrowIfAborted();
            WaitIfPaused();
            values.Add(Instrument.ReadVoltage());
        }

        var mean = values.Average();
        Results.Publish("Analyze", new List<string> { "Mean", "Threshold" }, mean, Threshold);
        Results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow" },
            "Mean",
            mean,
            string.Empty,
            Threshold);
        if (mean >= Threshold)
        {
            UpgradeVerdict(Verdict.Pass);
        }
        else
        {
            Log.Error("mean={0} < threshold={1}", mean, Threshold);
            UpgradeVerdict(Verdict.Fail);
        }
    }
}

[Display("Safe Shutdown", Groups: ["HardwareTest", "Safety"], Description: "Return instrument to a safe idle.")]
public sealed class SafeShutdownStep : RuntimeAwareTestStep
{
    [Display("Instrument")]
    public HardwareDmm Instrument { get; set; } = null!;

    public override void Run()
    {
        WaitIfPaused();
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Pass);
            return;
        }

        Instrument.OutputOff();
        Instrument.Reset();
        Log.Info("Safe shutdown complete for {0}", DmmStepAccess.InstrumentResourceLabel(Instrument));
        UpgradeVerdict(Verdict.Pass);
    }
}

internal static class DmmStepAccess
{
    public static string InstrumentResourceLabel(HardwareDmm instrument)
    {
        if (instrument is MockDmmInstrument mock && !string.IsNullOrWhiteSpace(mock.ResourceName))
        {
            return mock.ResourceName;
        }

        if (instrument is VisaDmmInstrument visa && !string.IsNullOrWhiteSpace(visa.VisaAddress))
        {
            return visa.VisaAddress;
        }

        return string.IsNullOrWhiteSpace(instrument.Name) ? instrument.GetType().Name : instrument.Name;
    }
}

/// Pauses execution until the operator confirms a hardware change / fixture step.
[Display("Operator Prompt", Groups: ["HardwareTest", "Operator"], Description: "Pause for hardware change; resume from UI Continue.")]
public sealed class OperatorPromptStep : RuntimeAwareTestStep
{
    [Display("Message", Description: "Shown to the technician while waiting.")]
    public string Message { get; set; } = "Complete the hardware change, then Continue.";

    public override void Run()
    {
        TapThread.ThrowIfAborted();
        Log.Info("Operator prompt: {0}", Message);
        RequestOperatorAttention(Message);
        WaitIfPaused();
        Results.Publish("OperatorPrompt", new List<string> { "Message" }, Message);
        UpgradeVerdict(Verdict.Pass);
    }
}

/// Pauses for Avalonia-owned typed input (no floating OpenTAP dialogs).
[Display("Operator Input", Groups: ["HardwareTest", "Operator"], Description: "Collect string/number input from the Run board, then Continue.")]
public sealed class OperatorInputStep : RuntimeAwareTestStep
{
    [Display("Title", Order: 1)]
    public string Title { get; set; } = "Operator input";

    [Display("Message", Order: 2)]
    public string Message { get; set; } = "Enter the requested value(s), then Continue.";

    [Display("String Field Id", Order: 10)]
    public string StringFieldId { get; set; } = "value";

    [Display("String Field Label", Order: 11)]
    public string StringFieldLabel { get; set; } = "Value";

    [Display("String Field Required", Order: 12)]
    public bool StringFieldRequired { get; set; }

    [Display("String Default", Order: 13)]
    public string StringFieldDefault { get; set; } = string.Empty;

    [Display("Number Field Id", Order: 20, Description: "Leave empty to omit the number field.")]
    public string NumberFieldId { get; set; } = string.Empty;

    [Display("Number Field Label", Order: 21)]
    public string NumberFieldLabel { get; set; } = "Number";

    [Display("Number Field Required", Order: 22)]
    public bool NumberFieldRequired { get; set; }

    public override void Run()
    {
        TapThread.ThrowIfAborted();
        var fields = new List<OperatorInteractionField>
        {
            new()
            {
                Id = string.IsNullOrWhiteSpace(StringFieldId) ? "value" : StringFieldId.Trim(),
                Label = string.IsNullOrWhiteSpace(StringFieldLabel) ? "Value" : StringFieldLabel.Trim(),
                Kind = OperatorInteractionFieldKind.String,
                Required = StringFieldRequired,
                DefaultValue = StringFieldDefault,
            },
        };
        if (!string.IsNullOrWhiteSpace(NumberFieldId))
        {
            fields.Add(new OperatorInteractionField
            {
                Id = NumberFieldId.Trim(),
                Label = string.IsNullOrWhiteSpace(NumberFieldLabel) ? "Number" : NumberFieldLabel.Trim(),
                Kind = OperatorInteractionFieldKind.Number,
                Required = NumberFieldRequired,
            });
        }

        var request = new OperatorInteractionRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(Title) ? "Operator input" : Title.Trim(),
            Message = Message,
            Fields = fields,
        };

        Log.Info("Operator input: {0}", request.Message);
        var response = RequestInteraction(request);
        WaitIfPaused();

        if (response.Cancelled)
        {
            Log.Warning("Operator input cancelled.");
            UpgradeVerdict(Verdict.Error);
            return;
        }

        var stringId = fields[0].Id;
        response.Values.TryGetValue(stringId, out var stringValue);
        stringValue ??= string.Empty;
        var numberValue = string.Empty;
        if (fields.Count > 1)
        {
            response.Values.TryGetValue(fields[1].Id, out numberValue);
            numberValue ??= string.Empty;
        }

        Results.Publish(
            "OperatorInput",
            new List<string> { "StringId", "StringValue", "NumberId", "NumberValue" },
            stringId,
            stringValue,
            fields.Count > 1 ? fields[1].Id : string.Empty,
            numberValue);
        StepRun.Parameters["OperatorInput." + stringId] = stringValue;
        if (fields.Count > 1)
        {
            StepRun.Parameters["OperatorInput." + fields[1].Id] = numberValue;
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

/// Publishes one Scalar row with optional LimitLow/LimitHigh for band-first Presentation authoring.
[Display(
    "Publish Band Scalar",
    Groups: ["HardwareTest", "Analyze"],
    Description: "Publish a derived Scalar metric with limits (bump timing, envelope, thresholds). Prefer this over waveforms for pass criteria.")]
public sealed class PublishBandScalarStep : RuntimeAwareTestStep
{
    [Display("Metric name", Order: 1, Description: "Scalar Name / ChannelKey (e.g. bump.rise.ms).")]
    public string MetricName { get; set; } = "metric";

    [Display("Value", Order: 2)]
    public double Value { get; set; }

    [Display("Unit", Order: 3)]
    public string Unit { get; set; } = string.Empty;

    [Display("Limit low", Order: 4, Description: "Optional lower bound (passband / GTE).")]
    public double? LimitLow { get; set; }

    [Display("Limit high", Order: 5, Description: "Optional upper bound (passband / LTE).")]
    public double? LimitHigh { get; set; }

    [Display("Fail when out of band", Order: 6)]
    public bool FailWhenOutOfBand { get; set; } = true;

    public override void Run()
    {
        WaitIfPaused();
        Results.Publish(
            "Scalar",
            new List<string> { "Name", "Value", "Unit", "LimitLow", "LimitHigh" },
            MetricName,
            Value,
            Unit ?? string.Empty,
            LimitLow ?? double.NaN,
            LimitHigh ?? double.NaN);

        if (!FailWhenOutOfBand)
        {
            UpgradeVerdict(Verdict.Pass);
            return;
        }

        if (LimitLow is { } lo && Value < lo)
        {
            Log.Error("{0}={1} < LimitLow={2}", MetricName, Value, lo);
            UpgradeVerdict(Verdict.Fail);
            return;
        }

        if (LimitHigh is { } hi && Value > hi)
        {
            Log.Error("{0}={1} > LimitHigh={2}", MetricName, Value, hi);
            UpgradeVerdict(Verdict.Fail);
            return;
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

/// Parent grouping step for hierarchy (subsystem / domain).
[Display("Test Group", Groups: ["HardwareTest"], Description: "Hierarchical group of child steps.")]
[AllowAnyChild]
public sealed class TestGroupStep : RuntimeAwareTestStep
{
    public override void Run()
    {
        WaitIfPaused();
        RunChildSteps();
        UpgradeVerdict(Verdict.Pass);
    }
}

/// Simple repeat loop for Phase G iteration chrome (and offline demos without BasicSteps).
[Display("Repeat Loop", Groups: ["HardwareTest", "Flow"], Description: "Run child steps Count times.")]
[AllowAnyChild]
public sealed class RepeatLoopStep : RuntimeAwareTestStep
{
    [Display("Count", Order: 1)]
    public int Count { get; set; } = 3;

    public override void Run()
    {
        var n = Math.Max(1, Count);
        for (var i = 0; i < n; i++)
        {
            TapThread.ThrowIfAborted();
            WaitIfPaused();
            RunChildSteps();
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

/// Test-only: ignores cooperative abort so the worker kill path can be proven.
/// Do not put this step in sample or operator plans.
[Display("Hang Forever", Groups: ["HardwareTest", "Test"], Description: "Ignores cancel; used to test worker kill.")]
public sealed class HangForeverStep : TestStep
{
    public override void Run()
    {
        while (true)
        {
            Thread.Sleep(200);
        }
    }
}
