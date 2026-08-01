using System;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// DUT / technician confirmation panel: confirm, same-DUT, change session and stale prompts.
public partial class OperatorSessionPanelViewModel : ReactiveObject
{
    private readonly OperatorSession _session;
    private readonly AppSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Func<ProgramItemViewModel?> _getSelectedProgram;
    private readonly Action _onSessionCleared;

    public OperatorSessionPanelViewModel(
        OperatorSession session,
        AppSettings settings,
        Action<string> setStatus,
        Func<ProgramItemViewModel?>? getSelectedProgram = null,
        Action? onSessionCleared = null)
    {
        _session = session;
        _settings = settings;
        _setStatus = setStatus;
        _getSelectedProgram = getSelectedProgram ?? (() => null);
        _onSessionCleared = onSessionCleared ?? (() => { });

        ConfirmSessionCommand = ReactiveCommand.Create(ConfirmSession);
        ConfirmSameDutCommand = ReactiveCommand.Create(ConfirmSameDut);
        ChangeSessionCommand = ReactiveCommand.Create(ChangeSession);
    }

    public OperatorSession Session => _session;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSessionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSameDutCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ChangeSessionCommand { get; }

    [Reactive] private string _dutSerialInput = string.Empty;
    [Reactive] private string _dutPartInput = string.Empty;
    [Reactive] private string _dutRevisionInput = string.Empty;
    [Reactive] private string _operatorInput = string.Empty;
    [Reactive] private bool _requirePartNumber;
    [Reactive] private bool _requireRevision;
    [Reactive] private bool _requireOperator = true;
    [Reactive] private bool _showSessionForm = true;
    [Reactive] private bool _sessionBlocked = true;
    [Reactive] private string _sessionSummary = "Session: (confirm required)";
    [Reactive] private bool _needsDutConfirm = true;
    [Reactive] private bool _isStalePrompt;

    /// Marks the session stale once it has been idle past the configured window.
    public void ApplyIdleStaleCheck()
    {
        var hours = Math.Max(1, _settings.OperatorSessionIdleHours);
        _session.CheckIdleStale(TimeSpan.FromHours(hours));
    }

    public void RefreshRequirementFlags()
    {
        var req = _getSelectedProgram()?.Requirements ?? ProgramRequirements.Sample;
        RequirePartNumber = req.RequirePartNumber;
        RequireRevision = req.RequireRevision;
        RequireOperator = req.RequireOperator;
    }

    public void RefreshSessionSummary()
    {
        NeedsDutConfirm = _session.State == OperatorSessionState.NeedsDut;
        IsStalePrompt = _session.State == OperatorSessionState.Stale;
        SessionBlocked = !_session.CanRun;
        ShowSessionForm = NeedsDutConfirm || IsStalePrompt || SessionBlocked;
        var program = _session.ProgramDisplayName ?? "(none)";
        if (_session.CanRun)
        {
            SessionSummary = $"DUT {_session.DutSerial} | Tech {_session.OperatorName ?? "—"} | {program}";
            return;
        }

        if (_session.State == OperatorSessionState.Stale)
        {
            SessionSummary = $"DUT {_session.DutSerial} (re-confirm) | {program}";
            return;
        }

        SessionSummary = $"Session blocked — confirm DUT + technician | {program}";
    }

    private void ConfirmSession()
    {
        var program = _getSelectedProgram();
        var req = program?.Requirements ?? ProgramRequirements.Sample;
        var family = program?.DutFamily ?? "generic";
        if (!_session.TryConfirm(req, DutSerialInput, DutPartInput, DutRevisionInput, OperatorInput, family, out var error))
        {
            _setStatus(error);
            SessionBlocked = true;
            return;
        }

        ShowSessionForm = false;
        RefreshSessionSummary();
        _setStatus($"Session confirmed: {_session.DutSerial} / {_session.OperatorName}");
    }

    private void ConfirmSameDut()
    {
        if (string.IsNullOrWhiteSpace(OperatorInput) && string.IsNullOrWhiteSpace(_session.OperatorName))
        {
            _setStatus("Technician name is required.");
            SessionBlocked = true;
            ShowSessionForm = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(OperatorInput))
        {
            _session.OperatorName = OperatorInput.Trim();
        }

        _session.ConfirmSameDut();
        DutSerialInput = _session.DutSerial;
        DutPartInput = _session.DutPartNumber ?? string.Empty;
        DutRevisionInput = _session.DutRevision ?? string.Empty;
        OperatorInput = _session.OperatorName ?? string.Empty;
        ShowSessionForm = !_session.CanRun;
        RefreshSessionSummary();
        _setStatus(_session.CanRun ? $"Still testing {_session.DutSerial}." : "Confirm DUT, then Run.");
    }

    private void ChangeSession()
    {
        _session.ChangeSession();
        _onSessionCleared();
        DutSerialInput = string.Empty;
        DutPartInput = string.Empty;
        DutRevisionInput = string.Empty;
        OperatorInput = string.Empty;
        ShowSessionForm = true;
        RefreshSessionSummary();
        _setStatus("Confirm DUT, then Run.");
    }
}
