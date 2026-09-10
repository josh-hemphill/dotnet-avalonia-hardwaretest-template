using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Mixins;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class OpenTapPresentationTests
{
    [Fact]
    public void ApplySample_copies_limits_and_elapsed()
    {
        var sample = new StoredSample();
        OpenTapPresentation.ApplySample(
            sample,
            "rail.x",
            new OpenTapPresentation.MixinHints("rail.x", PresentationDisplayRoles.Timeseries, "V"),
            limitLow: 3.2,
            limitHigh: 3.4,
            elapsedMs: 12.5);

        Assert.Equal("rail.x", sample.MetricKey);
        Assert.Equal(3.2, sample.LimitLow);
        Assert.Equal(3.4, sample.LimitHigh);
        Assert.Equal(12.5, sample.ElapsedMs);
        Assert.Equal(PresentationDisplayRoles.Timeseries, sample.DisplayRole);
        Assert.Equal("V", sample.Unit);
    }

    [Fact]
    public void FromStored_round_trips_elapsed()
    {
        var stored = new StoredSample
        {
            Channel = "rail.x",
            MetricKey = "rail.x",
            Value = 1,
            LimitLow = 0,
            LimitHigh = 2,
            ElapsedMs = 8,
        };
        var live = MeasurementSampleEvent.FromStored(stored, index: 3);
        Assert.Equal(8, live.ElapsedMs);
        Assert.Equal(0, live.LimitLow);
        Assert.Equal(2, live.LimitHigh);
        Assert.Equal(3, live.Index);
    }
}
