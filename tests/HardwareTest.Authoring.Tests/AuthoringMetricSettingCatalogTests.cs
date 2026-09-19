using HardwareTest.Authoring;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringMetricSettingCatalogTests
{
    [Fact]
    public void Known_keys_resolve_to_typed_rows()
    {
        var sample = AuthoringMetricSettingCatalog.CreateRow(
            AuthoringFunctionIds.BasicAcquireVoltage,
            "SampleCount",
            "32",
            []);
        Assert.Equal(AuthoringSettingKind.Integer, sample.Kind);
        Assert.True(sample.IsInteger);
        Assert.Equal(1, sample.Minimum);

        var fail = AuthoringMetricSettingCatalog.CreateRow("*", "FailWhenOutOfBand", "false", []);
        Assert.Equal(AuthoringSettingKind.Boolean, fail.Kind);
        Assert.False(fail.BoolValue);

        var series = AuthoringMetricSettingCatalog.CreateRow("*", "SeriesCompliance", "none", []);
        Assert.Equal(AuthoringSettingKind.Choice, series.Kind);
        Assert.Equal(SeriesComplianceModes.Choices, series.Choices);
        Assert.False(series.ChoiceIsEditable);

        var channel = AuthoringMetricSettingCatalog.CreateRow("*", "Channel", "VDC.extra", ["VDC"]);
        Assert.Equal(AuthoringSettingKind.Choice, channel.Kind);
        Assert.True(channel.ChoiceIsEditable);
        Assert.Contains("VDC", channel.Choices!);
        Assert.Contains("VDC.extra", channel.Choices!);

        var unknown = AuthoringMetricSettingCatalog.CreateRow("*", "MetricName", "rail.mean", []);
        Assert.Equal(AuthoringSettingKind.Text, unknown.Kind);
        Assert.True(unknown.IsText);

        var threshold = AuthoringMetricSettingCatalog.CreateRow("*", "Threshold", "1.25", []);
        Assert.Equal(AuthoringSettingKind.Double, threshold.Kind);
        Assert.Equal("0.################", threshold.NumberFormat);
        Assert.Equal(1.25m, threshold.NumberValue);

        var elapsed = AuthoringMetricSettingCatalog.CreateRow("*", "ElapsedMs", "12.5", []);
        Assert.Equal(AuthoringSettingKind.Double, elapsed.Kind);
        Assert.Equal(0, elapsed.Minimum);

        var ts = AuthoringMetricSettingCatalog.CreateRow("*", "TsSeconds", "0.005", []);
        Assert.Equal(AuthoringSettingKind.Double, ts.Kind);
        Assert.Equal(0.005m, ts.NumberValue);

        var interval = AuthoringMetricSettingCatalog.CreateRow("*", "IntervalMs", "10", []);
        Assert.Equal(AuthoringSettingKind.Integer, interval.Kind);
        Assert.Equal(0, interval.Minimum);

        var dwell = AuthoringMetricSettingCatalog.CreateRow("*", "DwellLimitMs", "5", []);
        Assert.Equal(AuthoringSettingKind.Double, dwell.Kind);
        Assert.Equal(0, dwell.Minimum);

        var summaries = AuthoringMetricSettingCatalog.CreateRow("*", "PublishSummaries", "true", []);
        Assert.True(summaries.IsBoolean);
        Assert.True(summaries.BoolValue);
    }

    [Fact]
    public void FormatDecimal_keeps_values_outside_int32()
    {
        Assert.Equal("2147483648", AuthoringInvariantNumbers.FormatDecimal(2147483648m));
        Assert.Equal("-2147483649", AuthoringInvariantNumbers.FormatDecimal(-2147483649m));
        Assert.Equal("1.234567", AuthoringInvariantNumbers.FormatDecimal(1.234567m));
    }
}
