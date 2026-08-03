using Avalonia.Data.Converters;
using Avalonia.Media;
using HardwareTest.Core.Runs;

namespace HardwareTest.Features.RunTest;

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
        return key switch
        {
            "Pass" or "Passed" => new SolidColorBrush(Color.Parse("#2E7D32")),
            "Fail" or "Failed" => new SolidColorBrush(Color.Parse("#C62828")),
            "Running" => new SolidColorBrush(Color.Parse("#1565C0")),
            "Awaiting" => new SolidColorBrush(Color.Parse("#EF6C00")),
            "Error" or "Cancelled" => new SolidColorBrush(Color.Parse("#6A1B9A")),
            _ => new SolidColorBrush(Color.Parse("#607D8B")),
        };
    });
}
