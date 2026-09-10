using System.ComponentModel;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Presentation;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
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

    [Fact]
    public void ApplySample_without_elapsed_uses_timestamp_delta()
    {
        var live = new LivePresentationViewModel();
        var step = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "acq",
            Name = "Acquire",
            Path = "Suite/Acquire",
        });
        var t0 = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        live.ApplySample(
            new MeasurementSampleEvent("rail.x", 0, 3.3, t0, DisplayRole: "timeseries"),
            step.Path,
            null,
            step);
        live.ApplySample(
            new MeasurementSampleEvent("rail.x", 1, 3.31, t0.AddMilliseconds(250), DisplayRole: "timeseries"),
            step.Path,
            null,
            step);

        Assert.Equal(2, live.PlotYsLength);
        Assert.Equal(0, live.PlotXs[0], 6);
        Assert.Equal(0.25, live.PlotXs[1], 6);
    }

    [Fact]
    public void PlotOutOfBandSpans_raises_property_changed()
    {
        var live = new LivePresentationViewModel();
        var step = new HierarchyStepViewModel(new OpenTapStepNode
        {
            Id = "acq",
            Name = "Acquire",
            Path = "Suite/Acquire",
        });
        var notified = false;
        live.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LivePresentationViewModel.PlotOutOfBandSpans))
            {
                notified = true;
            }
        };

        live.ApplySample(
            new MeasurementSampleEvent(
                "rail.x",
                0,
                3.6,
                DateTimeOffset.UtcNow,
                DisplayRole: "timeseries",
                LimitLow: 3.2,
                LimitHigh: 3.5,
                ElapsedMs: 5),
            step.Path,
            null,
            step);

        Assert.True(notified);
        Assert.NotEmpty(live.PlotOutOfBandSpans);
    }

    [Fact]
    public void LatestEventAt_picks_greatest_elapsed_at_or_before_now()
    {
        var marks = new MeasurementEventMark[]
        {
            new("cfg", 20, "bit2", 4, "p"),
            new("cfg", 5, "bit0", 1, "p"),
            new("cfg", 10, "bit1", 2, "p"),
        };
        var latest = SeriesTimingChrome.LatestEventAt(marks, 15);
        Assert.Equal("bit1", latest?.Label);
    }

    [Fact]
    public void BuildFromStoredSamples_uses_time_axis_only_when_every_sample_has_elapsed()
    {
        var timed = PresentationRoleMap.BuildFromStoredSamples(
        [
            new StoredSample
            {
                MetricKey = "rail.x",
                DisplayRole = PresentationRoleMap.Timeseries,
                Value = 3.3,
                ElapsedMs = 0,
                Timestamp = DateTimeOffset.UtcNow,
            },
            new StoredSample
            {
                MetricKey = "rail.x",
                DisplayRole = PresentationRoleMap.Timeseries,
                Value = 3.6,
                ElapsedMs = 10,
                LimitLow = 3.2,
                LimitHigh = 3.5,
                Timestamp = DateTimeOffset.UtcNow,
            },
        ]);
        var timedTile = Assert.Single(timed);
        Assert.True(timedTile.UsesTimeAxis);
        Assert.Equal(0.010, timedTile.Xs[1], 6);
        timedTile.SetTimingChrome(
            [new MeasurementEventMark("cfg", 10, "bit1", 2, "p")],
            SeriesTimingChrome.OutOfBandSpans(
                timedTile.Xs,
                timedTile.Ys,
                timedTile.YsLength,
                timedTile.LimitLow,
                timedTile.LimitHigh));
        Assert.Single(timedTile.TimingMarks);
        Assert.NotEmpty(timedTile.OutOfBandSpans);

        var mixed = PresentationRoleMap.BuildFromStoredSamples(
        [
            new StoredSample
            {
                MetricKey = "legacy",
                DisplayRole = PresentationRoleMap.Timeseries,
                Value = 1,
                Timestamp = DateTimeOffset.UtcNow,
            },
            new StoredSample
            {
                MetricKey = "legacy",
                DisplayRole = PresentationRoleMap.Timeseries,
                Value = 2,
                ElapsedMs = 50,
                Timestamp = DateTimeOffset.UtcNow,
            },
        ]);
        var indexTile = Assert.Single(mixed);
        Assert.False(indexTile.UsesTimeAxis);
        Assert.Equal(0, indexTile.Xs[0]);
        Assert.Equal(1, indexTile.Xs[1]);
        Assert.Equal(
            0.05,
            SeriesTimingChrome.StripDurationSec(
                [new MeasurementEventMark("cfg", 50, "bit1", 2, "p")],
                timeAxisEndSec: null),
            6);
        Assert.Equal(
            0.010,
            SeriesTimingChrome.StripDurationSec([], timedTile.Xs[timedTile.YsLength - 1]),
            6);
    }

    [Fact]
    public async Task Results_index_axis_uses_event_duration_not_sample_index()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "index-timing",
            PlanName = "Legacy",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    MetricKey = "legacy",
                    DisplayRole = PresentationRoleMap.Timeseries,
                    Value = 3.3,
                    LimitLow = 3.2,
                    LimitHigh = 3.5,
                    Timestamp = DateTimeOffset.UtcNow,
                },
                new StoredSample
                {
                    MetricKey = "legacy",
                    DisplayRole = PresentationRoleMap.Timeseries,
                    Value = 3.6,
                    ElapsedMs = 50,
                    LimitLow = 3.2,
                    LimitHigh = 3.5,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            Events = [new StoredEvent { Name = "cfg", Label = "bit1", ElapsedMs = 50 }],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        var chart = Assert.Single(vm.PresentationTiles, t => t.IsChart);
        Assert.False(chart.UsesTimeAxis);
        Assert.Empty(chart.OutOfBandSpans);
        Assert.Empty(vm.TimingSpans);
        Assert.Equal(0.05, vm.TimingDurationSec, 6);
        Assert.True(vm.HasTimingStrip);
        Assert.Single(vm.TimingEvents);
    }

    [Fact]
    public void SetTimingChrome_notifies_and_strip_only_tiles_are_not_metric_tiles()
    {
        var strip = new PresentationTileViewModel("win", PresentationTileKind.Timing, "timing", "ms", "p");
        var chart = new PresentationTileViewModel("rail.x", PresentationTileKind.Timeseries, "timeseries", "V", "p");
        var notified = false;
        ((INotifyPropertyChanged)chart).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresentationTileViewModel.TimingMarks))
            {
                notified = true;
            }
        };
        chart.SetTimingChrome([new MeasurementEventMark("cfg", 0, "bit0", 1, "p")], []);
        Assert.True(notified);
        Assert.True(strip.IsStrip);
        Assert.False(chart.IsStrip);
    }
}
