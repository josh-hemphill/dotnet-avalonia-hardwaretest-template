using Avalonia.Headless.XUnit;
using HardwareTest.Widgets.MeasurementPlot;
using ScottPlot;
using ScottPlot.Avalonia;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class PlotFontFallbackTests
{
    [AvaloniaFact]
    public void Measurement_axes_and_legend_resolve_to_fonts_with_readable_glyphs()
    {
        var view = new MeasurementPlotView();
        view.SetLabels("Recorded voltage", "V", "VDC");
        var plot = Assert.IsType<AvaPlot>(view.Content).Plot;
        foreach (var font in new[] { plot.Axes.Bottom.Label.FontName, plot.Axes.Left.Label.FontName, plot.Legend.FontName })
        {
            var effective = string.IsNullOrWhiteSpace(font) ? Fonts.Default : font;
            Assert.True(Fonts.GetTypeface(effective, false, false).GlyphCount > 0,
                $"Plot font '{font}' (default '{Fonts.Default}') must contain glyphs.");
        }
    }
}
