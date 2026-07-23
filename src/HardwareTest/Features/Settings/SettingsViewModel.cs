using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel : ReactiveObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IOpenTapSession _openTap;
    private readonly System.Timers.Timer _debounce;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        IOpenTapSession openTap)
    {
        _settingsStore = settingsStore;
        _openTap = openTap;
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
        ExportOpenTapResults = s.ExportOpenTapResults;
        IsEngineerDebugMode = s.IsEngineerDebugMode;
        OperatorSessionIdleHours = s.OperatorSessionIdleHours;
        DataDirectory = settingsStore.RootDirectory;
        Status = "Settings load from settings.json under ApplicationData/HardwareTest.";
        ThemeOptions = ["System", "Light", "Dark"];
        LogLevelOptions = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        ShowEventLogOptions = OperatingSystem.IsWindows();
        ShowSyslogOptions = !OperatingSystem.IsWindows();
        Packages = [];
        PluginDirectories = [];

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        RefreshPackagesCommand = ReactiveCommand.Create(RefreshPackages);
        CopyPathCommand = ReactiveCommand.CreateFromTask(CopySelectedPathAsync);
        OpenFolderCommand = ReactiveCommand.Create(OpenSelectedFolder);

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
            if (args.PropertyName == nameof(SelectedPackage) && SelectedPackage is not null)
            {
                SelectedPluginDirectory = null;
                return;
            }

            if (args.PropertyName == nameof(SelectedPluginDirectory) && SelectedPluginDirectory is not null)
            {
                SelectedPackage = null;
                return;
            }

            if (args.PropertyName is nameof(Status) or nameof(DataDirectory)
                or nameof(ShowEventLogOptions) or nameof(ShowSyslogOptions)
                or nameof(SelectedPackage) or nameof(SelectedPluginDirectory)
                or nameof(Packages) or nameof(PluginDirectories))
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        };

        RefreshPackages();
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshPackagesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CopyPathCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenFolderCommand { get; }
    public ObservableCollection<string> ThemeOptions { get; }
    public ObservableCollection<string> LogLevelOptions { get; }
    public ObservableCollection<OpenTapPackageInfo> Packages { get; }
    public ObservableCollection<OpenTapPluginDirectoryInfo> PluginDirectories { get; }
    public bool ShowEventLogOptions { get; }
    public bool ShowSyslogOptions { get; }

    /// Optional clipboard hook (wired from the view); null means Copy shows a status message.
    public Func<string, Task>? CopyTextAsync { get; set; }

    [Reactive] private bool _useMockVisa;
    [Reactive] private string _logMinimumLevel = "Information";
    [Reactive] private bool _enableOsEventSink;
    [Reactive] private bool _enableSyslogOnUnix;
    [Reactive] private string _syslogHost = "127.0.0.1";
    [Reactive] private int _syslogPort = 514;
    [Reactive] private int _plotRefreshHz = 20;
    [Reactive] private string _themePreference = "System";
    [Reactive] private bool _embedPlotsInReport = true;
    [Reactive] private bool _exportOpenTapResults;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private int _operatorSessionIdleHours = 4;
    [Reactive] private string _dataDirectory = string.Empty;
    [Reactive] private string _status = string.Empty;
    [Reactive] private OpenTapPackageInfo? _selectedPackage;
    [Reactive] private OpenTapPluginDirectoryInfo? _selectedPluginDirectory;

    private void RefreshPackages()
    {
        Packages.Clear();
        foreach (var pkg in _openTap.ListInstalledPackages())
        {
            Packages.Add(pkg);
        }

        PluginDirectories.Clear();
        foreach (var dir in _openTap.ListPluginDirectories())
        {
            PluginDirectories.Add(dir);
        }

        Status = $"Packages: {Packages.Count}, plugin dirs: {PluginDirectories.Count}. Offline install only (CLI / image bake).";
    }

    private async Task CopySelectedPathAsync()
    {
        var path = ResolveSelectedPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Select a package or plugin directory first.";
            return;
        }

        if (CopyTextAsync is null)
        {
            Status = "Clipboard is not available in this host.";
            return;
        }

        try
        {
            await CopyTextAsync(path);
            Status = $"Copied path: {path}";
        }
        catch (Exception ex)
        {
            Status = $"Copy failed: {ex.Message}";
        }
    }

    private void OpenSelectedFolder()
    {
        var path = ResolveSelectedPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Select a package or plugin directory first.";
            return;
        }

        try
        {
            OpenFolder(path);
            Status = $"Opened: {path}";
        }
        catch (Exception ex)
        {
            Status = $"Open folder failed (locked appliance?): {ex.Message}";
        }
    }

    private string? ResolveSelectedPath()
        => SelectedPackage?.Path ?? SelectedPluginDirectory?.Path;

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

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
        s.ExportOpenTapResults = ExportOpenTapResults;
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
