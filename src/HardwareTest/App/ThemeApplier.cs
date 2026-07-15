using Avalonia;
using Avalonia.Styling;
using HardwareTest.Core.Settings;

namespace HardwareTest;

/// Applies ThemePreference (System/Light/Dark) to the Avalonia application.
public static class ThemeApplier
{
    public static void Apply(string? preference)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedThemeVariant = preference?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static void Apply(AppSettings settings) => Apply(settings.ThemePreference);
}
