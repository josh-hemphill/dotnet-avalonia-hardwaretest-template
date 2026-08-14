using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.Settings;

public interface ISettingsStore
{
    AppSettings AppSettings { get; }
    UiState UiState { get; }
    string RootDirectory { get; }
    string RunsDirectory { get; }
    string SettingsPath { get; }
    IReadOnlyList<SettingProvenance> Provenance { get; }
    bool IsSettingsWritable { get; }
    string? LastPersistenceError { get; }
    string? SettingsSchemaWarning { get; }
    bool IsOverridden(string key);
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task LoadAsync(
        IReadOnlyDictionary<string, string>? environmentOverlays,
        IReadOnlyDictionary<string, string>? commandLineOverlays,
        Action<string>? warn = null,
        CancellationToken cancellationToken = default);
    Task SaveAppSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUiStateAsync(CancellationToken cancellationToken = default);
    /// Raised after a successful settings.json write (and overlay reapply).
    event EventHandler? AppSettingsSaved;
}

/// Loads and saves settings.json / ui-state.json with STJ source generation.
/// Effective values = defaults → settings.json → environment → command line.
public sealed class SettingsStore : ISettingsStore
{
    private readonly string _rootDirectory;
    private readonly string _settingsPath;
    private readonly string _uiStatePath;
    private AppSettings _fileBaseline;
    private List<SettingProvenance> _provenance = [];
    private IReadOnlyDictionary<string, string> _environmentOverlays =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _commandLineOverlays =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool _settingsSchemaReadOnly;
    private bool _uiStateSchemaReadOnly;
    private string? _settingsSchemaWarning;
    private string? _uiStateSchemaWarning;

    public SettingsStore(string? rootDirectory = null, string? settingsFilePath = null)
    {
        _rootDirectory = rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HardwareTest");
        Directory.CreateDirectory(_rootDirectory);
        _settingsPath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? Path.Combine(_rootDirectory, "settings.json")
            : Path.GetFullPath(settingsFilePath);
        _uiStatePath = Path.Combine(_rootDirectory, "ui-state.json");
        _fileBaseline = CreateDefaultAppSettings(_rootDirectory);
        AppSettings = CloneSettings(_fileBaseline);
        UiState = new UiState { SchemaVersion = SchemaVersions.UiState };
        SeedDefaultProvenance();
        IsSettingsWritable = true;
    }

    public AppSettings AppSettings { get; private set; }
    public UiState UiState { get; private set; }
    public string RootDirectory => _rootDirectory;
    public string RunsDirectory => Path.Combine(_rootDirectory, "runs");
    public string SettingsPath => _settingsPath;
    public IReadOnlyList<SettingProvenance> Provenance => _provenance;
    public bool IsSettingsWritable { get; private set; }
    public string? LastPersistenceError { get; private set; }
    /// In-panel warning when settings.json schema is newer than this app.
    public string? SettingsSchemaWarning => _settingsSchemaWarning;
    /// In-panel warning when ui-state.json schema is newer than this app.
    public string? UiStateSchemaWarning => _uiStateSchemaWarning;

    public bool IsOverridden(string key)
        => AppSettingsEnvironmentBinder.IsOverridden(_provenance, key)
           || AppSettingsEnvironmentBinder.IsListOverridden(_provenance, key);

    public Task LoadAsync(CancellationToken cancellationToken = default)
        => LoadAsync(null, null, null, cancellationToken);

    public async Task LoadAsync(
        IReadOnlyDictionary<string, string>? environmentOverlays,
        IReadOnlyDictionary<string, string>? commandLineOverlays,
        Action<string>? warn = null,
        CancellationToken cancellationToken = default)
    {
        _environmentOverlays = environmentOverlays
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _commandLineOverlays = commandLineOverlays
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(RunsDirectory);

        _fileBaseline = CreateDefaultAppSettings(_rootDirectory);
        var provenance = new List<SettingProvenance>();
        SeedDefaultProvenance(provenance, _fileBaseline);

        if (File.Exists(_settingsPath))
        {
            try
            {
                await using var stream = File.OpenRead(_settingsPath);
                var loaded = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonContext.Default.AppSettings,
                    cancellationToken).ConfigureAwait(false);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.DataDirectory))
                    {
                        loaded.DataDirectory = _rootDirectory;
                    }

                    var status = DocumentSchemaGate.Apply(
                        SchemaDocumentTypes.AppSettings,
                        loaded.SchemaVersion,
                        SchemaVersions.AppSettings,
                        _settingsPath,
                        document: loaded);
                    _settingsSchemaReadOnly = status.IsReadOnly;
                    _settingsSchemaWarning = status.IsReadOnly ? status.FormatOperatorWarning() : null;
                    if (status.IsReadOnly)
                    {
                        IsSettingsWritable = false;
                        warn?.Invoke(status.FormatOperatorWarning());
                    }
                    else if (status.Kind is DocumentSchemaKind.Current or DocumentSchemaKind.UpgradeNeeded)
                    {
                        loaded.SchemaVersion = SchemaVersions.AppSettings;
                    }

                    _fileBaseline = loaded;
                    OperatorSessionIdle.NormalizeAfterFileLoad(_fileBaseline);
                    MarkFileProvenance(provenance, _fileBaseline);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warn?.Invoke($"Failed to read settings.json ({ex.Message}); using defaults.");
                LastPersistenceError = ex.Message;
            }
        }

        // Rebuild effective settings into a temp instance, then copy onto the stable identity
        // so DI-injected AppSettings consumers stay live across Load / Save.
        var next = CloneSettings(_fileBaseline);
        AppSettingsEnvironmentBinder.Apply(
            next,
            provenance,
            SettingSource.Environment,
            _environmentOverlays,
            warn);
        AppSettingsEnvironmentBinder.Apply(
            next,
            provenance,
            SettingSource.CommandLine,
            _commandLineOverlays,
            warn);
        NormalizeIdle(next, provenance);
        CopyOnto(next, AppSettings);
        _provenance = provenance;

        if (File.Exists(_uiStatePath))
        {
            try
            {
                await using var stream = File.OpenRead(_uiStatePath);
                var loaded = await JsonSerializer.DeserializeAsync(
                    stream,
                    AppJsonContext.Default.UiState,
                    cancellationToken).ConfigureAwait(false);
                if (loaded is not null)
                {
                    var status = DocumentSchemaGate.Apply(
                        SchemaDocumentTypes.UiState,
                        loaded.SchemaVersion,
                        SchemaVersions.UiState,
                        _uiStatePath,
                        document: loaded);
                    _uiStateSchemaReadOnly = status.IsReadOnly;
                    _uiStateSchemaWarning = status.IsReadOnly ? status.FormatOperatorWarning() : null;
                    if (status.IsReadOnly)
                    {
                        warn?.Invoke(status.FormatOperatorWarning());
                    }
                    else if (status.Kind is DocumentSchemaKind.Current or DocumentSchemaKind.UpgradeNeeded)
                    {
                        loaded.SchemaVersion = SchemaVersions.UiState;
                    }

                    CopyOnto(loaded, UiState);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warn?.Invoke($"Failed to read ui-state.json ({ex.Message}); using defaults.");
            }
        }
    }

    public event EventHandler? AppSettingsSaved;

    public async Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsSchemaReadOnly)
        {
            IsSettingsWritable = false;
            LastPersistenceError = _settingsSchemaWarning
                ?? "settings.json schema is newer than this app; refusing to overwrite.";
            return;
        }

        // Write-back: persist only keys not overridden by env/CLI.
        OperatorSessionIdle.Normalize(AppSettings, preferMinutes: true);
        var toWrite = CloneSettings(_fileBaseline);
        CopyNonOverridden(AppSettings, toWrite);
        // DataDirectory in the file should stay the root we manage unless overridden.
        if (!IsOverridden(nameof(AppSettings.DataDirectory)))
        {
            toWrite.DataDirectory = AppSettings.DataDirectory;
        }

        toWrite.SchemaVersion = SchemaVersions.AppSettings;

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFile.WriteJsonAsync(
                    _settingsPath,
                    toWrite,
                    AppJsonContext.Default.AppSettings,
                    cancellationToken)
                .ConfigureAwait(false);
            _fileBaseline = CloneSettings(toWrite);
            IsSettingsWritable = true;
            LastPersistenceError = null;
            ReapplyOverlays();
            AppSettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsSettingsWritable = false;
            LastPersistenceError = ex.Message;
        }
    }

    public async Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        if (_uiStateSchemaReadOnly)
        {
            LastPersistenceError = _uiStateSchemaWarning
                ?? "ui-state.json schema is newer than this app; refusing to overwrite.";
            return;
        }

        try
        {
            UiState.SchemaVersion = SchemaVersions.UiState;
            await AtomicFile.WriteJsonAsync(
                    _uiStatePath,
                    UiState,
                    AppJsonContext.Default.UiState,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastPersistenceError = ex.Message;
        }
    }

    private void ReapplyOverlays()
    {
        // Rebuild into a temp instance, then copy onto the existing AppSettings identity so
        // DI-injected consumers (retention, export, reports, OpenTAP) see live values.
        var next = CloneSettings(_fileBaseline);
        var provenance = new List<SettingProvenance>();
        SeedDefaultProvenance(provenance, _fileBaseline);
        MarkFileProvenance(provenance, _fileBaseline);
        AppSettingsEnvironmentBinder.Apply(
            next,
            provenance,
            SettingSource.Environment,
            _environmentOverlays);
        AppSettingsEnvironmentBinder.Apply(
            next,
            provenance,
            SettingSource.CommandLine,
            _commandLineOverlays);
        NormalizeIdle(next, provenance);
        CopyOnto(next, AppSettings);
        _provenance = provenance;
    }

    private static void NormalizeIdle(AppSettings settings, List<SettingProvenance> provenance)
    {
        var minutesFromOverlay = provenance.Any(p =>
            string.Equals(p.Key, nameof(AppSettings.OperatorSessionIdleMinutes), StringComparison.OrdinalIgnoreCase)
            && p.Source is SettingSource.Environment or SettingSource.CommandLine);
        var hoursFromOverlay = provenance.Any(p =>
            string.Equals(p.Key, nameof(AppSettings.OperatorSessionIdleHours), StringComparison.OrdinalIgnoreCase)
            && p.Source is SettingSource.Environment or SettingSource.CommandLine);

        if (minutesFromOverlay)
        {
            OperatorSessionIdle.Normalize(settings, preferMinutes: true);
        }
        else if (hoursFromOverlay)
        {
            OperatorSessionIdle.Normalize(settings, preferMinutes: false);
        }
        else
        {
            OperatorSessionIdle.Normalize(settings, preferMinutes: true);
        }
    }

    private void NormalizeIdleAfterOverlays(List<SettingProvenance> provenance)
        => NormalizeIdle(AppSettings, provenance);

    private void CopyNonOverridden(AppSettings from, AppSettings to)
    {
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            if (IsOverridden(binding.Key))
            {
                continue;
            }

            // Re-apply formatted value through the binder for a consistent copy.
            binding.TryApply(to, binding.Format(from), out _, out _);
        }

        if (!IsOverridden("Instruments") && !AppSettingsEnvironmentBinder.IsListOverridden(_provenance, "Instruments"))
        {
            to.Instruments = CloneList(from.Instruments, static i => new VisaInstrument
            {
                Id = i.Id,
                DisplayName = i.DisplayName,
                Resource = i.Resource,
                Enabled = i.Enabled,
                Notes = i.Notes,
            });
        }

        if (!IsOverridden("StationBindings")
            && !AppSettingsEnvironmentBinder.IsListOverridden(_provenance, "StationBindings"))
        {
            to.StationBindings = CloneList(from.StationBindings, static b => new StationBinding
            {
                Role = b.Role,
                InstrumentId = b.InstrumentId,
            });
        }

        if (!IsOverridden("PlanSlotOverrides")
            && !AppSettingsEnvironmentBinder.IsListOverridden(_provenance, "PlanSlotOverrides"))
        {
            to.PlanSlotOverrides = CloneList(from.PlanSlotOverrides, static o => new PlanSlotOverride
            {
                PlanId = o.PlanId,
                SlotName = o.SlotName,
                RoleHint = o.RoleHint,
                Resource = o.Resource,
            });
        }

        if (!IsOverridden("PlanParameterOverrides")
            && !AppSettingsEnvironmentBinder.IsListOverridden(_provenance, "PlanParameterOverrides"))
        {
            to.PlanParameterOverrides = CloneList(from.PlanParameterOverrides, static o => new PlanParameterOverride
            {
                PlanId = o.PlanId,
                MemberKey = o.MemberKey,
                Value = o.Value,
            });
        }
    }

    private void SeedDefaultProvenance()
        => SeedDefaultProvenance(_provenance = [], AppSettings);

    private static void SeedDefaultProvenance(List<SettingProvenance> provenance, AppSettings settings)
    {
        provenance.Clear();
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            provenance.Add(new SettingProvenance
            {
                Key = binding.Key,
                EffectiveValue = binding.Format(settings),
                Source = SettingSource.Default,
                RawValue = null,
                SourceDetail = "built-in default",
            });
        }
    }

    private void MarkFileProvenance(List<SettingProvenance> provenance, AppSettings file)
    {
        foreach (var binding in AppSettingsEnvironmentBinder.Bindings)
        {
            var effective = binding.Format(file);
            var defaults = CreateDefaultAppSettings(_rootDirectory);
            var defaultValue = binding.Format(defaults);
            if (string.Equals(effective, defaultValue, StringComparison.Ordinal))
            {
                continue;
            }

            Upsert(provenance, new SettingProvenance
            {
                Key = binding.Key,
                EffectiveValue = effective,
                Source = SettingSource.SettingsFile,
                RawValue = effective,
                SourceDetail = _settingsPath,
            });
        }
    }

    private static void Upsert(List<SettingProvenance> provenance, SettingProvenance row)
    {
        for (var i = 0; i < provenance.Count; i++)
        {
            if (!string.Equals(provenance[i].Key, row.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            provenance[i] = row;
            return;
        }

        provenance.Add(row);
    }

    private static AppSettings CreateDefaultAppSettings(string root)
    {
        return new AppSettings
        {
            SchemaVersion = SchemaVersions.AppSettings,
            DataDirectory = root,
            DefaultVisaResource = "MOCK::INSTR0",
            UseMockVisa = true,
            ThemePreference = "System",
            EmbedPlotsInReport = true,
            Instruments =
            [
                new VisaInstrument
                {
                    Id = "instr0",
                    DisplayName = "Mock DMM",
                    Resource = "MOCK::INSTR0",
                    Enabled = true,
                },
            ],
            StationBindings =
            [
                new StationBinding { Role = "dmm", InstrumentId = "instr0" },
            ],
        };
    }

    private static AppSettings CloneSettings(AppSettings source)
    {
        // Round-trip through STJ context to deep-clone without reflection.
        var json = JsonSerializer.Serialize(source, AppJsonContext.Default.AppSettings);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)
               ?? CreateDefaultAppSettings(source.DataDirectory);
    }

    /// Copies all settings fields onto <paramref name="target"/> without replacing its identity.
    private static void CopyOnto(AppSettings source, AppSettings target)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.DataDirectory = source.DataDirectory;
        target.DefaultVisaResource = source.DefaultVisaResource;
        target.UseMockVisa = source.UseMockVisa;
        target.LogMinimumLevel = source.LogMinimumLevel;
        target.EnableOsEventSink = source.EnableOsEventSink;
        target.EnableSyslogOnUnix = source.EnableSyslogOnUnix;
        target.SyslogHost = source.SyslogHost;
        target.SyslogPort = source.SyslogPort;
        target.PlotRefreshHz = source.PlotRefreshHz;
        target.ThemePreference = source.ThemePreference;
        target.EmbedPlotsInReport = source.EmbedPlotsInReport;
        target.ExportOpenTapResults = source.ExportOpenTapResults;
        target.ShowDutHistoryOnRun = source.ShowDutHistoryOnRun;
        target.OperatorSessionIdleMinutes = source.OperatorSessionIdleMinutes;
        target.OperatorSessionIdleHours = source.OperatorSessionIdleHours;
        target.OperatorSessionIdleWarnPercent = source.OperatorSessionIdleWarnPercent;
        target.RequireDutConfirmEveryRun = source.RequireDutConfirmEveryRun;
        target.IsEngineerDebugMode = source.IsEngineerDebugMode;
        target.OpenTapPluginDirectories = CloneList(source.OpenTapPluginDirectories, static s => s);
        target.ReportTemplateName = source.ReportTemplateName;
        target.Instruments = CloneList(source.Instruments, static i => new VisaInstrument
        {
            Id = i.Id,
            DisplayName = i.DisplayName,
            Resource = i.Resource,
            Enabled = i.Enabled,
            Notes = i.Notes,
        });
        target.StationBindings = CloneList(source.StationBindings, static b => new StationBinding
        {
            Role = b.Role,
            InstrumentId = b.InstrumentId,
        });
        target.PlanSlotOverrides = CloneList(source.PlanSlotOverrides, static o => new PlanSlotOverride
        {
            PlanId = o.PlanId,
            SlotName = o.SlotName,
            RoleHint = o.RoleHint,
            Resource = o.Resource,
        });
        target.PlanParameterOverrides = CloneList(source.PlanParameterOverrides, static o => new PlanParameterOverride
        {
            PlanId = o.PlanId,
            MemberKey = o.MemberKey,
            Value = o.Value,
        });
        target.CrashEnabled = source.CrashEnabled;
        target.CrashDirectory = source.CrashDirectory;
        target.CrashRetentionCount = source.CrashRetentionCount;
        target.RedactIdentifiersInDiagnostics = source.RedactIdentifiersInDiagnostics;
        target.ExportDirectory = source.ExportDirectory;
        target.PreferRemovableExport = source.PreferRemovableExport;
        target.RunRetentionDays = source.RunRetentionDays;
        target.RunRetentionMaxRuns = source.RunRetentionMaxRuns;
        target.DataFreeSpaceWarnBytes = source.DataFreeSpaceWarnBytes;
        target.DataFreeSpaceCriticalBytes = source.DataFreeSpaceCriticalBytes;
        target.AllowOsFolderBrowse = source.AllowOsFolderBrowse;
        target.OpenTapWorkerKillTimeoutMilliseconds = source.OpenTapWorkerKillTimeoutMilliseconds;
        target.ClockSkewWarnThresholdMinutes = source.ClockSkewWarnThresholdMinutes;
        target.NtpHost = source.NtpHost;
    }

    /// Copies UI state fields onto <paramref name="target"/> without replacing its identity.
    private static void CopyOnto(UiState source, UiState target)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.X = source.X;
        target.Y = source.Y;
        target.Width = source.Width;
        target.Height = source.Height;
        target.NormalX = source.NormalX;
        target.NormalY = source.NormalY;
        target.NormalWidth = source.NormalWidth;
        target.NormalHeight = source.NormalHeight;
        target.IsMaximized = source.IsMaximized;
        target.SelectedPageId = source.SelectedPageId;
        target.MonitorDeviceName = source.MonitorDeviceName;
        target.CompactStepRows = source.CompactStepRows;
    }

    private static List<T> CloneList<T>(IEnumerable<T> source, Func<T, T> clone)
        => source.Select(clone).ToList();
}
