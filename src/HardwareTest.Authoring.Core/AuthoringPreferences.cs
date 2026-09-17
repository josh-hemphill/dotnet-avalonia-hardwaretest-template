namespace HardwareTest.Authoring;

/// Canonical theme strings for authoring-preferences.json (Avalonia ThemeVariant stays in the exe).
public static class AuthoringThemePreference
{
    public const string System = "System";

    public const string Light = "Light";

    public const string Dark = "Dark";

    public static IReadOnlyList<string> Options { get; } = [System, Light, Dark];

    /// Maps operator-style case variants onto System / Light / Dark.
    public static string Normalize(string? preference)
        => preference?.Trim().ToLowerInvariant() switch
        {
            "light" => Light,
            "dark" => Dark,
            _ => System,
        };
}

/// Per-user workstation prefs. Not operator appliance settings.json.
public sealed class AuthoringPreferences
{
    public int SchemaVersion { get; set; } = AuthoringSchemaVersions.Preferences;

    public string ThemePreference { get; set; } = AuthoringThemePreference.System;

    public string? LastWorkspace { get; set; }

    public string? OpenTapHomeOverride { get; set; }

    public bool ShowRawStepXml { get; set; } = true;
}

public sealed class AuthoringPreferencesException : InvalidOperationException
{
    public AuthoringPreferencesException(string message)
        : base(message)
    {
    }

    public AuthoringPreferencesException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
