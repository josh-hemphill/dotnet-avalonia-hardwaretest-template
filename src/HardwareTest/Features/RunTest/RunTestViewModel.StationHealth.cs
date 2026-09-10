using HardwareTest.Core.StationHealth;
using HardwareTest.Features.Shell;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    /// True when neither a run, session block, nor a station-health Block is holding start.
    public bool CanStartRun
        => !IsRunning
           && !SessionPanel.SessionBlocked
           && !string.Equals(
               _stationHealthDecision?.Level,
               StationHealthGateLevels.Block,
               StringComparison.OrdinalIgnoreCase);

    /// Tooltip for Run / Run Selected reflecting why start is blocked when disabled.
    public string CanStartRunTip
    {
        get
        {
            if (IsRunning)
            {
                return StopRunCopy.InProgressTip;
            }

            if (SessionPanel.SessionBlocked)
            {
                if (SessionPanel.IsStalePrompt || SessionPanel.IsIdleWarningPrompt)
                {
                    return "Confirm Same DUT or Change Session before Run.";
                }

                return "Confirm DUT first.";
            }

            if (_stationHealthDecision is { } blocked
                && string.Equals(blocked.Level, StationHealthGateLevels.Block, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(blocked.Message))
            {
                return blocked.Message;
            }

            if (_stationHealthDecision is { } warned
                && string.Equals(warned.Level, StationHealthGateLevels.Warn, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(warned.Message))
            {
                return warned.Message;
            }

            return "Run the full suite.";
        }
    }

    /// Tooltip for Run Selected (same gates, different idle copy).
    public string CanStartRunSelectedTip
        => CanStartRun
            ? "Run the selected leaf or section (subtree + Safe Shutdown). Use Run for the full suite."
            : CanStartRunTip;

    /// Re-evaluates sidecar freshness for the selected program.
    public void RefreshStationHealthGate()
    {
        var program = ProgramSelection.SelectedProgram;
        _stationHealthDecision = program is null || _stationHealthGate is null
            ? null
            : _stationHealthGate.Evaluate(program.ToGateRequest(), _clock);
        this.RaisePropertyChanged(nameof(CanStartRun));
        this.RaisePropertyChanged(nameof(CanStartRunTip));
        this.RaisePropertyChanged(nameof(CanStartRunSelectedTip));
        this.RaisePropertyChanged(nameof(ShowStartBlockedTip));
    }
}
