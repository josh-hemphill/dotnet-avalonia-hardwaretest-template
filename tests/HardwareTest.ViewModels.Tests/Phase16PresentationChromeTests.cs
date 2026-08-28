using HardwareTest.Features.Presentation;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Live chart chrome: no auto-open, per-metric buffers, out-of-band warns without stealing focus.
public sealed class Phase16PresentationChromeTests
{
    private static HierarchyStepViewModel Leaf(string name = "Acquire", string? path = null)
        => new(new OpenTapStepNode
        {
            Id = name,
            Name = name,
            Path = path ?? $"Suite/{name}",
        });

    private static MeasurementSampleEvent Timeseries(
        string metric,
        double value,
        DateTimeOffset? timestamp = null,
        double? low = null,
        double? high = null)
        => new(
            metric,
            0,
            value,
            timestamp ?? DateTimeOffset.UtcNow,
            DisplayRole: "timeseries",
            LimitLow: low,
            LimitHigh: high);

    [Fact]
    public void Timeseries_selection_does_not_auto_open_Focus_or_Chart()
    {
        var live = new LivePresentationViewModel();
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);
        Assert.False(live.HasChartData);

        var step = Leaf();
        live.ApplySample(Timeseries("VDC", 1.0), step.Path, null, selectedStep: null);

        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);
        Assert.False(live.ShowPlotForSelection);
        Assert.True(live.HasChartData);
        Assert.True(live.OfferOpenChart);

        live.RefreshChrome(step);
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);
        Assert.True(live.HasChartData);
    }

    [Fact]
    public void Out_of_band_sets_attention_without_promoting_Focus()
    {
        var live = new LivePresentationViewModel();
        var acquire = Leaf("Acquire", "Suite/Acquire");
        var mean = Leaf("Mean", "Suite/Mean");
        live.ApplySample(Timeseries("VDC", 1.0, low: 0.5, high: 2.0), acquire.Path, null, acquire);
        live.ApplySample(
            new MeasurementSampleEvent(
                "VDC.mean",
                0,
                0.5,
                DateTimeOffset.UtcNow,
                DisplayRole: "passband",
                LimitLow: 1.0,
                LimitHigh: 2.0),
            mean.Path,
            null,
            mean);

        Assert.Contains(live.PresentationTiles, t => t.IsOutOfBand);
        Assert.True(live.HasChartData);
        Assert.True(live.HasChartAttention);
        Assert.False(live.ShowFocusTrend);
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.Contains("Out of band", live.FocusTrendTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Distinct_metrics_do_not_share_a_buffer()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), step.Path, null, step);
        live.ApplySample(Timeseries("IDC", 0.2, t0.AddSeconds(1)), step.Path, null, step);

        Assert.Equal(2, live.AvailableSeries.Count);
        Assert.Contains(live.AvailableSeries, s => s.Key.MetricKey == "VDC");
        Assert.Contains(live.AvailableSeries, s => s.Key.MetricKey == "IDC");
    }

    [Fact]
    public void Caps_live_series_at_eight_and_keeps_buffers_when_chrome_refreshes()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        for (var i = 0; i < 9; i++)
        {
            live.ApplySample(Timeseries($"M{i}", i, t0.AddSeconds(i)), step.Path, null, step);
        }

        Assert.Equal(LivePresentationViewModel.MaximumLiveSeries, live.AvailableSeries.Count);
        Assert.DoesNotContain(live.AvailableSeries, s => s.Key.MetricKey == "M0");
        Assert.Contains(live.AvailableSeries, s => s.Key.MetricKey == "M8");

        live.RefreshChrome(step);
        Assert.True(live.HasChartData);
        Assert.Equal(LivePresentationViewModel.MaximumLiveSeries, live.AvailableSeries.Count);
    }

    [Fact]
    public void Time_window_snapshot_excludes_older_samples()
    {
        var key = new LiveSeriesKey("Suite/Acquire", "VDC");
        var buffer = new LiveSeriesBuffer(key);
        var t0 = DateTimeOffset.UtcNow;
        buffer.Append(1.0, t0, 0, 2, "V");
        buffer.Append(1.5, t0.AddSeconds(40), 0, 2, "V");

        var all = buffer.Snapshot(null);
        Assert.Equal(2, all.Length);
        var window = buffer.Snapshot(TimeSpan.FromSeconds(30));
        Assert.Equal(1, window.Length);
        Assert.Equal(1.5, window.Ys[0]);
    }

    [Fact]
    public void Timeseries_out_of_band_sets_attention()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        live.ApplySample(Timeseries("VDC", 9.9, low: 0, high: 1), step.Path, null, step);
        Assert.True(live.HasChartData);
        Assert.True(live.HasChartAttention);
        Assert.Contains("Out of band", live.ChartBandText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tile_IsOutOfBand_respects_limits()
    {
        var tile = new PresentationTileViewModel("m", PresentationTileKind.Passband, "passband", "V", "p");
        tile.Apply(1.5, 1.0, 2.0);
        Assert.False(tile.IsOutOfBand);
        tile.Apply(0.5, 1.0, 2.0);
        Assert.True(tile.IsOutOfBand);
        tile.Apply(2.5, 1.0, 2.0);
        Assert.True(tile.IsOutOfBand);
    }
}
