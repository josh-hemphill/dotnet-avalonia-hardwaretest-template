using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Bench station-health demo: mock cal query, no DUT serial, status report only.
public static class StationHealthDemoProgramFactory
{
    public const string DisplayName = "Station Health (Demo)";
    public const string EmbeddedName = "embedded:station-health";
    public const string FixtureFileName = "station-health.TapPlan";
    public const string CatalogId = "station-health";

    public static TestPlan Create()
    {
        OpenTapPluginSearch.SearchSerialized();

        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var report = new ReportStationHealthStep
        {
            Name = "Report station cal",
            OffsetVolts = 0.002,
            OffsetLimitLow = -0.01,
            OffsetLimitHigh = 0.01,
            AgeHours = 0,
            MaxAgeHours = 24,
            ResultSource = HardwareTest.Core.Runs.SampleResultSources.Measured,
            FailWhenOutOfBand = true,
        };

        // One step publishes two ChannelKeys — do not attach a Presentation mixin
        // (a single ChannelKey would relabel both Scalars).
        var measure = new TestGroupStep { Name = "Station cal" };
        measure.ChildTestSteps.Add(report);

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
