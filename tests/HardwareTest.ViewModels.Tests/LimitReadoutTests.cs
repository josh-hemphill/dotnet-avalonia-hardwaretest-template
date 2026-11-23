using HardwareTest.Widgets.Presentation;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class LimitReadoutTests
{
    [Theory]
    [InlineData(99.88, 93d, 99.9, "Margin 0.02 pts to upper limit")]
    [InlineData(99.91, 93d, 99.9, "0.01 pts above upper limit")]
    [InlineData(92.95, 93d, 99.9, "0.05 pts below lower limit")]
    [InlineData(99.9, 93d, 99.9, "At upper limit")]
    [InlineData(93d, 93d, 99.9, "At lower limit")]
    [InlineData(1d, 1d, 1d, "At both limits")]
    [InlineData(99.8, null, 99.9, "Margin 0.1 pts to upper limit")]
    [InlineData(93.1, 93d, null, "Margin 0.1 pts to lower limit")]
    [InlineData(99.8, 100d, 90d, "Invalid limits")]
    [InlineData(double.NaN, 0d, 1d, "Result unavailable")]
    public void Displays_distance_relative_to_the_applicable_limit(double value, double? lower, double? upper, string expected)
    {
        var reading = Assert.IsType<LimitReadout>(LimitReadout.Create(value, lower, upper, "pts"));
        Assert.Equal(expected, reading.Distance);
        if (lower is null) Assert.Equal("—", reading.Lower);
        if (upper is null) Assert.Equal("—", reading.Upper);
    }

    [Fact]
    public void A_near_miss_and_its_boundary_remain_visibly_distinct()
    {
        var reading = Assert.IsType<LimitReadout>(LimitReadout.Create(Math.BitIncrement(1), 0d, 1d, null));
        Assert.Equal("1", reading.Upper);
        Assert.NotEqual(reading.Upper, reading.Value);
        Assert.EndsWith("above upper limit", reading.Distance, StringComparison.Ordinal);
        Assert.Null(LimitReadout.Create(1, null, null, null));
    }
}
