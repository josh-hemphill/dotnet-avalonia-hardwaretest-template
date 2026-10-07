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
}
