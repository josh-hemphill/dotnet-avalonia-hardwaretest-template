using System.Text.Json;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.Settings;

public interface ISettingsStore
{
    AppSettings AppSettings { get; }
    UiState UiState { get; }
    string RootDirectory { get; }
    string RunsDirectory { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAppSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUiStateAsync(CancellationToken cancellationToken = default);
}

/// Loads and saves settings.json / ui-state.json with STJ source generation.
public sealed class SettingsStore : ISettingsStore
{
    private readonly string _rootDirectory;
    private readonly string _settingsPath;
    private readonly string _uiStatePath;

    public SettingsStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HardwareTest");
        Directory.CreateDirectory(_rootDirectory);
        _settingsPath = Path.Combine(_rootDirectory, "settings.json");
        _uiStatePath = Path.Combine(_rootDirectory, "ui-state.json");
        AppSettings = CreateDefaultAppSettings(_rootDirectory);
        UiState = new UiState();
    }

    public AppSettings AppSettings { get; private set; }
    public UiState UiState { get; private set; }
    public string RootDirectory => _rootDirectory;
    public string RunsDirectory => Path.Combine(_rootDirectory, "runs");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RunsDirectory);

        if (File.Exists(_settingsPath))
        {
            await using var stream = File.OpenRead(_settingsPath);
            var loaded = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonContext.Default.AppSettings,
                cancellationToken);
            if (loaded is not null)
            {
                if (string.IsNullOrWhiteSpace(loaded.DataDirectory))
                {
                    loaded.DataDirectory = _rootDirectory;
                }

                AppSettings = loaded;
            }
        }
        else
        {
            await SaveAppSettingsAsync(cancellationToken);
        }

        if (File.Exists(_uiStatePath))
        {
            await using var stream = File.OpenRead(_uiStatePath);
            var loaded = await JsonSerializer.DeserializeAsync(
                stream,
                AppJsonContext.Default.UiState,
                cancellationToken);
            if (loaded is not null)
            {
                UiState = loaded;
            }
        }
        else
        {
            await SaveUiStateAsync(cancellationToken);
        }
    }

    public async Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            AppSettings,
            AppJsonContext.Default.AppSettings,
            cancellationToken);
    }

    public async Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(_uiStatePath);
        await JsonSerializer.SerializeAsync(
            stream,
            UiState,
            AppJsonContext.Default.UiState,
            cancellationToken);
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
}
