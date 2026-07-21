using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using HardwareTest.Core.Settings;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel : ReactiveObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly System.Timers.Timer _debounce;

    public SettingsViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var s = settingsStore.AppSettings;
        UseMockVisa = s.UseMockVisa;
        LogMinimumLevel = NormalizeLogLevel(s.LogMinimumLevel);
        EnableOsEventSink = s.EnableOsEventSink;
        EnableSyslogOnUnix = s.EnableSyslogOnUnix;
        SyslogHost = s.SyslogHost ?? "127.0.0.1";
        SyslogPort = s.SyslogPort;
        PlotRefreshHz = s.PlotRefreshHz;
        ThemePreference = string.IsNullOrWhiteSpace(s.ThemePreference) ? "System" : s.ThemePreference;
        EmbedPlotsInReport = s.EmbedPlotsInReport;
        IsEngineerDebugMode = s.IsEngineerDebugMode;
        OperatorSessionIdleHours = s.OperatorSessionIdleHours;
        DataDirectory = settingsStore.RootDirectory;
        Status = "Settings load from settings.json under ApplicationData/HardwareTest.";
        ThemeOptions = ["System", "Light", "Dark"];
        LogLevelOptions = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        ShowEventLogOptions = OperatingSystem.IsWindows();
        ShowSyslogOptions = !OperatingSystem.IsWindows();

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        _debounce = new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed += async (_, _) =>
        {
            try
            {
                await SaveAsync();
            }
            catch
            {
                // ignore debounce errors
            }
        };

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Status) or nameof(DataDirectory)
                or nameof(ShowEventLogOptions) or nameof(ShowSyslogOptions))
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        };
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ObservableCollection<string> ThemeOptions { get; }
    public ObservableCollection<string> LogLevelOptions { get; }
    public bool ShowEventLogOptions { get; }
    public bool ShowSyslogOptions { get; }

    [Reactive] private bool _useMockVisa;
    [Reactive] private string _logMinimumLevel = "Information";
    [Reactive] private bool _enableOsEventSink;
    [Reactive] private bool _enableSyslogOnUnix;
    [Reactive] private string _syslogHost = "127.0.0.1";
    [Reactive] private int _syslogPort = 514;
    [Reactive] private int _plotRefreshHz = 20;
    [Reactive] private string _themePreference = "System";
    [Reactive] private bool _embedPlotsInReport = true;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private int _operatorSessionIdleHours = 4;
    [Reactive] private string _dataDirectory = string.Empty;
    [Reactive] private string _status = string.Empty;

    private async Task SaveAsync()
    {
        var s = _settingsStore.AppSettings;
        s.UseMockVisa = UseMockVisa;
        s.LogMinimumLevel = NormalizeLogLevel(LogMinimumLevel);
        s.EnableOsEventSink = EnableOsEventSink;
        s.EnableSyslogOnUnix = EnableSyslogOnUnix;
        s.SyslogHost = SyslogHost;
        s.SyslogPort = SyslogPort;
        s.PlotRefreshHz = PlotRefreshHz;
        s.ThemePreference = ThemePreference;
        s.EmbedPlotsInReport = EmbedPlotsInReport;
        s.IsEngineerDebugMode = IsEngineerDebugMode;
        s.OperatorSessionIdleHours = Math.Clamp(OperatorSessionIdleHours, 1, 168);
        await _settingsStore.SaveAppSettingsAsync();
        ThemeApplier.Apply(s);
        Status = $"Saved at {DateTimeOffset.Now:T}. Restart may be required for logging sink / mock VISA changes.";
    }

    private static string NormalizeLogLevel(string? level)
    {
        var value = string.IsNullOrWhiteSpace(level) ? "Information" : level.Trim();
        return value switch
        {
            "Verbose" or "Debug" or "Information" or "Warning" or "Error" or "Fatal" => value,
            _ => "Information",
        };
    }
}
