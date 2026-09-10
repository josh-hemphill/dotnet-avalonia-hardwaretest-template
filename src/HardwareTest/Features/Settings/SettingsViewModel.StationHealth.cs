using System.Linq;
using HardwareTest.Core.Settings;
using HardwareTest.Core.StationHealth;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel
{
    private IStationHealthStore? _stationHealthStore;
    private IClock _stationHealthClock = SystemClock.Instance;

    [Reactive] private string _stationHealthGateOverride = string.Empty;
    [Reactive] private bool _stationHealthGateOverrideReadOnly;
    [Reactive] private string _stationHealthSummary = string.Empty;
    [Reactive] private bool _showStationHealthSummary;
    [Reactive] private string _stationHealthStorePath = string.Empty;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CopyStationHealthPathCommand { get; private set; }
        = null!;

    private void InitStationHealthChrome(IStationHealthStore? store, IClock clock)
    {
        _stationHealthStore = store;
        _stationHealthClock = clock;
        RefreshStationHealthSummary();
    }

    /// Last record one-liner; hidden when no record and no health program.
    public void RefreshStationHealthSummary()
    {
        var profile = string.IsNullOrWhiteSpace(_settingsStore.AppSettings.StationHealthProfileId)
            ? FileStationHealthStore.DefaultProfileId
            : _settingsStore.AppSettings.StationHealthProfileId.Trim();
        var record = _stationHealthStore?.TryRead(profile);
        var hasHealthProgram = ProgramCatalog.Enumerate()
            .Any(e => ProgramKinds.IsStationHealth(e.ProgramKind));
        ShowStationHealthSummary = record is not null || hasHealthProgram;
        StationHealthStorePath = _stationHealthStore is FileStationHealthStore files
            ? files.PathFor(profile)
            : string.Empty;

        if (record is null)
        {
            StationHealthSummary = "Station health: none";
            return;
        }

        var age = StationHealthGate.FormatAge(_stationHealthClock.UtcNow - record.MeasuredAt);
        var when = record.MeasuredAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        StationHealthSummary = string.IsNullOrEmpty(age)
            ? $"Station health: {record.Verdict} · {when}"
            : $"Station health: {record.Verdict} · {age} ago · {when}";
    }

    private async Task CopyStationHealthPathAsync()
    {
        if (string.IsNullOrWhiteSpace(StationHealthStorePath))
        {
            Status = "Station health store path is not available.";
            return;
        }

        if (CopyTextAsync is null)
        {
            Status = "Clipboard is not available in this host.";
            return;
        }

        try
        {
            await CopyTextAsync(StationHealthStorePath);
            Status = $"Copied path: {StationHealthStorePath}";
        }
        catch (Exception ex)
        {
            Status = $"Copy failed: {ex.Message}";
        }
    }
}
