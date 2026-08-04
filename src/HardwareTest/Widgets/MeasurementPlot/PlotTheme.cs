using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ScottPlot;
using AvaColor = Avalonia.Media.Color;
using SpColor = ScottPlot.Color;

namespace HardwareTest.Widgets.MeasurementPlot;

/// Maps Avalonia Fluent chrome + app accents onto ScottPlot so live charts match the shell.
internal static class PlotTheme
{
    /// Matches <c>Button.primary</c> / Running chip.
    private static readonly SpColor SeriesLight = SpColor.FromHex("#1565C0");

    /// Lighter blue for dark surfaces.
    private static readonly SpColor SeriesDark = SpColor.FromHex("#42A5F5");

    /// Matches Awaiting chip / warning accents.
    private static readonly SpColor LimitLine = SpColor.FromHex("#EF6C00");

    public static SpColor SeriesColor => IsDarkTheme() ? SeriesDark : SeriesLight;

    public static SpColor LimitColor => LimitLine;

    public static void Apply(Plot plot)
    {
        var dark = IsDarkTheme();
        var figure = Resolve(
            "SystemControlBackgroundChromeMediumLowBrush",
            dark ? "#2C2C2C" : "#F3F3F3");
        var axis = Resolve(
            "SystemControlForegroundBaseHighBrush",
            dark ? "#F3F3F3" : "#1A1A1A");
        var muted = Resolve(
            "SystemControlForegroundBaseMediumBrush",
            dark ? "#A0A0A0" : "#666666");
        var grid = dark
            ? SpColor.FromHex("#FFFFFF22")
            : SpColor.FromHex("#00000018");

        plot.SetStyle(new PlotStyle
        {
            FigureBackgroundColor = figure,
            DataBackgroundColor = figure,
            AxisColor = axis,
            GridMajorLineColor = grid,
            LegendBackgroundColor = figure,
            LegendFontColor = axis,
            LegendOutlineColor = muted,
        });
    }

    public static bool IsDarkTheme()
    {
        var variant = Application.Current?.ActualThemeVariant;
        if (variant == ThemeVariant.Dark)
        {
            return true;
        }

        if (variant == ThemeVariant.Light)
        {
            return false;
        }

        return false;
    }

    private static SpColor Resolve(string resourceKey, string fallbackHex)
    {
        var app = Application.Current;
        if (app is not null
            && app.TryGetResource(resourceKey, app.ActualThemeVariant, out var resource)
            && TryToSpColor(resource, out var color))
        {
            return color;
        }

        return SpColor.FromHex(fallbackHex);
    }

    private static bool TryToSpColor(object? resource, out SpColor color)
    {
        switch (resource)
        {
            case ISolidColorBrush brush:
                color = FromAva(brush.Color);
                return true;
            case AvaColor ava:
                color = FromAva(ava);
                return true;
            default:
                color = default;
                return false;
        }
    }

    private static SpColor FromAva(AvaColor c)
        => new(c.R, c.G, c.B, c.A);
}
