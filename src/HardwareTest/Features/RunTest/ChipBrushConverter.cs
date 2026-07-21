using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HardwareTest.Features.RunTest;

/// Maps status chip labels to background brushes.
public static class ChipBrushConverter
{
    public static readonly IValueConverter Instance = new FuncValueConverter<string?, IBrush>(chip =>
        chip switch
        {
            "Pass" => new SolidColorBrush(Color.Parse("#2E7D32")),
            "Fail" => new SolidColorBrush(Color.Parse("#C62828")),
            "Running" => new SolidColorBrush(Color.Parse("#1565C0")),
            "Awaiting" => new SolidColorBrush(Color.Parse("#EF6C00")),
            _ => new SolidColorBrush(Color.Parse("#607D8B")),
        });
}
