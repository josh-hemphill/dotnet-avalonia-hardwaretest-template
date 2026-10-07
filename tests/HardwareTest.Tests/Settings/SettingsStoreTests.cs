using System.Text.Json;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Settings;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task First_load_uses_defaults_without_requiring_files()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();

        Assert.False(File.Exists(Path.Combine(temp.Path, "settings.json")));
        Assert.True(store.AppSettings.UseMockVisa);
        Assert.Equal("test-report.typ", store.AppSettings.ReportTemplateName);

        await store.SaveAppSettingsAsync();
        await store.SaveUiStateAsync();
        Assert.True(File.Exists(Path.Combine(temp.Path, "settings.json")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "ui-state.json")));
    }

    [Fact]
    public async Task Full_settings_and_ui_state_round_trip()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();

        store.AppSettings.ReportTemplateName = "custom-report.typ";
        store.AppSettings.UseMockVisa = false;
        store.AppSettings.LogMinimumLevel = "Debug";
        store.AppSettings.EnableOsEventSink = true;
        store.AppSettings.EnableSyslogOnUnix = true;
        store.AppSettings.SyslogHost = "10.0.0.1";
        store.AppSettings.SyslogPort = 1514;
        store.AppSettings.PlotRefreshHz = 30;
        store.AppSettings.ThemePreference = "Dark";
        store.AppSettings.EmbedPlotsInReport = false;
        store.AppSettings.PlanSlotOverrides =
        [
            new PlanSlotOverride { PlanId = "sample", SlotName = "Meter", RoleHint = "dmm", Resource = "MOCK::A" },
        ];
        store.AppSettings.PlanParameterOverrides =
        [
            new PlanParameterOverride
            {
                PlanId = "sample",
                MemberKey = "acq/SampleCount",
                Value = "16",
            },
        ];
        store.AppSettings.OperatorSessionIdleMinutes = 37;
        store.AppSettings.OperatorSessionIdleWarnPercent = 90;
        store.UiState.SelectedPageId = "Results";
        store.UiState.Width = 1111;
        store.UiState.IsMaximized = true;
        store.UiState.MonitorDeviceName = "Secondary";
        await store.SaveAppSettingsAsync();
        await store.SaveUiStateAsync();

        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.Equal("custom-report.typ", reload.AppSettings.ReportTemplateName);
        Assert.False(reload.AppSettings.UseMockVisa);
        Assert.Equal("Debug", reload.AppSettings.LogMinimumLevel);
        Assert.True(reload.AppSettings.EnableOsEventSink);
        Assert.Equal(1514, reload.AppSettings.SyslogPort);
        Assert.Equal(30, reload.AppSettings.PlotRefreshHz);
        Assert.Equal("Dark", reload.AppSettings.ThemePreference);
        Assert.False(reload.AppSettings.EmbedPlotsInReport);
        var slot = Assert.Single(reload.AppSettings.PlanSlotOverrides);
        Assert.Equal("sample", slot.PlanId);
        Assert.Equal("Meter", slot.SlotName);
        Assert.Equal("dmm", slot.RoleHint);
        Assert.Equal("MOCK::A", slot.Resource);
        Assert.Equal(37, reload.AppSettings.OperatorSessionIdleMinutes);
        Assert.Equal(90, reload.AppSettings.OperatorSessionIdleWarnPercent);
        Assert.Single(reload.AppSettings.PlanParameterOverrides);
        Assert.Equal("16", reload.AppSettings.PlanParameterOverrides[0].Value);
        Assert.Equal("Results", reload.UiState.SelectedPageId);
        Assert.Equal(1111, reload.UiState.Width);
        Assert.True(reload.UiState.IsMaximized);
        Assert.Equal("Secondary", reload.UiState.MonitorDeviceName);
    }

    [Fact]
    public async Task Removed_registry_hours_and_global_resource_fields_do_not_populate_current_settings()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await File.WriteAllTextAsync(store.SettingsPath,
            $$"""{"schemaVersion":{{SchemaVersions.AppSettings}},"instruments":[{"id":"meter","resource":"MOCK::OLD"}],"stationBindings":[{"role":"dmm","instrumentId":"meter"}],"operatorSessionIdleHours":12,"defaultVisaResource":"MOCK::OLD"}""");

        await store.LoadAsync();

        Assert.Empty(store.AppSettings.PlanSlotOverrides);
        Assert.Empty(store.AppSettings.PlanParameterOverrides);
        Assert.Equal(OperatorSessionIdle.DefaultMinutes, store.AppSettings.OperatorSessionIdleMinutes);
        await store.SaveAppSettingsAsync();
        var saved = await File.ReadAllTextAsync(store.SettingsPath);
        Assert.DoesNotContain("instruments", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stationBindings", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operatorSessionIdleHours", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultVisaResource", saved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 10, OperatorSessionIdle.MinMinutes, OperatorSessionIdle.MinWarnPercent)]
    [InlineData(20000, 100, OperatorSessionIdle.MaxMinutes, OperatorSessionIdle.MaxWarnPercent)]
    public async Task File_minutes_and_warning_threshold_are_normalized(
        int minutes, int warnPercent, int expectedMinutes, int expectedWarnPercent)
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await File.WriteAllTextAsync(store.SettingsPath,
            $"{{\"schemaVersion\":{SchemaVersions.AppSettings},\"operatorSessionIdleMinutes\":{minutes},\"operatorSessionIdleWarnPercent\":{warnPercent}}}");

        await store.LoadAsync();

        Assert.Equal(expectedMinutes, store.AppSettings.OperatorSessionIdleMinutes);
        Assert.Equal(expectedWarnPercent, store.AppSettings.OperatorSessionIdleWarnPercent);
        Assert.Equal(expectedMinutes.ToString(), store.Provenance.Single(
            row => row.Key == nameof(AppSettings.OperatorSessionIdleMinutes)).EffectiveValue);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0,\"operatorSessionIdleMinutes\":1}")]
    [InlineData("{\"schemaVersion\":-1}")]
    [InlineData("{\"schemaVersion\":999,\"themePreference\":\"Light\"}")]
    public async Task Unsupported_or_future_settings_preserve_primary_and_block_autosave(string json)
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var identity = store.AppSettings;
        await File.WriteAllTextAsync(store.SettingsPath, json);
        await File.WriteAllTextAsync(store.SettingsPath + ".bak",
            JsonSerializer.Serialize(new AppSettings { ThemePreference = "Dark" }, AppJsonContext.Default.AppSettings));
        var before = await File.ReadAllBytesAsync(store.SettingsPath);
        var backup = await File.ReadAllBytesAsync(store.SettingsPath + ".bak");
        var warnings = new List<string>();
        await store.LoadAsync(null, new Dictionary<string, string> { [nameof(AppSettings.PlotRefreshHz)] = "30" }, warnings.Add);
        Assert.Same(identity, store.AppSettings);
        Assert.False(store.IsSettingsWritable);
        Assert.NotEmpty(warnings);
        Assert.NotNull(store.SettingsSchemaWarning);
        Assert.Equal(30, store.AppSettings.PlotRefreshHz);
        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();
        Assert.NotNull(store.LastPersistenceError);
        Assert.Equal(before, await File.ReadAllBytesAsync(store.SettingsPath));
        Assert.Equal(backup, await File.ReadAllBytesAsync(store.SettingsPath + ".bak"));
        Assert.DoesNotContain(store.Provenance, row => row.Source == SettingSource.SettingsFile && row.Key == nameof(AppSettings.PlotRefreshHz));
    }

    [Fact]
    public async Task Current_reload_clears_blocked_state_and_keeps_injected_identity()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        var identity = store.AppSettings;
        await File.WriteAllTextAsync(store.SettingsPath, "{}");
        await store.LoadAsync();
        Assert.False(store.IsSettingsWritable);
        await File.WriteAllTextAsync(store.SettingsPath, JsonSerializer.Serialize(
            new AppSettings { ThemePreference = "Dark", OperatorSessionIdleMinutes = 0 }, AppJsonContext.Default.AppSettings));
        await store.LoadAsync();
        Assert.Same(identity, store.AppSettings);
        Assert.True(store.IsSettingsWritable);
        Assert.Null(store.LastPersistenceError);
        Assert.Null(store.SettingsSchemaWarning);
        Assert.Equal(OperatorSessionIdle.MinMinutes, store.AppSettings.OperatorSessionIdleMinutes);
        await store.SaveAppSettingsAsync();
        Assert.True(store.IsSettingsWritable);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":999}")]
    public async Task Ui_state_unsupported_and_future_bytes_are_preserved(string json)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "ui-state.json");
        await File.WriteAllTextAsync(path, json);
        var before = await File.ReadAllBytesAsync(path);
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();
        Assert.NotNull(store.UiStateSchemaWarning);
        store.UiState.SelectedPageId = "Results";
        await store.SaveUiStateAsync();
        Assert.NotNull(store.LastPersistenceError);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new UiState(), AppJsonContext.Default.UiState));
        await store.LoadAsync();
        Assert.Null(store.UiStateSchemaWarning);
        Assert.Null(store.LastPersistenceError);
        store.UiState.SelectedPageId = "Results";
        await store.SaveUiStateAsync();
        Assert.Contains("Results", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupt_settings_recovers_only_current_backup_then_reapplies_overlays()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        store.AppSettings.ThemePreference = "Light";
        await store.SaveAppSettingsAsync();
        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();
        var backup = await File.ReadAllBytesAsync(store.SettingsPath + ".bak");
        await File.WriteAllTextAsync(store.SettingsPath, "{");
        await store.LoadAsync(new Dictionary<string, string> { [nameof(AppSettings.PlotRefreshHz)] = "30" }, null);
        Assert.True(store.IsSettingsWritable);
        Assert.Equal("Light", store.AppSettings.ThemePreference);
        Assert.Equal(30, store.AppSettings.PlotRefreshHz);
        Assert.Equal(backup, await File.ReadAllBytesAsync(store.SettingsPath));
        await store.SaveAppSettingsAsync();
        var loaded = JsonSerializer.Deserialize(await File.ReadAllTextAsync(store.SettingsPath), AppJsonContext.Default.AppSettings)!;
        Assert.Equal(20, loaded.PlotRefreshHz);
    }

    [Fact]
    public async Task Save_rechecks_destination_and_candidate_versions_without_a_load()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await File.WriteAllTextAsync(store.SettingsPath, "{}");
        await store.SaveAppSettingsAsync();
        Assert.False(store.IsSettingsWritable);
        Assert.Equal("{}", await File.ReadAllTextAsync(store.SettingsPath));
        File.Delete(store.SettingsPath);
        store.AppSettings.SchemaVersion = 0;
        await store.SaveAppSettingsAsync();
        Assert.False(store.IsSettingsWritable);
        Assert.False(File.Exists(store.SettingsPath));
    }

}
