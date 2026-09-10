using HardwareTest.Core.Runs;
using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Time;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class StationHealthHostTests
{
    [Fact]
    public async Task Station_health_demo_publishes_cal_scalars_and_writes_store()
    {
        var session = new OpenTapSession();
        await session.LoadStationHealthDemoProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("", Family: "demo"));

        var summary = await session.RunAsync();
        Assert.Equal(RunResult.Passed, summary.Result);

        var offset = Assert.Single(summary.Samples, s =>
            string.Equals(s.MetricKey, StationHealthRecorder.OffsetMetric, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0.002, offset.Value);
        Assert.Equal(-0.01, offset.LimitLow);
        Assert.Equal(0.01, offset.LimitHigh);
        Assert.Equal(SampleResultSources.Measured, offset.ResultSource);

        var age = Assert.Single(summary.Samples, s =>
            string.Equals(s.MetricKey, StationHealthRecorder.AgeMetric, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, age.Value);
        Assert.Equal(24, age.LimitHigh);
        Assert.Equal(SampleResultSources.Measured, age.ResultSource);

        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero));
        await StationHealthRecorder.TryWriteAsync(
            store,
            clock,
            ProgramKinds.StationHealth,
            FileStationHealthStore.DefaultProfileId,
            StationHealthDemoProgramFactory.CatalogId,
            summary.RunId,
            summary.Result,
            summary.Samples);

        var record = store.TryRead("default");
        Assert.NotNull(record);
        Assert.Equal(StationHealthVerdicts.Pass, record!.Verdict);
        Assert.Equal(StationHealthSources.Queried, record.Source);
        Assert.Equal(clock.UtcNow, record.MeasuredAt);
        Assert.Equal(2, record.Metrics.Count);
    }

    [Fact]
    public async Task Isolated_health_step_fails_when_offset_out_of_band()
    {
        var summary = await RunIsolatedAsync(offset: 0.05, ageHours: 0, failWhenOutOfBand: true);
        Assert.Equal(RunResult.Failed, summary.Result);
        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, StationHealthRecorder.OffsetMetric, StringComparison.OrdinalIgnoreCase)
            && s.Value is { } v
            && Math.Abs(v - 0.05) < 1e-9);
    }

    [Fact]
    public async Task Fail_when_out_of_band_false_still_passes()
    {
        var summary = await RunIsolatedAsync(offset: 0.05, ageHours: 40, failWhenOutOfBand: false);
        Assert.Equal(RunResult.Passed, summary.Result);
    }

    private static async Task<OpenTapRunSummary> RunIsolatedAsync(
        double offset,
        double ageHours,
        bool failWhenOutOfBand)
    {
        OpenTapPluginSearch.SearchSerialized();
        var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new ReportStationHealthStep
        {
            Name = "Report station cal",
            OffsetVolts = offset,
            OffsetLimitLow = -0.01,
            OffsetLimitHigh = 0.01,
            AgeHours = ageHours,
            MaxAgeHours = 24,
            FailWhenOutOfBand = failWhenOutOfBand,
        });
        plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });

        using var temp = new TempDataDirectory();
        var planPath = Path.Combine(temp.Path, "isolated-health.TapPlan");
        plan.Save(planPath);

        var session = new OpenTapSession();
        await session.LoadPlanAsync(planPath);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("", Family: "demo"));
        return await session.RunAsync();
    }
}
