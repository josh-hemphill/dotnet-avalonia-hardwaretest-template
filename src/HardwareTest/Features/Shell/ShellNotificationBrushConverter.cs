using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HardwareTest.Features.RunTest;

namespace HardwareTest.Features.Shell;

/// Theme-aware brushes/icons for the MainWindow shell notification strip.
public static class ShellNotificationBrushConverter
{
    public static readonly IValueConverter Background =
        new FuncValueConverter<ShellNotificationSeverity, IBrush>(BackgroundFor);

    public static readonly IValueConverter Border =
        new FuncValueConverter<ShellNotificationSeverity, IBrush>(BorderFor);

    public static readonly IValueConverter Icon =
        new FuncValueConverter<ShellNotificationSeverity, string>(IconFor);

    /// Maps Run board severity into shell strip severity (single mapping for host + chrome).
    public static ShellNotificationSeverity FromRun(RunBannerSeverity severity) => severity switch
    {
        RunBannerSeverity.Error => ShellNotificationSeverity.Error,
        RunBannerSeverity.Warning => ShellNotificationSeverity.Warning,
        _ => ShellNotificationSeverity.Info,
    };

    private static IBrush BackgroundFor(ShellNotificationSeverity s) =>
        IsDarkTheme()
            ? s switch
            {
                ShellNotificationSeverity.Critical or ShellNotificationSeverity.Error => Brush("#3B1010"),
                ShellNotificationSeverity.Warning => Brush("#2C2000"),
                _ => Brush("#1A2530"),
            }
            : s switch
            {
                ShellNotificationSeverity.Critical or ShellNotificationSeverity.Error => Brush("#FFEBEE"),
                ShellNotificationSeverity.Warning => Brush("#FFF8E1"),
                _ => Brush("#E3F2FD"),
            };

    private static IBrush BorderFor(ShellNotificationSeverity s) => s switch
    {
        ShellNotificationSeverity.Critical or ShellNotificationSeverity.Error => Brush("#C62828"),
        ShellNotificationSeverity.Warning => Brush("#F57F17"),
        _ => Brush("#1565C0"),
    };

    private static string IconFor(ShellNotificationSeverity s) => s switch
    {
        ShellNotificationSeverity.Critical => "Critical",
        ShellNotificationSeverity.Error => "Error",
        ShellNotificationSeverity.Warning => "Warning",
        _ => "Info",
    };

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

        return false;
    }
}
