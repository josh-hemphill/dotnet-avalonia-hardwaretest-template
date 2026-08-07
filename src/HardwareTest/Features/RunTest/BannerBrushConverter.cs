using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using HardwareTest.Features.Shell;

namespace HardwareTest.Features.RunTest;

/// Maps banner severities to theme-aware background and border brushes.
public static class BannerBrushConverter
{
    public static readonly IValueConverter Background = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        BackgroundFor(Map(s)));

    public static readonly IValueConverter Border = new FuncValueConverter<RunBannerSeverity, IBrush>(s =>
        BorderFor(Map(s)));

    public static readonly IValueConverter Icon = new FuncValueConverter<RunBannerSeverity, string>(s =>
        IconFor(Map(s)));

    public static readonly IValueConverter ShellBackground = new FuncValueConverter<ShellNotificationSeverity, IBrush>(BackgroundFor);

    public static readonly IValueConverter ShellBorder = new FuncValueConverter<ShellNotificationSeverity, IBrush>(BorderFor);

    public static readonly IValueConverter ShellIcon = new FuncValueConverter<ShellNotificationSeverity, string>(IconFor);

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

    private static ShellNotificationSeverity Map(RunBannerSeverity s) => s switch
    {
        RunBannerSeverity.Error => ShellNotificationSeverity.Error,
        RunBannerSeverity.Warning => ShellNotificationSeverity.Warning,
        _ => ShellNotificationSeverity.Info,
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

        // Default / system unknown — prefer light-safe surfaces for first impression.
        return false;
    }
}
