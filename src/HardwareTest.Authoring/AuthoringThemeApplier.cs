using Avalonia;
using Avalonia.Styling;

namespace HardwareTest.Authoring;

/// Maps System/Light/Dark onto RequestedThemeVariant. Operator ThemeApplier stays in HardwareTest.
public static class AuthoringThemeApplier
{
    public static void Apply(string? preference)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedThemeVariant = AuthoringThemePreference.Normalize(preference) switch
        {
            AuthoringThemePreference.Light => ThemeVariant.Light,
            AuthoringThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
