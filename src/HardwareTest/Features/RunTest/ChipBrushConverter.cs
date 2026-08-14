using Avalonia.Data.Converters;
using Avalonia.Media;
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
}
