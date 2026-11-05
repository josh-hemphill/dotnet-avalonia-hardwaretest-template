using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HardwareTest.Core.Runs;

namespace HardwareTest.Features.RunTest;

/// Chip background hex values (white foreground). Contrast is gated in tests via ContrastMath.
public static class ChipPalette
{
    public const string Foreground = "#FFFFFF";
    public const string Pass = "#2E7D32";
    public const string Fail = "#C62828";
    public const string Running = "#1565C0";
    public const string Awaiting = "#BF360C";
    public const string Error = "#6A1B9A";
    public const string Pending = "#37474F";
}

/// Maps status chip labels / <see cref="RunResult"/> to brushes readable on Light and Dark.
public static class ChipBrushConverter
{
    public static readonly IValueConverter Instance = new FuncValueConverter<object?, IBrush>(chip =>
    {
        var key = chip switch
        {
            RunResult result => result.ToString(),
            string text => text,
            _ => chip?.ToString(),
        };
        var hex = key switch
        {
            "Pass" or "Passed" => ChipPalette.Pass,
            "Fail" or "Failed" => ChipPalette.Fail,
            "Running" => ChipPalette.Running,
            "Awaiting" => ChipPalette.Awaiting,
            "Error" or "Cancelled" => ChipPalette.Error,
            _ => ChipPalette.Pending,
        };
        return new SolidColorBrush(Color.Parse(hex));
    });

    /// Light banner wash remains available to nonvisual consumers.
    public static readonly IValueConverter BannerFill = new FuncValueConverter<object?, IBrush>(
        chip => FilterBannerFill(chip, ThemeVariant.Light));

    /// Observe the actual control theme, so a retained filter updates without changing its value.
    public static readonly IMultiValueConverter BannerFillForTheme = new FuncMultiValueConverter<object?, IBrush>(
        values => FilterBannerFill(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1) as ThemeVariant));

    public static IBrush FilterBannerFill(object? chip, ThemeVariant? theme)
    {
        var key = chip switch
        {
            RunResult result => result.ToString(),
            string text => text,
            _ => chip?.ToString(),
        };
        var hex = theme == ThemeVariant.Dark
            ? key switch
            {
                "Pass" or "Passed" => "#153C26",
                "Fail" or "Failed" => "#3B1820",
                "Running" => "#173754",
                _ => "#27353A",
            }
            : key switch
            {
                "Pass" or "Passed" => "#C8E6C9",
                "Fail" or "Failed" => "#FFCDD2",
                "Running" => "#BBDEFB",
                _ => "#ECEFF1",
            };
        return new SolidColorBrush(Color.Parse(hex));
    }
}
