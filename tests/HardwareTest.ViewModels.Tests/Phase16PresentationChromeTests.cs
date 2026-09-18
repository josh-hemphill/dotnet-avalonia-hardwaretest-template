using System.Reactive.Linq;
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
    public void Additional_samples_do_not_lock_metric_to_the_combo_item()
    {
        var live = new LivePresentationViewModel();
        var acquire = Leaf("Acquire", "Suite/Acquire");
        var mean = Leaf("Mean", "Suite/Mean");
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), acquire.Path, null, acquire);
        live.ApplySample(Timeseries("VDC", 1.1, t0.AddSeconds(1)), acquire.Path, null, acquire);
        live.ApplySample(Timeseries("VDC", 1.2, t0.AddSeconds(2)), mean.Path, null, mean);

        Assert.Equal("Suite/Mean", live.SelectedSeries?.Key.StepPath);

        live.ApplySample(Timeseries("VDC", 1.3, t0.AddSeconds(3)), acquire.Path, null, mean);
        live.ApplySample(Timeseries("VDC", 1.4, t0.AddSeconds(4)), mean.Path, null, mean);
        Assert.Equal("Suite/Mean", live.SelectedSeries?.Key.StepPath);
        Assert.Equal(2, live.AvailableSeries.Count);
        Assert.Same(live.AvailableSeries.First(s => s.Key.StepPath == "Suite/Mean"), live.SelectedSeries);
    }

    [Fact]
    public void SelectSeries_locks_until_a_later_user_pick()
    {
        var live = new LivePresentationViewModel();
        var acquire = Leaf("Acquire", "Suite/Acquire");
        var mean = Leaf("Mean", "Suite/Mean");
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), acquire.Path, null, acquire);
        live.ApplySample(Timeseries("IDC", 0.2, t0.AddSeconds(1)), mean.Path, null, mean);
        var acquireItem = live.AvailableSeries.First(s => s.Key.StepPath == "Suite/Acquire");
        live.SelectSeriesCommand.Execute(acquireItem).Subscribe();

        Assert.Equal("Suite/Acquire", live.SelectedSeries?.Key.StepPath);

        live.ApplySample(Timeseries("VDC", 1.5, t0.AddSeconds(2)), mean.Path, null, mean);
        Assert.Equal("Suite/Acquire", live.SelectedSeries?.Key.StepPath);

        live.SelectedSeries = live.AvailableSeries.First(s => s.Key.StepPath == "Suite/Mean");
        live.ApplySample(Timeseries("VDC", 1.6, t0.AddSeconds(3)), acquire.Path, null, acquire);
        Assert.Equal("Suite/Mean", live.SelectedSeries?.Key.StepPath);
    }

    [Fact]
    public void Changing_time_window_republishes_the_snapshot()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), step.Path, null, step);
        live.ApplySample(Timeseries("VDC", 1.5, t0.AddSeconds(40)), step.Path, null, step);

        Assert.Equal(1, live.PlotYsLength);
        live.SelectedTimeWindow = ChartTimeWindow.All;
        Assert.Equal(2, live.PlotYsLength);
    }

    [Fact]
    public void PlaceCursor_snaps_to_nearest_sample_and_pauses_follow_live()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0, low: 0, high: 2), step.Path, null, step);
        live.ApplySample(Timeseries("VDC", 1.5, t0.AddSeconds(1), low: 0, high: 2), step.Path, null, step);
        live.SelectedTimeWindow = ChartTimeWindow.All;

        live.PlaceCursor(0.2);

        Assert.True(live.HasCursor);
        Assert.False(live.FollowLive);
        Assert.Equal(0.0, live.CursorX);
        Assert.Equal("Readout", live.ChartAgeText);
        Assert.Equal("1", live.ChartValueText);
        Assert.Equal(SeriesTimingChrome.FormatElapsed(0), live.ChartElapsedText);
        Assert.Contains("Within", live.ChartBandText, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceCursor_resnaps_when_sample_window_drops_the_point()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), step.Path, null, step);
        live.ApplySample(Timeseries("VDC", 1.5, t0.AddSeconds(40)), step.Path, null, step);
        live.SelectedTimeWindow = ChartTimeWindow.All;
        live.PlaceCursor(0.0);
        Assert.Equal(0.0, live.CursorX);
        Assert.DoesNotContain("1.5", live.ChartValueText, StringComparison.Ordinal);

        live.SelectedTimeWindow = ChartTimeWindow.ThirtySeconds;

        Assert.True(live.HasCursor);
        Assert.Equal(40.0, live.CursorX);
        Assert.Contains("1.5", live.ChartValueText, StringComparison.Ordinal);
        Assert.False(live.FollowLive);
    }

    [Fact]
    public void ResetForRun_drops_cursor_and_resumes_follow_live()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        live.ApplySample(Timeseries("VDC", 1.0), step.Path, null, step);
        live.PlaceCursor(0.0);
        Assert.True(live.HasCursor);
        Assert.False(live.FollowLive);

        live.ResetForRun();

        Assert.False(live.HasCursor);
        Assert.True(live.FollowLive);
        Assert.Equal(0, live.PlotYsLength);
    }

    [Fact]
    public void FollowLive_off_without_a_cursor_survives_a_publish()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        live.ApplySample(Timeseries("VDC", 1.0), step.Path, null, step);
        live.FollowLive = false;
        Assert.False(live.HasCursor);

        live.ApplySample(Timeseries("VDC", 1.5, DateTimeOffset.UtcNow.AddSeconds(1)), step.Path, null, step);

        Assert.False(live.HasCursor);
        Assert.False(live.FollowLive);
    }

    [Fact]
    public void ApplyEvent_does_not_replace_cursor_toolbar()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), step.Path, null, step);
        live.ApplySample(Timeseries("VDC", 1.5, t0.AddSeconds(1)), step.Path, null, step);
        live.SelectedTimeWindow = ChartTimeWindow.All;
        live.PlaceCursor(0.0);
        var value = live.ChartValueText;
        var elapsed = live.ChartElapsedText;
        var eventLabel = live.ChartEventLabel;

        live.ApplyEvent(new MeasurementEventMark("cfg", 1000, "bit2", 4, step.Path));

        Assert.Equal(value, live.ChartValueText);
        Assert.Equal(elapsed, live.ChartElapsedText);
        Assert.Equal(eventLabel, live.ChartEventLabel);
        Assert.Equal("Readout", live.ChartAgeText);
        Assert.NotEqual("cfg:bit2", live.ChartEventLabel);
        Assert.Single(live.Events);
    }

    [Fact]
    public void ClearCursor_restores_latest_sample_toolbar()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        var t0 = DateTimeOffset.UtcNow;
        live.ApplySample(Timeseries("VDC", 1.0, t0), step.Path, null, step);
        live.ApplySample(Timeseries("VDC", 2.0, t0.AddSeconds(1)), step.Path, null, step);
        live.SelectedTimeWindow = ChartTimeWindow.All;
        live.PlaceCursor(0.0);
        Assert.True(live.HasCursor);

        live.ClearCursor();

        Assert.False(live.HasCursor);
        Assert.True(live.FollowLive);
        Assert.Contains("2", live.ChartValueText, StringComparison.Ordinal);
        Assert.DoesNotContain("Readout", live.ChartAgeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetView_clears_cursor()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        live.ApplySample(Timeseries("VDC", 1.0), step.Path, null, step);
        live.PlaceCursor(0.0);
        Assert.True(live.HasCursor);

        await live.ResetViewCommand.ExecuteAsync();
        Assert.False(live.HasCursor);
        Assert.True(live.FollowLive);
    }

    [Fact]
    public async Task ResetView_restores_follow_live()
    {
        var live = new LivePresentationViewModel();
        live.FollowLive = false;
        await live.ResetViewCommand.ExecuteAsync();
        Assert.True(live.FollowLive);
    }

    [Fact]
    public void Sample_window_labels_describe_the_data_filter()
    {
        Assert.Equal("Last 30 sec", ChartTimeWindow.ThirtySeconds.Label);
        Assert.Equal("Last 2 min", ChartTimeWindow.TwoMinutes.Label);
        Assert.Equal("All samples", ChartTimeWindow.All.Label);
        Assert.Equal(
            new[] { ChartTimeWindow.ThirtySeconds, ChartTimeWindow.TwoMinutes, ChartTimeWindow.All },
            ChartTimeWindow.AllWindows);
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
