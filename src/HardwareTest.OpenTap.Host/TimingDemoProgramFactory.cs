using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Band-first authoring demo: optional bump waveform + derived timing/envelope scalars with limits.
public static class TimingDemoProgramFactory
{
    public const string DisplayName = "Timing / Envelope Demo (Band-first)";
    public const string EmbeddedName = "embedded:timing-demo";
    public const string FixtureFileName = "timing-band.TapPlan";

    public static TestPlan Create()
    {
        OpenTapPluginSearch.EnsureCorePluginDirectories();

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
            Name = "Bump rise time",
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
            Name = "Return low time",
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
            Name = "Envelope error",
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

        var plan = new TestPlan();
        plan.ChildTestSteps.Add(waveform);
        plan.ChildTestSteps.Add(rise);
        plan.ChildTestSteps.Add(returnLow);
        plan.ChildTestSteps.Add(envelope);
        plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });
        return plan;
    }

    public static void SaveBeside(string directory)
    {
        Directory.CreateDirectory(directory);
        OpenTapPluginSearch.EnsureCorePluginDirectories();
        PluginManager.Search();
        Create().Save(Path.Combine(directory, FixtureFileName));
    }
}
