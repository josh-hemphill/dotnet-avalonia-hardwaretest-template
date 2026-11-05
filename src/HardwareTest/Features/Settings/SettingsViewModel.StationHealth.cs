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
    private bool _hasHealthProgram;
    private bool _hasHealthRecord;
    private bool _healthPassed;

    public string StationHealthNextStep => !_hasHealthProgram
        ? "Open Run to review the selected test's station requirements."
        : !_hasHealthRecord || !_healthPassed
            ? "Choose the station-health program on Run to check this station before testing devices that require it."
            : "Open Run to check whether this result meets the selected test's readiness policy.";

    [Reactive] private string _stationHealthGateOverride = string.Empty;
    [Reactive] private bool _stationHealthGateOverrideReadOnly;
    [Reactive] private string _stationHealthSummary = string.Empty;
    [Reactive] private bool _showStationHealthSummary;
    [Reactive] private string _stationHealthStorePath = string.Empty;

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CopyStationHealthPathCommand { get; private set; }

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
        _hasHealthProgram = ProgramCatalog.Enumerate()
            .Any(e => ProgramKinds.IsStationHealth(e.ProgramKind));
        _hasHealthRecord = record is not null;
        _healthPassed = record?.Verdict == StationHealthVerdicts.Pass;
        ShowStationHealthSummary = record is not null || _hasHealthProgram;
        this.RaisePropertyChanged(nameof(StationHealthNextStep));
        StationHealthStorePath = _stationHealthStore is FileStationHealthStore files
            ? files.PathFor(profile)
            : string.Empty;

        if (record is null)
        {
            StationHealthSummary = "No station-health result is stored.";
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
