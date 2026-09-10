using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Mock bit walk with series limits + Event marks (Phase M Area 2).
public static class EnvelopeSweepDemoProgramFactory
{
    public const string DisplayName = "Envelope Sweep Demo (Bit walk)";
    public const string EmbeddedName = "embedded:envelope-sweep-demo";
    public const string FixtureFileName = "envelope-sweep.TapPlan";

    public static TestPlan Create()
    {
        OpenTapPluginSearch.SearchSerialized();

        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var sweep = new BitSweepAcquireStep
        {
            Name = "Bit walk Vout",
            Instrument = instrument,
            Channel = "rail.x",
            BitCount = 4,
            IntervalMs = 5,
            LimitLow = 3.2,
            LimitHigh = 3.5,
            SeriesCompliance = SeriesComplianceModes.AllSamples,
            FailWhenOutOfBand = false,
            ScriptedValues = "3.30,3.32,3.60,3.31",
            PublishSummaries = true,
        };
        OpenTapMixinAttach.AttachPresentation(sweep, "rail.x", PresentationDisplayRoles.Timeseries, "V");

        var inBand = new PublishBandScalarStep
        {
            Name = "In-band percent",
            MetricName = "series.inband.pct",
            Value = 75,
            Unit = "%",
            LimitLow = 100,
            FailWhenOutOfBand = false,
        };
        OpenTapMixinAttach.AttachPresentation(inBand, "series.inband.pct", PresentationDisplayRoles.Passband, "%");

        var measure = new TestGroupStep { Name = "Bit walk" };
        measure.ChildTestSteps.Add(sweep);
        measure.ChildTestSteps.Add(inBand);

        var safety = new TestGroupStep { Name = "Safety" };
        safety.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        var root = new TestGroupStep { Name = DisplayName };
        root.ChildTestSteps.Add(measure);
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
