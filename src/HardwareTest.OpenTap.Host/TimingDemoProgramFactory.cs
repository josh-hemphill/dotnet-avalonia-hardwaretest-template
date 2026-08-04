using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Band-first authoring demo: bump waveform (Focus) + derived timing/envelope scalars with limits (Band).
public static class TimingDemoProgramFactory
{
    public const string DisplayName = "Timing / Envelope Demo (Band-first)";
    public const string EmbeddedName = "embedded:timing-demo";
    public const string FixtureFileName = "timing-band.TapPlan";

    public static TestPlan Create()
    {
        OpenTapPluginSearch.SearchSerialized();

        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };

        var waveform = new AcquireVoltageStep
        {
            Name = "Simulate bump waveform",
            Instrument = instrument,
            SampleCount = 8,
            IntervalMs = 1,
            Channel = "bump.v",
        };
        OpenTapMixinAttach.AttachPresentation(
            waveform,
            "bump.v",
            PresentationDisplayRoles.Timeseries,
            "V");

        var rise = new PublishBandScalarStep
        {
            Name = "Bump rise time (5–15 ms)",
            MetricName = "bump.rise.ms",
            Value = 8,
            Unit = "ms",
            LimitLow = 5,
            LimitHigh = 15,
        };
        OpenTapMixinAttach.AttachPresentation(
            rise,
            "bump.rise.ms",
            PresentationDisplayRoles.Passband,
            "ms");

        var returnLow = new PublishBandScalarStep
        {
            Name = "Return low time (≤50 ms)",
            MetricName = "return.low.at.ms",
            Value = 42,
            Unit = "ms",
            LimitLow = null,
            LimitHigh = 50,
        };
        OpenTapMixinAttach.AttachPresentation(
            returnLow,
            "return.low.at.ms",
            PresentationDisplayRoles.Passband,
            "ms");

        var envelope = new PublishBandScalarStep
        {
            Name = "Envelope error (0–0.1 V)",
            MetricName = "envelope.error",
            Value = 0.02,
            Unit = "V",
            LimitLow = 0,
            LimitHigh = 0.1,
        };
        OpenTapMixinAttach.AttachPresentation(
            envelope,
            "envelope.error",
            PresentationDisplayRoles.Passband,
            "V");

        // Intentional out-of-band teaching sample: Band chrome lights up without failing the run.
        var overshoot = new PublishBandScalarStep
        {
            Name = "Peak overshoot (Band only)",
            MetricName = "bump.overshoot.v",
            Value = 0.18,
            Unit = "V",
            LimitLow = 0,
            LimitHigh = 0.1,
            FailWhenOutOfBand = false,
        };
        OpenTapMixinAttach.AttachPresentation(
            overshoot,
            "bump.overshoot.v",
            PresentationDisplayRoles.Passband,
            "V");

        var bump = new TestGroupStep { Name = "Bump waveform" };
        bump.ChildTestSteps.Add(waveform);

        var derived = new TestGroupStep { Name = "Derived timing checks" };
        derived.ChildTestSteps.Add(rise);
        derived.ChildTestSteps.Add(returnLow);
        derived.ChildTestSteps.Add(envelope);
        derived.ChildTestSteps.Add(overshoot);

        var safety = new TestGroupStep { Name = "Safety" };
        safety.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        var root = new TestGroupStep { Name = DisplayName };
        root.ChildTestSteps.Add(bump);
        root.ChildTestSteps.Add(derived);
        root.ChildTestSteps.Add(safety);

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(root);
        return plan;
    }

    public static void SaveBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        OpenTapPluginSearch.SearchSerialized();
        Create().Save(Path.Combine(directory, FixtureFileName));
    }
}
