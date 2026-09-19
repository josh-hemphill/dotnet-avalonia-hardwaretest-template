using HardwareTest.Authoring;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringInspectorCopyTests
{
    [Fact]
    public void Every_catalog_function_has_a_title()
    {
        foreach (var spec in AuthoringFunctionCatalog.All)
        {
            var display = AuthoringInspectorCopy.DescribeFunction(spec.Id);
            Assert.Equal(spec.Id, display.Id);
            Assert.False(string.IsNullOrWhiteSpace(display.Title), spec.Id);
        }

        Assert.Contains("Acquire", AuthoringInspectorCopy.DescribeFunction(AuthoringFunctionIds.BasicAcquireVoltage).Title, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(
            AuthoringInspectorCopy.DescribeFunction(AuthoringFunctionIds.BasicAcquireVoltage).Summary));
    }

    [Fact]
    public void Unknown_function_keeps_the_raw_id()
    {
        var display = AuthoringInspectorCopy.DescribeFunction("Custom.Unknown");
        Assert.Equal("Custom.Unknown", display.Id);
        Assert.Equal("Custom.Unknown", display.Title);
    }

    [Fact]
    public void Series_compliance_setting_has_label_placeholder_and_mode_tokens()
    {
        var presented = AuthoringInspectorCopy.PresentSetting("SeriesCompliance");
        Assert.Equal("Series compliance", presented.Label);
        Assert.Equal(SeriesComplianceModes.None, presented.ValuePlaceholder);
        Assert.Contains(SeriesComplianceModes.None, presented.ValueTooltip, StringComparison.Ordinal);
        Assert.Contains(SeriesComplianceModes.AllSamples, presented.ValueTooltip, StringComparison.Ordinal);
        Assert.Contains(SeriesComplianceModes.Dwell, presented.ValueTooltip, StringComparison.Ordinal);
        Assert.Contains("allSamples", AuthoringInspectorCopy.SeriesComplianceModeCaption("allSamples"), StringComparison.Ordinal);
        Assert.Equal("Dwell (dwell)", AuthoringInspectorCopy.SeriesComplianceModeCaption("dwell"));

        var fail = AuthoringInspectorCopy.PresentSetting("FailWhenOutOfBand");
        Assert.Equal("Fail when out of band", fail.Label);
        Assert.Equal("false", fail.ValuePlaceholder);
        Assert.Contains("true/false", fail.ValueTooltip, StringComparison.Ordinal);

        var dwell = AuthoringInspectorCopy.PresentSetting("DwellLimitMs");
        Assert.Equal("Dwell limit (ms)", dwell.Label);
        Assert.Equal("5", dwell.ValuePlaceholder);
        Assert.Contains("dwell", dwell.ValueTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_count_label_is_spaced()
    {
        Assert.Equal("Sample count", AuthoringInspectorCopy.PresentSetting("SampleCount").Label);
    }
}
