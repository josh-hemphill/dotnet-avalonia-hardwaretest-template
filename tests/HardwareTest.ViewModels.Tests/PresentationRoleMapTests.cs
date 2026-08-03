using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class PresentationRoleMapTests
{
    [Theory]
    [InlineData("timeseries", PresentationTileKind.Timeseries)]
    [InlineData("scalar", PresentationTileKind.Scalar)]
    [InlineData("passband", PresentationTileKind.Passband)]
    [InlineData("TIMESERIES", PresentationTileKind.Timeseries)]
    public void TryMapRole_maps_known_roles(string role, PresentationTileKind expected)
        => Assert.Equal(expected, PresentationRoleMap.TryMapRole(role));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gauge")]
    [InlineData("unknown")]
    public void TryMapRole_returns_null_for_unknown(string? role)
        => Assert.Null(PresentationRoleMap.TryMapRole(role));

    [Fact]
    public void IsTimeseriesPlotSample_true_for_timeseries_or_blank_role()
    {
        Assert.True(PresentationRoleMap.IsTimeseriesPlotSample(
            new MeasurementSampleEvent("VDC", 0, 1, DateTimeOffset.UtcNow, DisplayRole: "timeseries")));
        Assert.True(PresentationRoleMap.IsTimeseriesPlotSample(
            new MeasurementSampleEvent("VDC", 0, 1, DateTimeOffset.UtcNow)));
        Assert.False(PresentationRoleMap.IsTimeseriesPlotSample(
            new MeasurementSampleEvent("Mean", 0, 1, DateTimeOffset.UtcNow, DisplayRole: "scalar")));
        Assert.False(PresentationRoleMap.IsTimeseriesPlotSample(
            new MeasurementSampleEvent("Mean", 0, 1, DateTimeOffset.UtcNow, DisplayRole: "weird")));
    }

    [Fact]
    public void UpsertRunGauge_only_adds_scalar_and_passband()
    {
        var tiles = new List<PresentationTileViewModel>();
        Assert.Null(PresentationRoleMap.UpsertRunGauge(
            tiles,
            new MeasurementSampleEvent("VDC", 0, 1.2, DateTimeOffset.UtcNow, MetricKey: "VDC", DisplayRole: "timeseries"),
            "path/a"));
        Assert.Empty(tiles);

        var gauge = PresentationRoleMap.UpsertRunGauge(
            tiles,
            new MeasurementSampleEvent(
                "Mean",
                0,
                1.2,
                DateTimeOffset.UtcNow,
                MetricKey: "VDC.mean",
                DisplayRole: "scalar",
                Unit: "V",
                LimitLow: 0),
            "path/mean");
        Assert.NotNull(gauge);
        Assert.Single(tiles);
        Assert.Equal(PresentationTileKind.Scalar, tiles[0].Kind);
        Assert.Equal("VDC.mean", tiles[0].MetricKey);
        Assert.Contains("V", tiles[0].ValueText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFromStoredSamples_creates_chart_and_gauge()
    {
        var tiles = PresentationRoleMap.BuildFromStoredSamples(
        [
            new Core.Runs.StoredSample
            {
                Channel = "VDC",
                MetricKey = "VDC",
                DisplayRole = "timeseries",
                Unit = "V",
                Value = 1.0,
                Timestamp = DateTimeOffset.UtcNow,
            },
            new Core.Runs.StoredSample
            {
                Channel = "VDC",
                MetricKey = "VDC",
                DisplayRole = "timeseries",
                Unit = "V",
                Value = 1.1,
                Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(10),
            },
            new Core.Runs.StoredSample
            {
                Channel = "Mean",
                MetricKey = "VDC.mean",
                DisplayRole = "passband",
                Unit = "V",
                Value = 1.05,
                LimitLow = 0.5,
                Timestamp = DateTimeOffset.UtcNow,
            },
        ]);

        Assert.Contains(tiles, t => t.IsChart && t.MetricKey == "VDC" && t.YsLength == 2);
        Assert.Contains(tiles, t => t.IsGauge && t.Kind == PresentationTileKind.Passband && t.ShowBand);
        Assert.True(tiles[0].IsGauge, "Band gauges should sort before timeseries charts");
    }

    [Fact]
    public void BuildFromStoredSamples_keeps_timing_passband_gauges()
    {
        var tiles = PresentationRoleMap.BuildFromStoredSamples(
        [
            new Core.Runs.StoredSample
            {
                Channel = "bump.rise.ms",
                MetricKey = "bump.rise.ms",
                DisplayRole = "passband",
                Unit = "ms",
                Value = 8,
                LimitLow = 5,
                LimitHigh = 15,
                Timestamp = DateTimeOffset.UtcNow,
            },
            new Core.Runs.StoredSample
            {
                Channel = "envelope.error",
                MetricKey = "envelope.error",
                DisplayRole = "passband",
                Unit = "V",
                Value = 0.02,
                LimitLow = 0,
                LimitHigh = 0.1,
                Timestamp = DateTimeOffset.UtcNow,
            },
        ]);

        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.IsGauge));
        Assert.Contains(tiles, t => t.MetricKey == "bump.rise.ms" && !t.IsOutOfBand);
    }
}
