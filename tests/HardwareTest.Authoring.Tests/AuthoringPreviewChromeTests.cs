using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringPreviewChromeTests
{
    [Fact]
    public void Scalar_preview_maps_to_gauge_chrome()
    {
        var preview = MetricPreviewBuilder.From(
            new MetricDraft(
                "Mean",
                "VDC.mean",
                PresentationRoles.Scalar,
                "V",
                new LimitSpec(null, null, 1.2),
                null,
                new AlgorithmSource(AuthoringFunctionIds.BasicMeanGte, [], new Dictionary<string, string>())));
        var chrome = AuthoringPreviewChromeBuilder.From(preview);
        Assert.True(chrome.IsGauge);
        Assert.False(chrome.IsChart);
        Assert.False(chrome.IsStrip);
        Assert.False(chrome.IsText);
        Assert.Equal("VDC.mean", chrome.MetricKey);
        Assert.Contains("V", chrome.ValueText, StringComparison.Ordinal);
        Assert.False(chrome.ShowBand);
    }

    [Fact]
    public void Passband_preview_shows_band_and_limits()
    {
        var preview = MetricPreviewBuilder.From(
            new MetricDraft(
                "Band",
                "rail.mean",
                PresentationRoles.Passband,
                "V",
                new LimitSpec(1.1, 1.4, null),
                null,
                new AlgorithmSource(AuthoringFunctionIds.BasicPublishBandScalar, [], new Dictionary<string, string>())));
        var chrome = AuthoringPreviewChromeBuilder.From(preview);
        Assert.True(chrome.IsGauge);
        Assert.True(chrome.ShowBand);
        Assert.Equal(1.1, chrome.LimitLow);
        Assert.Equal(1.4, chrome.LimitHigh);
        Assert.Contains("1.1", chrome.LimitsText, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeseries_preview_maps_to_chart_chrome()
    {
        var preview = MetricPreviewBuilder.From(
            new MetricDraft(
                "Acquire",
                "VDC",
                PresentationRoles.Timeseries,
                "V",
                null,
                null,
                new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        var chrome = AuthoringPreviewChromeBuilder.From(preview);
        Assert.True(chrome.IsChart);
        Assert.False(chrome.IsGauge);
        Assert.True(chrome.Ys.Count > 1);
        Assert.Equal(chrome.Ys.Count, chrome.Xs.Count);
    }

    [Fact]
    public void Timing_preview_maps_to_strip_and_keeps_recording_events()
    {
        var preview = MetricPreviewBuilder.From(
            new MetricDraft(
                "Timing",
                "event.mark",
                PresentationRoles.Timing,
                "s",
                null,
                null,
                new AlgorithmSource(AuthoringFunctionIds.BasicPublishTimedSample, [], new Dictionary<string, string>())));
        var chrome = AuthoringPreviewChromeBuilder.From(
            preview,
            [new HardwareTest.Core.Runs.StoredEvent { Name = "bit", ElapsedMs = 2500, Label = "1" }]);
        Assert.True(chrome.IsStrip);
        Assert.False(chrome.IsGauge);
        var mark = Assert.Single(chrome.Events);
        Assert.Equal("bit", mark.Name);
        Assert.Equal(2500, mark.ElapsedMs);
        Assert.True(chrome.DurationSec >= 2.5);
    }
}
