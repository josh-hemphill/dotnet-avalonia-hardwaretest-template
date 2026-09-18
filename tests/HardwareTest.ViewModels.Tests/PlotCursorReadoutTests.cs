using HardwareTest.Widgets.MeasurementPlot;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class PlotCursorReadoutTests
{
    [Fact]
    public void TryNearestSample_picks_closest_elapsed()
    {
        double[] xs = [0.0, 1.0, 2.0, 3.0];
        double[] ys = [10.0, 11.0, 12.0, 13.0];

        Assert.True(PlotCursorReadout.TryNearestSample(xs, ys, 4, 2.4, out var index, out var sampleX, out var sampleY));
        Assert.Equal(2, index);
        Assert.Equal(2.0, sampleX);
        Assert.Equal(12.0, sampleY);
    }

    [Fact]
    public void TryNearestSample_uses_index_when_xs_are_short()
    {
        double[] ys = [4.0, 5.0, 6.0];

        Assert.True(PlotCursorReadout.TryNearestSample([], ys, 3, 1.8, out var index, out var sampleX, out var sampleY));
        Assert.Equal(2, index);
        Assert.Equal(2.0, sampleX);
        Assert.Equal(6.0, sampleY);
    }

    [Fact]
    public void TryNearestSample_false_when_empty()
    {
        Assert.False(PlotCursorReadout.TryNearestSample([], [], 0, 1, out _, out _, out _));
    }

    [Fact]
    public void FormatValue_includes_unit()
    {
        Assert.Equal("1.25 V", PlotCursorReadout.FormatValue(1.25, "V"));
        Assert.Equal("1.25", PlotCursorReadout.FormatValue(1.25, null));
    }

    [Fact]
    public void FormatBand_reports_out_of_band()
    {
        Assert.Contains("Out of band", PlotCursorReadout.FormatBand(9.9, 0, 1, "V"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Within", PlotCursorReadout.FormatBand(0.5, 0, 1, "V"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("No limits", PlotCursorReadout.FormatBand(1, null, null, "V"));
    }

    [Fact]
    public void IsTap_treats_a_10px_move_as_a_pan()
    {
        Assert.True(PlotCursorReadout.IsTap(0, 0));
        Assert.True(PlotCursorReadout.IsTap(9, 0));
        Assert.False(PlotCursorReadout.IsTap(10, 0));
        Assert.False(PlotCursorReadout.IsTap(6, 8));
    }
}
