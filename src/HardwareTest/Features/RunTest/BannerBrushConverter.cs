using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace HardwareTest.Features.RunTest;

/// Maps <see cref="RunBannerSeverity"/> to theme-aware background and border brushes.
public static class BannerBrushConverter
{
    public static readonly IValueConverter Background = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        IsDarkTheme()
            ? s switch
            {
                RunBannerSeverity.Error => Brush("#3B1010"),
                RunBannerSeverity.Warning => Brush("#2C2000"),
                _ => Brush("#1A2530"),
            }
            : s switch
            {
                RunBannerSeverity.Error => Brush("#FFEBEE"),
                RunBannerSeverity.Warning => Brush("#FFF8E1"),
                _ => Brush("#E3F2FD"),
            });

    public static readonly IValueConverter Border = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        s switch
        {
            RunBannerSeverity.Error => Brush("#C62828"),
            RunBannerSeverity.Warning => Brush("#F57F17"),
            _ => Brush("#1565C0"),
        });

    public static readonly IValueConverter Icon = new FuncValueConverter<RunBannerSeverity, string>(s =>
        s switch
        {
            RunBannerSeverity.Error => "Error",
            RunBannerSeverity.Warning => "Warning",
            _ => "Info",
        });

    /// Storage banner background: critical → error surface, warn → warning surface.
    public static readonly IValueConverter StorageBackground = new FuncValueConverter<bool, IBrush>(critical =>
    {
        if (critical)
        {
            return IsDarkTheme() ? Brush("#3B1010") : Brush("#FFEBEE");
        }

        return IsDarkTheme() ? Brush("#2C2000") : Brush("#FFF8E1");
    });

    public static readonly IValueConverter StorageBorder = new FuncValueConverter<bool, IBrush>(critical =>
        critical ? Brush("#C62828") : Brush("#F57F17"));

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static bool IsDarkTheme()
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

        // Default / system unknown — prefer light-safe surfaces for first impression.
        return false;
    }
}
