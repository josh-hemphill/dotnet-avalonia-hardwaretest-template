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
    }
}
