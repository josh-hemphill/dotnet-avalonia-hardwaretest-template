using System.Text.Json;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Settings;

public sealed class BenchReferenceSettingsRecoveryTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task Malformed_saved_choices_preserve_bytes_and_recover_after_corrected_reload(string choices)
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var identity = store.AppSettings;
        var bad = $"{{\"schemaVersion\":{SchemaVersions.AppSettings},\"benchReferenceSelections\":{choices}}}";
        await File.WriteAllTextAsync(store.SettingsPath, bad);
        var original = await File.ReadAllBytesAsync(store.SettingsPath);
        var warnings = new List<string>();
        await store.LoadAsync(null, null, warnings.Add);
        Assert.Same(identity, store.AppSettings);
        Assert.Empty(store.AppSettings.BenchReferenceSelections);
        Assert.False(store.IsSettingsWritable);
        Assert.Contains(warnings, warning => warning.Contains("golden-reference choices", StringComparison.Ordinal));
        await store.SaveAppSettingsAsync();
        Assert.NotNull(store.LastPersistenceError);
        Assert.Equal(original, await File.ReadAllBytesAsync(store.SettingsPath));

        var corrected = new AppSettings
        {
            ThemePreference = "Dark",
            BenchReferenceSelections = [new() { PlanId = "measured-plan", RuleId = "voltage", ReferenceId = Guid.NewGuid().ToString("N") }],
        };
        await File.WriteAllTextAsync(store.SettingsPath, JsonSerializer.Serialize(corrected, AppJsonContext.Default.AppSettings));
        await store.LoadAsync();
        Assert.Same(identity, store.AppSettings);
        Assert.True(store.IsSettingsWritable);
        Assert.Null(store.LastPersistenceError);
        Assert.Equal("Dark", store.AppSettings.ThemePreference);
        Assert.Equal("voltage", Assert.Single(store.AppSettings.BenchReferenceSelections).RuleId);
        await store.SaveAppSettingsAsync();
        Assert.True(store.IsSettingsWritable);
    }

    [Fact]
    public async Task Invalid_in_memory_choices_do_not_overwrite_a_valid_saved_configuration()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.SaveAppSettingsAsync();
        var original = await File.ReadAllBytesAsync(store.SettingsPath);
        store.AppSettings.BenchReferenceSelections = null!;
        await store.SaveAppSettingsAsync();
        Assert.False(store.IsSettingsWritable);
        Assert.Equal(original, await File.ReadAllBytesAsync(store.SettingsPath));
        store.AppSettings.BenchReferenceSelections = [];
        await store.SaveAppSettingsAsync();
        Assert.True(store.IsSettingsWritable);
    }
}
