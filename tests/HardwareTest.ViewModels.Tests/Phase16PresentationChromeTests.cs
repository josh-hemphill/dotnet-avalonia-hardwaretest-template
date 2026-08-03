using HardwareTest.Features.Presentation;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Phase 16 — Band board + Focus trend chrome.
public sealed class Phase16PresentationChromeTests
{
    private static HierarchyStepViewModel Leaf(string name = "Acquire", string? path = null)
        => new(new OpenTapStepNode
        {
            Id = name,
            Name = name,
            Path = path ?? $"Suite/{name}",
        });

    [Fact]
    public void Default_chrome_is_Band_until_timeseries_step_selected()
    {
        var live = new LivePresentationViewModel();
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);

        var step = Leaf();
        live.ApplySample(
            new MeasurementSampleEvent("VDC", 0, 1.0, DateTimeOffset.UtcNow, DisplayRole: "timeseries"),
            step.Path,
            null,
            selectedStep: null);

        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);
        Assert.False(live.ShowFocusTrend);

        live.RefreshChrome(step);
        Assert.Equal(PresentationChromeMode.Focus, live.ChromeMode);
        Assert.True(live.ShowFocusTrend);
        Assert.True(live.ShowPlotForSelection);
    }

    [Fact]
    public void Toggle_hides_and_show_restores_Focus()
    {
        var live = new LivePresentationViewModel();
        var step = Leaf();
        live.ApplySample(
            new MeasurementSampleEvent("VDC", 0, 1.0, DateTimeOffset.UtcNow),
            step.Path,
            null,
            step);

        Assert.True(live.ShowFocusTrend);
        live.ToggleFocusTrendCommand.Execute().Subscribe();
        Assert.False(live.ShowFocusTrend);
        Assert.Equal(PresentationChromeMode.Band, live.ChromeMode);

        live.ToggleFocusTrendCommand.Execute().Subscribe();
        Assert.True(live.ShowFocusTrend);
        Assert.Equal(PresentationChromeMode.Focus, live.ChromeMode);
    }

    [Fact]
    public void Out_of_band_gauge_promotes_Focus_when_plot_data_exists()
    {
        var live = new LivePresentationViewModel();
        var acquire = Leaf("Acquire", "Suite/Acquire");
        var mean = Leaf("Mean", "Suite/Mean");
        live.ApplySample(
            new MeasurementSampleEvent("VDC", 0, 1.0, DateTimeOffset.UtcNow, DisplayRole: "timeseries"),
            acquire.Path,
            null,
            acquire);
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
        Assert.True(live.ShowFocusTrend);
        Assert.Equal(PresentationChromeMode.Focus, live.ChromeMode);
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
