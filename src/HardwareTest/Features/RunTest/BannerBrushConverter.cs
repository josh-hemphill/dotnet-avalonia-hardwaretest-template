using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HardwareTest.Features.RunTest;

/// Maps <see cref="RunBannerSeverity"/> to background and border brushes for the in-panel banner.
public static class BannerBrushConverter
{
    public static readonly IValueConverter Background = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        s switch
        {
            RunBannerSeverity.Error => new SolidColorBrush(Color.Parse("#3B1010")),
            RunBannerSeverity.Warning => new SolidColorBrush(Color.Parse("#2C2000")),
            _ => new SolidColorBrush(Color.Parse("#1A2530")),
        });

    public static readonly IValueConverter Border = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        s switch
        {
            RunBannerSeverity.Error => new SolidColorBrush(Color.Parse("#D32F2F")),
            RunBannerSeverity.Warning => new SolidColorBrush(Color.Parse("#F57F17")),
            _ => new SolidColorBrush(Color.Parse("#546E7A")),
        });

    public static readonly IValueConverter Icon = new FuncValueConverter<RunBannerSeverity, string>(s =>
        s switch
        {
            RunBannerSeverity.Error => "Error",
            RunBannerSeverity.Warning => "Warning",
            _ => "Info",
        });
}
