using System.Text.Json;
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
    bool IsOverridden(string key);
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task LoadAsync(
        IReadOnlyDictionary<string, string>? environmentOverlays,
        IReadOnlyDictionary<string, string>? commandLineOverlays,
        Action<string>? warn = null,
        CancellationToken cancellationToken = default);
    Task SaveAppSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUiStateAsync(CancellationToken cancellationToken = default);
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
        UiState = new UiState();
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

                    _fileBaseline = loaded;
                    MarkFileProvenance(provenance, _fileBaseline);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warn?.Invoke($"Failed to read settings.json ({ex.Message}); using defaults.");
                LastPersistenceError = ex.Message;
            }
        }

        AppSettings = CloneSettings(_fileBaseline);
        AppSettingsEnvironmentBinder.Apply(
            AppSettings,
            provenance,
            SettingSource.Environment,
            _environmentOverlays,
            warn);
        AppSettingsEnvironmentBinder.Apply(
            AppSettings,
            provenance,
            SettingSource.CommandLine,
            _commandLineOverlays,
            warn);
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
                    UiState = loaded;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warn?.Invoke($"Failed to read ui-state.json ({ex.Message}); using defaults.");
            }
        }
    }

    public async Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        // Write-back: persist only keys not overridden by env/CLI.
        var toWrite = CloneSettings(_fileBaseline);
        CopyNonOverridden(AppSettings, toWrite);
        // DataDirectory in the file should stay the root we manage unless overridden.
        if (!IsOverridden(nameof(AppSettings.DataDirectory)))
        {
            toWrite.DataDirectory = AppSettings.DataDirectory;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(
                stream,
                toWrite,
                AppJsonContext.Default.AppSettings,
                cancellationToken).ConfigureAwait(false);
            _fileBaseline = CloneSettings(toWrite);
            IsSettingsWritable = true;
            LastPersistenceError = null;
            ReapplyOverlays();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsSettingsWritable = false;
            LastPersistenceError = ex.Message;
        }
    }

    public async Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.Create(_uiStatePath);
            await JsonSerializer.SerializeAsync(
                stream,
                UiState,
                AppJsonContext.Default.UiState,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastPersistenceError = ex.Message;
        }
    }

    private void ReapplyOverlays()
    {
        AppSettings = CloneSettings(_fileBaseline);
        var provenance = new List<SettingProvenance>();
        SeedDefaultProvenance(provenance, _fileBaseline);
        MarkFileProvenance(provenance, _fileBaseline);
        AppSettingsEnvironmentBinder.Apply(
            AppSettings,
            provenance,
            SettingSource.Environment,
            _environmentOverlays);
        AppSettingsEnvironmentBinder.Apply(
            AppSettings,
            provenance,
            SettingSource.CommandLine,
            _commandLineOverlays);
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

    private static List<T> CloneList<T>(IEnumerable<T> source, Func<T, T> clone)
        => source.Select(clone).ToList();
}
