using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Settings;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task First_load_creates_default_files()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();

        Assert.True(File.Exists(Path.Combine(temp.Path, "settings.json")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "ui-state.json")));
        Assert.True(store.AppSettings.UseMockVisa);
        Assert.Equal("MOCK::INSTR0", store.AppSettings.DefaultVisaResource);
    }

    [Fact]
    public async Task Full_settings_and_ui_state_round_trip()
    {
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();

        store.AppSettings.DefaultVisaResource = "MOCK::T1";
        store.AppSettings.UseMockVisa = false;
        store.AppSettings.LogMinimumLevel = "Debug";
        store.AppSettings.EnableOsEventSink = true;
        store.AppSettings.EnableSyslogOnUnix = true;
        store.AppSettings.SyslogHost = "10.0.0.1";
        store.AppSettings.SyslogPort = 1514;
        store.AppSettings.PlotRefreshHz = 30;
        store.AppSettings.ThemePreference = "Dark";
        store.AppSettings.EmbedPlotsInReport = false;
        store.AppSettings.Instruments =
        [
            new VisaInstrument { Id = "a", DisplayName = "A", Resource = "MOCK::A", Enabled = true },
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
        store.UiState.SelectedPageId = "Results";
        store.UiState.Width = 1111;
        store.UiState.IsMaximized = true;
        store.UiState.MonitorDeviceName = "Secondary";
        await store.SaveAppSettingsAsync();
        await store.SaveUiStateAsync();

        var reload = new SettingsStore(temp.Path);
        await reload.LoadAsync();
        Assert.Equal("MOCK::T1", reload.AppSettings.DefaultVisaResource);
        Assert.False(reload.AppSettings.UseMockVisa);
        Assert.Equal("Debug", reload.AppSettings.LogMinimumLevel);
        Assert.True(reload.AppSettings.EnableOsEventSink);
        Assert.Equal(1514, reload.AppSettings.SyslogPort);
        Assert.Equal(30, reload.AppSettings.PlotRefreshHz);
        Assert.Equal("Dark", reload.AppSettings.ThemePreference);
        Assert.False(reload.AppSettings.EmbedPlotsInReport);
        Assert.Single(reload.AppSettings.Instruments);
        Assert.Single(reload.AppSettings.PlanParameterOverrides);
        Assert.Equal("16", reload.AppSettings.PlanParameterOverrides[0].Value);
        Assert.Equal("Results", reload.UiState.SelectedPageId);
        Assert.Equal(1111, reload.UiState.Width);
        Assert.True(reload.UiState.IsMaximized);
        Assert.Equal("Secondary", reload.UiState.MonitorDeviceName);
    }
}
