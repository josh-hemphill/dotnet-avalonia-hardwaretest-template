using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class SeriesComplianceHostTests
{
    [Fact]
    public async Task Envelope_sweep_demo_publishes_events_and_passes_when_fail_flag_off()
    {
        var session = new OpenTapSession();
        await session.LoadEnvelopeSweepDemoProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-ENV-SWEEP", Family: "demo"));

        var summary = await session.RunAsync();
        Assert.Equal(RunResult.Passed, summary.Result);
        Assert.Equal(4, summary.Events.Count);
        Assert.Equal(["bit0", "bit1", "bit2", "bit3"], summary.Events.Select(e => e.Label).ToList());
        Assert.All(summary.Events, e =>
        {
            Assert.Equal("cfg", e.Name);
            Assert.True(e.Value is >= 1);
        });

        var samples = summary.Samples.Where(s =>
            string.Equals(s.MetricKey, "rail.x", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(4, samples.Count);
        Assert.Contains(samples, s => s.ElapsedMs is 10 && s.Value is { } v && Math.Abs(v - 3.60) < 1e-9);
        Assert.All(samples, s =>
        {
            Assert.Equal(3.2, s.LimitLow);
            Assert.Equal(3.5, s.LimitHigh);
            Assert.NotNull(s.ElapsedMs);
        });

        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, "series.inband.pct", StringComparison.OrdinalIgnoreCase)
            && s.Value is { } pct
            && Math.Abs(pct - 75) < 1e-9
            && s.LimitLow is { } lo
            && Math.Abs(lo - 100) < 1e-9);
        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, "series.excursion.max", StringComparison.OrdinalIgnoreCase)
            && s.Value is { } exc
            && Math.Abs(exc - 0.10) < 1e-9);
        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, "series.outband.ms", StringComparison.OrdinalIgnoreCase)
            && s.Value is { } ms
            && Math.Abs(ms - 5) < 1e-9);
    }

    [Fact]
    public async Task AllSamples_fails_when_flag_on_and_keeps_publishing()
    {
        var summary = await RunIsolatedBitSweepAsync(
            SeriesComplianceModes.AllSamples,
            failWhenOutOfBand: true,
            scripted: "3.30,3.32,3.60,3.31",
            dwellLimitMs: null);

        Assert.Equal(RunResult.Failed, summary.Result);
        Assert.Equal(4, summary.Events.Count);
        Assert.Equal(4, summary.Samples.Count(s =>
            string.Equals(s.MetricKey, "rail.x", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, "series.inband.pct", StringComparison.OrdinalIgnoreCase)
            && s.Value is { } pct
            && Math.Abs(pct - 75) < 1e-9);
    }

    [Fact]
    public async Task Dwell_fails_after_contiguous_out_of_band_exceeds_limit()
    {
        var summary = await RunIsolatedBitSweepAsync(
            SeriesComplianceModes.Dwell,
            failWhenOutOfBand: true,
            scripted: "3.60,3.61,3.30",
            dwellLimitMs: 5,
            bitCount: 3);

        Assert.Equal(RunResult.Failed, summary.Result);
        Assert.Equal(3, summary.Events.Count);
        Assert.Contains(summary.Samples, s =>
            string.Equals(s.MetricKey, "series.outband.ms", StringComparison.OrdinalIgnoreCase)
            && s.Value is { } ms
            && Math.Abs(ms - 10) < 1e-9);
    }

    [Fact]
    public async Task FailWhenOutOfBand_false_stays_pass_with_excursion()
    {
        var summary = await RunIsolatedBitSweepAsync(
            SeriesComplianceModes.AllSamples,
            failWhenOutOfBand: false,
            scripted: "3.30,3.32,3.60,3.31",
            dwellLimitMs: null);

        Assert.Equal(RunResult.Passed, summary.Result);
        Assert.Equal(4, summary.Events.Count);
    }

    [Fact]
    public async Task PublishSeriesComplianceStep_fails_on_scripted_out_of_band()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-series-pub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            OpenTapPluginSearch.SearchSerialized();
            var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
            var step = new PublishSeriesComplianceStep
            {
                Name = "Series summaries",
                Values = "3.30,3.60",
                LimitLow = 3.2,
                LimitHigh = 3.5,
                IntervalMs = 5,
                FailWhenOutOfBand = true,
            };
            var plan = new TestPlan();
            plan.ChildTestSteps.Add(step);
            plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });
            var path = Path.Combine(dir, "series-pub.TapPlan");
            plan.Save(path);

            var session = new OpenTapSession();
            await session.LoadPlanAsync(path);
            await session.ApplyStationAndDutAsync(
                new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
                new DutIdentity("DUT-SERIES-PUB", Family: "demo"));

            var summary = await session.RunAsync();
            Assert.Equal(RunResult.Failed, summary.Result);
            Assert.Contains(summary.Samples, s =>
                string.Equals(s.MetricKey, "series.inband.pct", StringComparison.OrdinalIgnoreCase)
                && s.Value is { } pct
                && Math.Abs(pct - 50) < 1e-9);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // temp cleanup
            }
        }
    }

    private static async Task<OpenTapRunSummary> RunIsolatedBitSweepAsync(
        string compliance,
        bool failWhenOutOfBand,
        string scripted,
        double? dwellLimitMs,
        int bitCount = 4)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-bitsweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            OpenTapPluginSearch.SearchSerialized();
            var instrument = new MockDmmInstrument { Name = "DMM", ResourceName = "MOCK::INSTR0" };
            var step = new BitSweepAcquireStep
            {
                Name = "Bit walk",
                Instrument = instrument,
                Channel = "rail.x",
                BitCount = bitCount,
                IntervalMs = 5,
                LimitLow = 3.2,
                LimitHigh = 3.5,
                SeriesCompliance = compliance,
                DwellLimitMs = dwellLimitMs,
                FailWhenOutOfBand = failWhenOutOfBand,
                ScriptedValues = scripted,
                PublishSummaries = true,
            };
            var plan = new TestPlan();
            plan.ChildTestSteps.Add(step);
            plan.ChildTestSteps.Add(new SafeShutdownStep { Name = "Safe Shutdown", Instrument = instrument });
            var path = Path.Combine(dir, "bitsweep.TapPlan");
            plan.Save(path);

            var session = new OpenTapSession();
            await session.LoadPlanAsync(path);
            await session.ApplyStationAndDutAsync(
                new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
                new DutIdentity("DUT-BITSWEEP", Family: "demo"));
            return await session.RunAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // temp cleanup
            }
        }
    }
}
