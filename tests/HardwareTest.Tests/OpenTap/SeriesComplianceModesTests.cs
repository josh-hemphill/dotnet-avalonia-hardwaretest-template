using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

public sealed class SeriesComplianceModesTests
{
    [Fact]
    public void IsOutOfBand_is_inclusive()
    {
        Assert.False(SeriesComplianceModes.IsOutOfBand(3.2, 3.2, 3.5));
        Assert.False(SeriesComplianceModes.IsOutOfBand(3.5, 3.2, 3.5));
        Assert.True(SeriesComplianceModes.IsOutOfBand(3.19, 3.2, 3.5));
        Assert.True(SeriesComplianceModes.IsOutOfBand(3.51, 3.2, 3.5));
        Assert.False(SeriesComplianceModes.IsOutOfBand(0, null, null));
    }

    [Fact]
    public void ParseScriptedValues_skips_blank_and_invalid_tokens()
    {
        Assert.Empty(SeriesComplianceModes.ParseScriptedValues(""));
        Assert.Equal([3.3, 3.6], SeriesComplianceModes.ParseScriptedValues("3.3, nope, 3.6,"));
    }

    [Fact]
    public void Summaries_match_scripted_envelope_demo()
    {
        double[] values = [3.30, 3.32, 3.60, 3.31];
        Assert.Equal(75, SeriesComplianceModes.InBandPercent(values, 3.2, 3.5));
        Assert.Equal(0.10, SeriesComplianceModes.MaxExcursion(values, 3.2, 3.5), 6);
        Assert.Equal(5, SeriesComplianceModes.MaxOutOfBandMs(values, 3.2, 3.5, 5));
    }

    [Fact]
    public void InBandPercent_is_100_when_empty_or_unlimited()
    {
        Assert.Equal(100, SeriesComplianceModes.InBandPercent([], 0, 1));
        Assert.Equal(100, SeriesComplianceModes.InBandPercent([9], null, null));
    }

    [Fact]
    public void ShouldFailSample_allSamples_and_dwell()
    {
        var dwell = 0.0;
        Assert.True(SeriesComplianceModes.ShouldFailSample(
            SeriesComplianceModes.AllSamples, true, 3.6, 3.2, 3.5, null, 5, ref dwell));
        Assert.Equal(5, dwell);

        dwell = 0;
        Assert.False(SeriesComplianceModes.ShouldFailSample(
            SeriesComplianceModes.AllSamples, false, 3.6, 3.2, 3.5, null, 5, ref dwell));

        dwell = 0;
        Assert.False(SeriesComplianceModes.ShouldFailSample(
            SeriesComplianceModes.Dwell, true, 3.6, 3.2, 3.5, 5, 5, ref dwell));
        Assert.True(SeriesComplianceModes.ShouldFailSample(
            SeriesComplianceModes.Dwell, true, 3.61, 3.2, 3.5, 5, 5, ref dwell));
        Assert.Equal(10, dwell);
    }
}
