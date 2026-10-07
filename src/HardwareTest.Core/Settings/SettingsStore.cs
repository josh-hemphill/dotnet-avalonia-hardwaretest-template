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
    private bool _settingsWriteBlocked;
    private bool _uiStateWriteBlocked;
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
    /// In-panel warning when settings.json cannot be written.
    public string? SettingsSchemaWarning => _settingsSchemaWarning;
    /// In-panel warning when ui-state.json cannot be written.
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

        _settingsWriteBlocked = false;
        _uiStateWriteBlocked = false;
        _settingsSchemaWarning = null;
        _uiStateSchemaWarning = null;
        CopyOnto(new UiState(), UiState);
        LastPersistenceError = null;
        IsSettingsWritable = true;
        _fileBaseline = CreateDefaultAppSettings(_rootDirectory);
        var provenance = new List<SettingProvenance>();
        SeedDefaultProvenance(provenance, _fileBaseline);

        try
        {
            var (loaded, status) = await CurrentDocumentFile.ReadAsync(_settingsPath, AppJsonContext.Default.AppSettings,
                SchemaDocumentTypes.AppSettings, SchemaVersions.AppSettings, cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                _settingsWriteBlocked = status.IsReadOnly;
                _settingsSchemaWarning = status.IsReadOnly ? status.FormatOperatorWarning() : null;
                if (status.IsReadOnly)
                {
                    IsSettingsWritable = false;
                    warn?.Invoke(_settingsSchemaWarning!);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(loaded.DataDirectory)) loaded.DataDirectory = _rootDirectory;
                    OperatorSessionIdle.Normalize(loaded);
                }
                _fileBaseline = loaded;
                MarkFileProvenance(provenance, _fileBaseline);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or UnsupportedDocumentSchemaException or SchemaReadOnlyException)
        {
            _settingsWriteBlocked = true;
            IsSettingsWritable = false;
            _settingsSchemaWarning = ex.Message;
            LastPersistenceError = ex.Message;
            warn?.Invoke($"Failed to read settings.json ({ex.Message}); using defaults. Saving is blocked.");
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
        OperatorSessionIdle.Normalize(next);
        CopyOnto(next, AppSettings);
        _provenance = provenance;

        try
        {
            var (loaded, status) = await CurrentDocumentFile.ReadAsync(_uiStatePath, AppJsonContext.Default.UiState,
                SchemaDocumentTypes.UiState, SchemaVersions.UiState, cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                _uiStateWriteBlocked = status.IsReadOnly;
                _uiStateSchemaWarning = status.IsReadOnly ? status.FormatOperatorWarning() : null;
                if (status.IsReadOnly) warn?.Invoke(_uiStateSchemaWarning!);
                CopyOnto(loaded, UiState);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or UnsupportedDocumentSchemaException or SchemaReadOnlyException)
        {
            _uiStateWriteBlocked = true;
            _uiStateSchemaWarning = ex.Message;
            LastPersistenceError = ex.Message;
            warn?.Invoke($"Failed to read ui-state.json ({ex.Message}); saving is blocked.");
        }
    }

    public event EventHandler? AppSettingsSaved;

    public async Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsWriteBlocked)
        {
            IsSettingsWritable = false;
            LastPersistenceError = _settingsSchemaWarning
                ?? "settings.json document cannot be written; refusing to overwrite.";
            return;
        }

        // Write-back: persist only keys not overridden by env/CLI.
        OperatorSessionIdle.Normalize(AppSettings);
        var toWrite = CloneSettings(_fileBaseline);
        CopyNonOverridden(AppSettings, toWrite);
        // DataDirectory in the file should stay the root we manage unless overridden.
        if (!IsOverridden(nameof(AppSettings.DataDirectory)))
        {
            toWrite.DataDirectory = AppSettings.DataDirectory;
        }

        try
        {
            DocumentSchemaGate.RequireWritable(SchemaDocumentTypes.AppSettings, AppSettings.SchemaVersion, SchemaVersions.AppSettings, _settingsPath);
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await CurrentDocumentFile.WriteAsync(
                    _settingsPath, toWrite, AppJsonContext.Default.AppSettings,
                    SchemaDocumentTypes.AppSettings, toWrite.SchemaVersion, SchemaVersions.AppSettings, cancellationToken)
                .ConfigureAwait(false);
            _fileBaseline = CloneSettings(toWrite);
            IsSettingsWritable = true;
            LastPersistenceError = null;
            ReapplyOverlays();
            AppSettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or UnsupportedDocumentSchemaException or SchemaReadOnlyException)
        {
            IsSettingsWritable = false;
            LastPersistenceError = ex.Message;
        }
    }

    public async Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        if (_uiStateWriteBlocked)
        {
            LastPersistenceError = _uiStateSchemaWarning
                ?? "ui-state.json document cannot be written; refusing to overwrite.";
            return;
        }

        try
        {
            await CurrentDocumentFile.WriteAsync(
                    _uiStatePath, UiState, AppJsonContext.Default.UiState,
                    SchemaDocumentTypes.UiState, UiState.SchemaVersion, SchemaVersions.UiState, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or UnsupportedDocumentSchemaException or SchemaReadOnlyException)
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
        OperatorSessionIdle.Normalize(next);
        CopyOnto(next, AppSettings);
        _provenance = provenance;
    }

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
            UseMockVisa = true,
            ThemePreference = "System",
            EmbedPlotsInReport = true,
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
        target.OperatorSessionIdleWarnPercent = source.OperatorSessionIdleWarnPercent;
        target.RequireDutConfirmEveryRun = source.RequireDutConfirmEveryRun;
        target.IsEngineerDebugMode = source.IsEngineerDebugMode;
        target.OpenTapPluginDirectories = CloneList(source.OpenTapPluginDirectories, static s => s);
        target.ReportTemplateName = source.ReportTemplateName;
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
        target.StationHealthProfileId = source.StationHealthProfileId;
        target.StationHealthGateOverride = source.StationHealthGateOverride;
        target.UseMockOperatorCredential = source.UseMockOperatorCredential;
        target.Pkcs11LibraryPath = source.Pkcs11LibraryPath;
        target.RequireCredentialForOperator = source.RequireCredentialForOperator;
        target.RequireAttestationBeforeExport = source.RequireAttestationBeforeExport;
        target.AllowPresenceInLieuOfSigning = source.AllowPresenceInLieuOfSigning;
        target.ProbeBadgeWhenTechnicianFocused = source.ProbeBadgeWhenTechnicianFocused;
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
