using HardwareTest.Core.Hardware;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    private readonly IVisaModeController? _visaModeController;
    private string? _lastSavedRunId;

    /// The effective VISA adapter mode, rather than an unapplied requested setting.
    public string VisaModeText => (_visaModeController?.EffectiveUseMockVisa ?? _settings.UseMockVisa)
        ? "VISA: simulated"
        : "VISA: physical";

    public string VisaModeHelpText => (_visaModeController?.EffectiveUseMockVisa ?? _settings.UseMockVisa)
        ? "VISA instrument responses are simulated. This does not describe other equipment used by the program."
        : "VISA uses physical instrument connections. Confirm the station procedure before starting.";

    public bool ShowSavedResult => !IsRunning && !string.IsNullOrWhiteSpace(LastRunId)
        && string.Equals(LastRunId, _lastSavedRunId, StringComparison.Ordinal);

    private void RefreshVisaMode()
    {
        this.RaisePropertyChanged(nameof(VisaModeText));
        this.RaisePropertyChanged(nameof(VisaModeHelpText));
    }

    internal void MarkLastRunSaved(string runId)
    {
        _lastSavedRunId = runId;
        this.RaisePropertyChanged(nameof(ShowSavedResult));
    }
}
