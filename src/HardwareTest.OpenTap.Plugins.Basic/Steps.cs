using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

[Display("Identity Check", Groups: ["HardwareTest", "Identity"], Description: "Query instrument *IDN?.")]
[AllowAnyChild]
public sealed class IdentityCheckStep : TestStep
{
    [Display("Instrument")]
    public MockDmmInstrument Instrument { get; set; } = null!;

    [Display("DUT")]
    public HardwareDut Dut { get; set; } = null!;

    public override void Run()
    {
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Error);
            Log.Error("No instrument assigned.");
            return;
        }

        var idn = Instrument.QueryIdn();
        Log.Info("IDN={0}; DUT={1}", idn, Dut?.SerialNumber ?? "(none)");
        Results.Publish("Identity", new List<string> { "Idn", "DutSerial" }, idn, Dut?.SerialNumber ?? string.Empty);
        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Acquire Voltage", Groups: ["HardwareTest", "Measure"], Description: "Acquire mock VDC samples.")]
public sealed class AcquireVoltageStep : TestStep
{
    [Display("Instrument")]
    public MockDmmInstrument Instrument { get; set; } = null!;

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
            StepRuntime.WaitIfPaused?.Invoke();
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
public sealed class MeanGteStep : TestStep
{
    [Display("Instrument")]
    public MockDmmInstrument Instrument { get; set; } = null!;

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
            StepRuntime.WaitIfPaused?.Invoke();
            values.Add(Instrument.ReadVoltage());
        }

        var mean = values.Average();
        Results.Publish("Analyze", new List<string> { "Mean", "Threshold" }, mean, Threshold);
        Results.Publish("Scalar", new List<string> { "Name", "Value", "Unit" }, "Mean", mean, string.Empty);
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
public sealed class SafeShutdownStep : TestStep
{
    [Display("Instrument")]
    public MockDmmInstrument Instrument { get; set; } = null!;

    public override void Run()
    {
        StepRuntime.WaitIfPaused?.Invoke();
        if (Instrument is null)
        {
            UpgradeVerdict(Verdict.Pass);
            return;
        }

        Instrument.OutputOff();
        Instrument.Reset();
        Log.Info("Safe shutdown complete for {0}", Instrument.ResourceName);
        UpgradeVerdict(Verdict.Pass);
    }
}

/// Pauses execution until the operator confirms a hardware change / fixture step.
[Display("Operator Prompt", Groups: ["HardwareTest", "Operator"], Description: "Pause for hardware change; resume from UI Continue.")]
public sealed class OperatorPromptStep : TestStep
{
    [Display("Message", Description: "Shown to the technician while waiting.")]
    public string Message { get; set; } = "Complete the hardware change, then Continue.";

    public override void Run()
    {
        TapThread.ThrowIfAborted();
        Log.Info("Operator prompt: {0}", Message);
        Results.Publish("OperatorPrompt", new List<string> { "Message" }, Message);
        StepRuntime.RequestOperatorAttention(Message);
        StepRuntime.WaitIfPaused?.Invoke();
        UpgradeVerdict(Verdict.Pass);
    }
}

/// Pauses for Avalonia-owned typed input (no floating OpenTAP dialogs).
[Display("Operator Input", Groups: ["HardwareTest", "Operator"], Description: "Collect string/number input from the Run board, then Continue.")]
public sealed class OperatorInputStep : TestStep
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
        var response = StepRuntime.RequestInteraction?.Invoke(request)
                       ?? OperatorInteractionResponse.Cancel(request.Id);
        StepRuntime.WaitIfPaused?.Invoke();

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

/// Parent grouping step for hierarchy (subsystem / domain).
[Display("Test Group", Groups: ["HardwareTest"], Description: "Hierarchical group of child steps.")]
[AllowAnyChild]
public sealed class TestGroupStep : TestStep
{
    public override void Run()
    {
        StepRuntime.WaitIfPaused?.Invoke();
        RunChildSteps();
        UpgradeVerdict(Verdict.Pass);
    }
}

/// Simple repeat loop for Phase G iteration chrome (and offline demos without BasicSteps).
[Display("Repeat Loop", Groups: ["HardwareTest", "Flow"], Description: "Run child steps Count times.")]
[AllowAnyChild]
public sealed class RepeatLoopStep : TestStep
{
    [Display("Count", Order: 1)]
    public int Count { get; set; } = 3;

    public override void Run()
    {
        var n = Math.Max(1, Count);
        for (var i = 0; i < n; i++)
        {
            TapThread.ThrowIfAborted();
            StepRuntime.WaitIfPaused?.Invoke();
            RunChildSteps();
        }

        UpgradeVerdict(Verdict.Pass);
    }
}
