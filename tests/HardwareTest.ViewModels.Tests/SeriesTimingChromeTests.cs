using HardwareTest.Features.Presentation;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class SeriesTimingChromeTests
{
    [Fact]
    public void Timing_maps_to_strip_and_unknown_stays_text()
    {
        Assert.Equal(PresentationTileKind.Timing, PresentationRoleMap.TryMapRole("timing"));
        Assert.Null(PresentationRoleMap.TryMapRole("not-a-role"));
        var strip = new PresentationTileViewModel("win", PresentationTileKind.Timing, "timing", "ms", "p");
        Assert.True(strip.IsStrip);
        Assert.False(strip.IsGauge);
        Assert.False(strip.IsChart);
    }

    [Fact]
    public void OutOfBandSpans_merges_contiguous_points()
    {
        double[] xs = [0, 0.005, 0.010, 0.015];
        double[] ys = [3.3, 3.6, 3.61, 3.3];
        var spans = SeriesTimingChrome.OutOfBandSpans(xs, ys, 4, 3.2, 3.5);
        var span = Assert.Single(spans);
        Assert.Equal(0.005, span.T0);
        Assert.Equal(0.010, span.T1);
    }

    [Fact]
    public void ApplySample_prefers_plan_elapsed_ms()
    {
        var live = new LivePresentationViewModel();
        var step = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "acq",
            Name = "Acquire",
            Path = "Suite/Acquire",
        });
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(
            new MeasurementSampleEvent("rail.x", 0, 3.3, t0, DisplayRole: "timeseries", ElapsedMs: 12.5),
            step.Path,
            null,
            step);
        live.ApplySample(
            new MeasurementSampleEvent("rail.x", 1, 3.6, t0.AddSeconds(10), DisplayRole: "timeseries", LimitLow: 3.2, LimitHigh: 3.5, ElapsedMs: 17.5),
            step.Path,
            null,
            step);

        Assert.Equal(2, live.PlotYsLength);
        Assert.Equal(0.0125, live.PlotXs[0], 6);
        Assert.Equal(0.0175, live.PlotXs[1], 6);
        Assert.Contains("17.5 ms", live.ChartElapsedText, StringComparison.Ordinal);
        Assert.True(live.HasChartAttention);
        Assert.NotEmpty(live.PlotOutOfBandSpans);
    }

    [Fact]
    public void ApplyEvent_sets_toolbar_label_and_strip()
    {
        var live = new LivePresentationViewModel();
        live.ApplyEvent(new MeasurementEventMark("cfg", 10, "bit2", 4, "Suite/Acquire"));
        Assert.True(live.HasTimingStrip);
        Assert.Equal("cfg:bit2", live.ChartEventLabel);
        Assert.Equal("10 ms", live.ChartElapsedText);
        Assert.Single(live.Events);
    }

    [Fact]
    public void Focus_stays_earned_when_events_arrive()
    {
        var live = new LivePresentationViewModel();
        var step = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "acq",
            Name = "Acquire",
            Path = "Suite/Acquire",
        });
        live.ApplySample(
            new MeasurementSampleEvent("rail.x", 0, 3.3, DateTimeOffset.UtcNow, DisplayRole: "timeseries", ElapsedMs: 0),
            step.Path,
            null,
            step);
        live.ApplyEvent(new MeasurementEventMark("cfg", 0, "bit0", 1, step.Path));
        live.RefreshChrome(step);
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);
        Assert.True(live.HasChartData);
        Assert.True(live.OfferOpenChart);
    }
}
