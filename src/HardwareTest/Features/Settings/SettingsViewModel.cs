using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public sealed class SettingProvenanceRow
{
    public required string Key { get; init; }
    public required string EffectiveValue { get; init; }
    public required string Source { get; init; }
    public required string SourceDetail { get; init; }
}

public partial class SettingsViewModel : ReactiveObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IOpenTapSession _openTap;
    private readonly BuildInfo _buildInfo;
    private readonly OperatorSession? _operatorSession;
    private readonly IVisaModeController? _visaModeController;
    private readonly System.Timers.Timer _debounce;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        IOpenTapSession openTap,
        BuildInfo? buildInfo = null,
        OperatorSession? operatorSession = null,
        IVisaModeController? visaModeController = null)
    {
        _settingsStore = settingsStore;
        _openTap = openTap;
        _operatorSession = operatorSession;
        _visaModeController = visaModeController;
        _buildInfo = buildInfo ?? BuildInfo.FromAssembly(typeof(SettingsViewModel).Assembly);
        var s = settingsStore.AppSettings;
        // Prefer the live factory mode so the checkbox never claims a mode the DI façade is not using.
        UseMockVisa = visaModeController?.EffectiveUseMockVisa ?? s.UseMockVisa;
        LogMinimumLevel = NormalizeLogLevel(s.LogMinimumLevel);
        EnableOsEventSink = s.EnableOsEventSink;
        EnableSyslogOnUnix = s.EnableSyslogOnUnix;
        SyslogHost = s.SyslogHost ?? "127.0.0.1";
        SyslogPort = s.SyslogPort;
        PlotRefreshHz = s.PlotRefreshHz;
        ThemePreference = string.IsNullOrWhiteSpace(s.ThemePreference) ? "System" : s.ThemePreference;
        EmbedPlotsInReport = s.EmbedPlotsInReport;
        ExportOpenTapResults = s.ExportOpenTapResults;
        ShowDutHistoryOnRun = s.ShowDutHistoryOnRun;
        IsEngineerDebugMode = s.IsEngineerDebugMode;
        OperatorSessionIdleMinutes = OperatorSessionIdle.ClampMinutes(
            s.OperatorSessionIdleMinutes > 0
                ? s.OperatorSessionIdleMinutes
                : OperatorSessionIdle.HoursToMinutes(s.OperatorSessionIdleHours));
        OperatorSessionIdleWarnPercent = OperatorSessionIdle.ClampWarnPercent(s.OperatorSessionIdleWarnPercent);
        RequireDutConfirmEveryRun = s.RequireDutConfirmEveryRun;
        RunRetentionDays = s.RunRetentionDays;
        RunRetentionMaxRuns = s.RunRetentionMaxRuns;
        ExportDirectory = s.ExportDirectory ?? string.Empty;
        DataFreeSpaceWarnGb = BytesToGb(s.DataFreeSpaceWarnBytes);
        DataFreeSpaceCriticalGb = BytesToGb(s.DataFreeSpaceCriticalBytes);
        DataDirectory = settingsStore.RootDirectory;
        AllowOsFolderBrowse = s.AllowOsFolderBrowse || s.IsEngineerDebugMode;
        Status = settingsStore.IsSettingsWritable
            ? "Settings load from settings.json; env/CLI overlays win and stay read-only."
            : $"Settings file not writable: {settingsStore.LastPersistenceError}";
        if (!string.IsNullOrWhiteSpace(settingsStore.SettingsSchemaWarning))
        {
            Status = settingsStore.SettingsSchemaWarning;
        }
        ThemeOptions = ["System", "Light", "Dark"];
        LogLevelOptions = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        ShowEventLogOptions = OperatingSystem.IsWindows();
        ShowSyslogOptions = !OperatingSystem.IsWindows();
        Packages = [];
        PluginDirectories = [];
        ProvenanceRows = [];

        UseMockVisaReadOnly = settingsStore.IsOverridden(nameof(AppSettings.UseMockVisa));
        LogMinimumLevelReadOnly = settingsStore.IsOverridden(nameof(AppSettings.LogMinimumLevel));
        EnableOsEventSinkReadOnly = settingsStore.IsOverridden(nameof(AppSettings.EnableOsEventSink));
        EnableSyslogOnUnixReadOnly = settingsStore.IsOverridden(nameof(AppSettings.EnableSyslogOnUnix));
        SyslogHostReadOnly = settingsStore.IsOverridden(nameof(AppSettings.SyslogHost));
        SyslogPortReadOnly = settingsStore.IsOverridden(nameof(AppSettings.SyslogPort));
        PlotRefreshHzReadOnly = settingsStore.IsOverridden(nameof(AppSettings.PlotRefreshHz));
        ThemePreferenceReadOnly = settingsStore.IsOverridden(nameof(AppSettings.ThemePreference));
        EmbedPlotsInReportReadOnly = settingsStore.IsOverridden(nameof(AppSettings.EmbedPlotsInReport));
        ExportOpenTapResultsReadOnly = settingsStore.IsOverridden(nameof(AppSettings.ExportOpenTapResults));
        ShowDutHistoryOnRunReadOnly = settingsStore.IsOverridden(nameof(AppSettings.ShowDutHistoryOnRun));
        IsEngineerDebugModeReadOnly = settingsStore.IsOverridden(nameof(AppSettings.IsEngineerDebugMode));
        OperatorSessionIdleMinutesReadOnly =
            settingsStore.IsOverridden(nameof(AppSettings.OperatorSessionIdleMinutes))
            || settingsStore.IsOverridden(nameof(AppSettings.OperatorSessionIdleHours));
        OperatorSessionIdleWarnPercentReadOnly =
            settingsStore.IsOverridden(nameof(AppSettings.OperatorSessionIdleWarnPercent));
        RequireDutConfirmEveryRunReadOnly =
            settingsStore.IsOverridden(nameof(AppSettings.RequireDutConfirmEveryRun));
        RunRetentionDaysReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RunRetentionDays));
        RunRetentionMaxRunsReadOnly = settingsStore.IsOverridden(nameof(AppSettings.RunRetentionMaxRuns));
        ExportDirectoryReadOnly = settingsStore.IsOverridden(nameof(AppSettings.ExportDirectory));
        DataFreeSpaceWarnGbReadOnly = settingsStore.IsOverridden(nameof(AppSettings.DataFreeSpaceWarnBytes));
        DataFreeSpaceCriticalGbReadOnly = settingsStore.IsOverridden(nameof(AppSettings.DataFreeSpaceCriticalBytes));

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        RefreshPackagesCommand = ReactiveCommand.Create(RefreshPackages);
        CopyPathCommand = ReactiveCommand.CreateFromTask(CopySelectedPathAsync);
        OpenFolderCommand = ReactiveCommand.Create(OpenSelectedFolder);
        OpenCrashesFolderCommand = ReactiveCommand.Create(OpenCrashesFolder);
        CopyDiagnosticsCommand = ReactiveCommand.CreateFromTask(CopyDiagnosticsAsync);

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
                or nameof(Packages) or nameof(PluginDirectories)
                or nameof(ProvenanceRows)
                or nameof(AboutVersion) or nameof(AboutCommit) or nameof(AboutBuildTimestamp)
                or nameof(AboutRuntime) or nameof(AboutRuntimeIdentifier) or nameof(AboutOpenTapEngine)
                or nameof(UseMockVisaReadOnly) or nameof(LogMinimumLevelReadOnly)
                or nameof(EnableOsEventSinkReadOnly) or nameof(EnableSyslogOnUnixReadOnly)
                or nameof(SyslogHostReadOnly) or nameof(SyslogPortReadOnly)
                or nameof(PlotRefreshHzReadOnly) or nameof(ThemePreferenceReadOnly)
                or nameof(EmbedPlotsInReportReadOnly) or nameof(ExportOpenTapResultsReadOnly)
                or nameof(ShowDutHistoryOnRunReadOnly) or nameof(IsEngineerDebugModeReadOnly)
                or nameof(OperatorSessionIdleMinutesReadOnly)
                or nameof(OperatorSessionIdleWarnPercentReadOnly)
                or nameof(RequireDutConfirmEveryRunReadOnly)
                or nameof(RunRetentionDaysReadOnly) or nameof(RunRetentionMaxRunsReadOnly)
                or nameof(ExportDirectoryReadOnly)
                or nameof(DataFreeSpaceWarnGbReadOnly) or nameof(DataFreeSpaceCriticalGbReadOnly))
            {
                return;
            }

            if (IsPropertyOverridden(args.PropertyName))
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        };

        RefreshPackages();
        RefreshProvenance();
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshPackagesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CopyPathCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenFolderCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCrashesFolderCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CopyDiagnosticsCommand { get; }
    public ObservableCollection<string> ThemeOptions { get; }
    public ObservableCollection<string> LogLevelOptions { get; }
    public ObservableCollection<OpenTapPackageInfo> Packages { get; }
    public ObservableCollection<OpenTapPluginDirectoryInfo> PluginDirectories { get; }
    public ObservableCollection<SettingProvenanceRow> ProvenanceRows { get; }
    public bool ShowEventLogOptions { get; }
    public bool ShowSyslogOptions { get; }

    public string AboutVersion => _buildInfo.InformationalVersion;
    public string AboutCommit => _buildInfo.CommitSha;
    public string AboutBuildTimestamp =>
        _buildInfo.BuildTimestampUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown";
    public string AboutRuntime => _buildInfo.RuntimeVersion;
    public string AboutRuntimeIdentifier => _buildInfo.RuntimeIdentifier;
    public string AboutOpenTapEngine => _buildInfo.OpenTapEngineVersion ?? "n/a";

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
    [Reactive] private bool _showDutHistoryOnRun;
    [Reactive] private bool _isEngineerDebugMode;
    [Reactive] private bool _allowOsFolderBrowse;
    [Reactive] private int _operatorSessionIdleMinutes = 240;
    [Reactive] private int _operatorSessionIdleWarnPercent = 80;
    [Reactive] private bool _requireDutConfirmEveryRun;
    [Reactive] private int _runRetentionDays = 30;
    [Reactive] private int _runRetentionMaxRuns = 500;
    [Reactive] private string _exportDirectory = string.Empty;
    [Reactive] private double _dataFreeSpaceWarnGb = 2;
    [Reactive] private double _dataFreeSpaceCriticalGb = 0.5;
    [Reactive] private string _dataDirectory = string.Empty;
    [Reactive] private string _status = string.Empty;
    [Reactive] private OpenTapPackageInfo? _selectedPackage;
    [Reactive] private OpenTapPluginDirectoryInfo? _selectedPluginDirectory;
    [Reactive] private bool _useMockVisaReadOnly;
    [Reactive] private bool _logMinimumLevelReadOnly;
    [Reactive] private bool _enableOsEventSinkReadOnly;
    [Reactive] private bool _enableSyslogOnUnixReadOnly;
    [Reactive] private bool _syslogHostReadOnly;
    [Reactive] private bool _syslogPortReadOnly;
    [Reactive] private bool _plotRefreshHzReadOnly;
    [Reactive] private bool _themePreferenceReadOnly;
    [Reactive] private bool _embedPlotsInReportReadOnly;
    [Reactive] private bool _exportOpenTapResultsReadOnly;
    [Reactive] private bool _showDutHistoryOnRunReadOnly;
    [Reactive] private bool _isEngineerDebugModeReadOnly;
    [Reactive] private bool _operatorSessionIdleMinutesReadOnly;
    [Reactive] private bool _operatorSessionIdleWarnPercentReadOnly;
    [Reactive] private bool _requireDutConfirmEveryRunReadOnly;
    [Reactive] private bool _runRetentionDaysReadOnly;
    [Reactive] private bool _runRetentionMaxRunsReadOnly;
    [Reactive] private bool _exportDirectoryReadOnly;
    [Reactive] private bool _dataFreeSpaceWarnGbReadOnly;
    [Reactive] private bool _dataFreeSpaceCriticalGbReadOnly;

    private void RefreshProvenance()
    {
        ProvenanceRows.Clear();
        foreach (var row in _settingsStore.Provenance.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            ProvenanceRows.Add(new SettingProvenanceRow
            {
                Key = row.Key,
                EffectiveValue = row.EffectiveValue,
                Source = row.Source.ToString(),
                SourceDetail = row.SourceDetail ?? string.Empty,
            });
        }
    }

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

    private async Task CopyDiagnosticsAsync()
    {
        if (CopyTextAsync is null)
        {
            Status = "Clipboard is not available in this host.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(_buildInfo.FormatSupportBlock(DataDirectory));
        sb.AppendLine();
        sb.AppendLine("Key\tEffectiveValue\tSource\tSourceDetail");
        foreach (var row in ProvenanceRows)
        {
            sb.Append(row.Key).Append('\t')
                .Append(row.EffectiveValue).Append('\t')
                .Append(row.Source).Append('\t')
                .Append(row.SourceDetail).AppendLine();
        }

        try
        {
            await CopyTextAsync(sb.ToString());
            Status = "Copied diagnostics support block.";
        }
        catch (Exception ex)
        {
            Status = $"Copy failed: {ex.Message}";
        }
    }

    private void OpenSelectedFolder()
    {
        if (!AllowOsFolderBrowse)
        {
            Status = "Open folder is disabled. Enable AllowOsFolderBrowse or Engineer debug, or use Copy path.";
            return;
        }

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

    private void OpenCrashesFolder()
    {
        if (!AllowOsFolderBrowse)
        {
            Status = "Open folder is disabled. Use Copy path or export a support bundle from Home.";
            return;
        }

        try
        {
            var root = string.IsNullOrWhiteSpace(_settingsStore.AppSettings.CrashDirectory)
                ? Path.Combine(_settingsStore.RootDirectory, "crashes")
                : _settingsStore.AppSettings.CrashDirectory;
            Directory.CreateDirectory(root);
            OpenFolder(root);
            Status = $"Opened crashes: {root}";
        }
        catch (Exception ex)
        {
            Status = $"Open crashes folder failed: {ex.Message}";
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
        _operatorSession?.TouchActivity();
        var s = _settingsStore.AppSettings;
        string? visaApplyStatus = null;
        var visaRefused = false;
        if (!UseMockVisaReadOnly)
        {
            if (_visaModeController is not null && UseMockVisa != _visaModeController.EffectiveUseMockVisa)
            {
                if (_visaModeController.TryApply(UseMockVisa, out var applyMsg))
                {
                    visaApplyStatus = applyMsg;
                    s.UseMockVisa = UseMockVisa;
                }
                else
                {
                    visaRefused = true;
                    visaApplyStatus = applyMsg;
                    var effective = _visaModeController.EffectiveUseMockVisa;
                    s.UseMockVisa = effective;
                    UseMockVisa = effective;
                }
            }
            else
            {
                s.UseMockVisa = UseMockVisa;
            }
        }

        if (!LogMinimumLevelReadOnly)
        {
            s.LogMinimumLevel = NormalizeLogLevel(LogMinimumLevel);
        }

        if (!EnableOsEventSinkReadOnly)
        {
            s.EnableOsEventSink = EnableOsEventSink;
        }

        if (!EnableSyslogOnUnixReadOnly)
        {
            s.EnableSyslogOnUnix = EnableSyslogOnUnix;
        }

        if (!SyslogHostReadOnly)
        {
            s.SyslogHost = SyslogHost;
        }

        if (!SyslogPortReadOnly)
        {
            s.SyslogPort = SyslogPort;
        }

        if (!PlotRefreshHzReadOnly)
        {
            s.PlotRefreshHz = PlotRefreshHz;
        }

        if (!ThemePreferenceReadOnly)
        {
            s.ThemePreference = ThemePreference;
        }

        if (!EmbedPlotsInReportReadOnly)
        {
            s.EmbedPlotsInReport = EmbedPlotsInReport;
        }

        if (!ExportOpenTapResultsReadOnly)
        {
            s.ExportOpenTapResults = ExportOpenTapResults;
        }

        if (!ShowDutHistoryOnRunReadOnly)
        {
            s.ShowDutHistoryOnRun = ShowDutHistoryOnRun;
        }

        if (!IsEngineerDebugModeReadOnly)
        {
            s.IsEngineerDebugMode = IsEngineerDebugMode;
        }

        if (!OperatorSessionIdleMinutesReadOnly)
        {
            s.OperatorSessionIdleMinutes = OperatorSessionIdle.ClampMinutes(OperatorSessionIdleMinutes);
            s.OperatorSessionIdleHours = OperatorSessionIdle.MinutesToHoursDisplay(s.OperatorSessionIdleMinutes);
        }

        if (!OperatorSessionIdleWarnPercentReadOnly)
        {
            s.OperatorSessionIdleWarnPercent =
                OperatorSessionIdle.ClampWarnPercent(OperatorSessionIdleWarnPercent);
        }

        if (!RequireDutConfirmEveryRunReadOnly)
        {
            s.RequireDutConfirmEveryRun = RequireDutConfirmEveryRun;
        }

        if (!RunRetentionDaysReadOnly)
        {
            s.RunRetentionDays = Math.Max(0, RunRetentionDays);
        }

        if (!RunRetentionMaxRunsReadOnly)
        {
            s.RunRetentionMaxRuns = Math.Max(0, RunRetentionMaxRuns);
        }

        if (!ExportDirectoryReadOnly)
        {
            s.ExportDirectory = ExportDirectory?.Trim() ?? string.Empty;
        }

        if (!DataFreeSpaceWarnGbReadOnly)
        {
            s.DataFreeSpaceWarnBytes = GbToBytes(DataFreeSpaceWarnGb);
        }

        if (!DataFreeSpaceCriticalGbReadOnly)
        {
            s.DataFreeSpaceCriticalBytes = GbToBytes(DataFreeSpaceCriticalGb);
        }

        await _settingsStore.SaveAppSettingsAsync();
        ThemeApplier.Apply(s);
        AllowOsFolderBrowse = s.AllowOsFolderBrowse || s.IsEngineerDebugMode;
        RefreshProvenance();
        if (!_settingsStore.IsSettingsWritable)
        {
            Status = $"Could not write settings.json ({_settingsStore.LastPersistenceError}). Continuing in memory.";
            return;
        }

        if (visaRefused && visaApplyStatus is not null)
        {
            Status = visaApplyStatus;
        }
        else if (visaApplyStatus is not null)
        {
            Status = $"Saved at {DateTimeOffset.Now:T}. {visaApplyStatus}";
        }
        else
        {
            Status = $"Saved at {DateTimeOffset.Now:T}. Restart may be required for logging sink changes.";
        }
    }

    private static double BytesToGb(long bytes)
        => Math.Round(bytes / (1024d * 1024d * 1024d), 2, MidpointRounding.AwayFromZero);

    private static long GbToBytes(double gb)
        => (long)Math.Round(Math.Max(0, gb) * 1024d * 1024d * 1024d, MidpointRounding.AwayFromZero);

    private bool IsPropertyOverridden(string? propertyName)
        => propertyName switch
        {
            nameof(UseMockVisa) => UseMockVisaReadOnly,
            nameof(LogMinimumLevel) => LogMinimumLevelReadOnly,
            nameof(EnableOsEventSink) => EnableOsEventSinkReadOnly,
            nameof(EnableSyslogOnUnix) => EnableSyslogOnUnixReadOnly,
            nameof(SyslogHost) => SyslogHostReadOnly,
            nameof(SyslogPort) => SyslogPortReadOnly,
            nameof(PlotRefreshHz) => PlotRefreshHzReadOnly,
            nameof(ThemePreference) => ThemePreferenceReadOnly,
            nameof(EmbedPlotsInReport) => EmbedPlotsInReportReadOnly,
            nameof(ExportOpenTapResults) => ExportOpenTapResultsReadOnly,
            nameof(ShowDutHistoryOnRun) => ShowDutHistoryOnRunReadOnly,
            nameof(IsEngineerDebugMode) => IsEngineerDebugModeReadOnly,
            nameof(OperatorSessionIdleMinutes) => OperatorSessionIdleMinutesReadOnly,
            nameof(OperatorSessionIdleWarnPercent) => OperatorSessionIdleWarnPercentReadOnly,
            nameof(RequireDutConfirmEveryRun) => RequireDutConfirmEveryRunReadOnly,
            nameof(RunRetentionDays) => RunRetentionDaysReadOnly,
            nameof(RunRetentionMaxRuns) => RunRetentionMaxRunsReadOnly,
            nameof(ExportDirectory) => ExportDirectoryReadOnly,
            nameof(DataFreeSpaceWarnGb) => DataFreeSpaceWarnGbReadOnly,
            nameof(DataFreeSpaceCriticalGb) => DataFreeSpaceCriticalGbReadOnly,
            _ => false,
        };

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
