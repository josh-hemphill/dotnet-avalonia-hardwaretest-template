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

    /// Tooltip for Run Selected (same gates, plus hidden / whole-plan copy).
    public string CanStartRunSelectedTip
    {
        get
        {
            if (!CanStartRun)
            {
                return CanStartRunTip;
            }

            if (StepTree.SelectedStep is { } step && StepTree.IsWholePlanSelection(step.Path))
            {
                return "Run Selected needs a specific stage or step — not the entire program. Use Run for the full suite.";
            }

            if (!StepTree.IsSelectedStepVisible())
            {
                return "Select a visible stage or step to run.";
            }

            return "Run the selected leaf or section (subtree + Safe Shutdown). Use Run for the full suite.";
        }
    }

    /// True when Run Selected can start the current on-screen selection.
    public bool CanStartRunSelected
        => CanStartRun
           && StepTree.IsSelectedStepVisible()
           && StepTree.SelectedStep is { } selected
           && !StepTree.IsWholePlanSelection(selected.Path);

    /// Re-evaluates sidecar freshness for the selected program.
    public void RefreshStationHealthGate()
    {
        var program = ProgramSelection.SelectedProgram;
        _stationHealthDecision = program is null || _stationHealthGate is null
            ? null
            : _stationHealthGate.Evaluate(program.ToGateRequest(), _clock);
        RaiseStartGates();
    }

    private void RaiseStartGates()
    {
        this.RaisePropertyChanged(nameof(CanStartRun));
        this.RaisePropertyChanged(nameof(CanStartRunTip));
        this.RaisePropertyChanged(nameof(CanStartRunSelected));
        this.RaisePropertyChanged(nameof(CanStartRunSelectedTip));
        this.RaisePropertyChanged(nameof(ShowStartBlockedTip));
    }
}
