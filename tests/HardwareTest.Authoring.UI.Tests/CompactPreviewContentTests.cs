using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.Widgets.MeasurementPlot;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class CompactPreviewContentTests
{
    [AvaloniaFact]
    public async Task Compact_preview_initially_shows_chart_data_and_details_do_not_shrink_it()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        fixture.NavigateTask(5);
        await fixture.WaitForReadinessAsync();
        var viewport = fixture.Control<ScrollViewer>("Operator board viewport");
        var plot = window.GetVisualDescendants().OfType<MeasurementPlotView>().First(view => view.IsEffectivelyVisible);
        var origin = plot.TranslatePoint(default, viewport);
        Assert.NotNull(origin);
        var visibleHeight = Math.Min(origin.Value.Y + plot.Bounds.Height, viewport.Viewport.Height) - Math.Max(0, origin.Value.Y);
        Assert.True(visibleHeight >= 90, $"The initial preview should show useful plotted data, received {visibleHeight}px.");
        Assert.False(plot.IsHitTestVisible);
        var height = plot.Height;
        var tile = plot.GetVisualAncestors().OfType<BoardPreviewTileView>().Single();
        var details = tile.GetVisualDescendants().OfType<Expander>().Single();
        Assert.False(details.IsExpanded);
        details.IsExpanded = true;
        AuthoringUiFixture.Drain();
        Assert.Equal(height, plot.Height, 1);
        Assert.NotEmpty(details.GetVisualDescendants().OfType<TextBlock>());
    }
}
