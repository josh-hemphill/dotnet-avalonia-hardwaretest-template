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
