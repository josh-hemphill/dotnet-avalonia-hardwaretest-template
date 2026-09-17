using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringPreferencesTests
{
    [Fact]
    public void Load_missing_file_uses_defaults()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        var store = new AuthoringPreferencesStore(path);
        store.Load();

        Assert.False(store.IsReadOnly);
        Assert.Null(store.Warning);
        Assert.Equal(AuthoringSchemaVersions.Preferences, store.Current.SchemaVersion);
        Assert.Equal(AuthoringThemePreference.System, store.Current.ThemePreference);
        Assert.Null(store.Current.LastWorkspace);
        Assert.Null(store.Current.OpenTapHomeOverride);
        Assert.True(store.Current.ShowRawStepXml);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_round_trips_known_fields()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        var store = new AuthoringPreferencesStore(path);
        store.Current.ThemePreference = "dark";
        store.Current.LastWorkspace = "/tmp/ws";
        store.Current.OpenTapHomeOverride = "/opt/opentap";
        store.Current.ShowRawStepXml = false;
        store.Save();

        var reload = new AuthoringPreferencesStore(path);
        reload.Load();
        Assert.Equal(AuthoringThemePreference.Dark, reload.Current.ThemePreference);
        Assert.Equal("/tmp/ws", reload.Current.LastWorkspace);
        Assert.Equal("/opt/opentap", reload.Current.OpenTapHomeOverride);
        Assert.False(reload.Current.ShowRawStepXml);
        Assert.Equal(AuthoringSchemaVersions.Preferences, reload.Current.SchemaVersion);
        Assert.False(reload.IsReadOnly);
    }

    [Fact]
    public void Load_rejects_unknown_property_on_current_schema()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "themePreference": "Light",
              "extraField": true
            }
            """);
        var store = new AuthoringPreferencesStore(path);
        var ex = Assert.Throws<AuthoringPreferencesException>(store.Load);
        Assert.Contains("Unknown property 'extraField'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_missing_schema_version()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        File.WriteAllText(path, """{ "themePreference": "Light" }""");
        var store = new AuthoringPreferencesStore(path);
        var ex = Assert.Throws<AuthoringPreferencesException>(store.Load);
        Assert.Contains("schemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_future_schema_is_read_only_and_save_fails()
    {
        var path = Path.Combine(NewTempDir(), AuthoringPreferencesStore.FileName);
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 999,
              "themePreference": "Dark",
              "futureOnly": "ok"
            }
            """);

        var store = new AuthoringPreferencesStore(path);
        store.Load();
        Assert.True(store.IsReadOnly);
        Assert.Equal(999, store.Current.SchemaVersion);
        Assert.Equal(AuthoringThemePreference.Dark, store.Current.ThemePreference);
        Assert.Contains("read-only", store.Warning, StringComparison.Ordinal);

        var ex = Assert.Throws<AuthoringPreferencesException>(store.Save);
        Assert.Contains("future-schema", ex.Message, StringComparison.Ordinal);
        Assert.Contains("futureOnly", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, AuthoringThemePreference.System)]
    [InlineData("", AuthoringThemePreference.System)]
    [InlineData("nope", AuthoringThemePreference.System)]
    [InlineData("light", AuthoringThemePreference.Light)]
    [InlineData("Dark", AuthoringThemePreference.Dark)]
    public void Normalize_maps_operator_style_theme_strings(string? input, string expected)
        => Assert.Equal(expected, AuthoringThemePreference.Normalize(input));

    [Fact]
    public void DefaultFilePath_is_under_application_data_HardwareTest()
    {
        var path = AuthoringPreferencesStore.DefaultFilePath();
        Assert.EndsWith(
            Path.Combine(AuthoringPreferencesStore.ProductDirectoryName, AuthoringPreferencesStore.FileName),
            path,
            StringComparison.Ordinal);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            path,
            StringComparison.Ordinal);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
