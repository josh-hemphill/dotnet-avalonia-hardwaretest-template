using System;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

/// Explicit confirmation and incoming DUT/technician session ownership boundaries.
public partial class OperatorSessionPanelViewModel
{
    private void ConfirmSession()
    {
        if (_isRunning()) return;
        var program = _getSelectedProgram();
        var req = CurrentRequirements;
        var family = program?.DutFamily ?? "generic";
        RefreshRequirementFlags();
        ClearFieldErrors();
        DutSerialInput = NormalizeScan(DutSerialInput);
        DutPartInput = NormalizeScan(DutPartInput);
        DutRevisionInput = NormalizeScan(DutRevisionInput);
        OperatorInput = NormalizeScan(OperatorInput);
        PrepareChangedSessionIdentity(requireTypedOperator: true);
        if (!TryRequireCredential(req, out var credentialError))
        {
            ApplyFieldError(credentialError, SessionFieldCredential);
            _setStatus(credentialError);
            SessionBlocked = true;
            return;
        }

        if (!_session.TryConfirm(req, DutSerialInput, DutPartInput, DutRevisionInput, OperatorInput, family, out var error))
        {
            ApplyFieldError(error, NextIncompleteSessionField());
            _setStatus(error);
            SessionBlocked = true;
            return;
        }

        PendingConfirmEveryRun = false;
        _requiresFullConfirmation = false;
        InvalidateCredentialCapture();
        ShowSessionForm = false;
        RefreshSessionSummary();
        UpdateIdleTimer();
        _setStatus($"Session confirmed: {_session.DutSerial} / {_session.OperatorName ?? "—"}");
        RequestFocusRun?.Invoke(this, EventArgs.Empty);
    }

    private void ConfirmSameDut()
    {
        if (_isRunning()) return;
        var req = CurrentRequirements;
        RefreshRequirementFlags();
        ClearFieldErrors();
        if (NeedsDutConfirm)
        {
            var field = NextIncompleteSessionField();
            var message = field switch
            {
                SessionFieldDutSerial => "DUT serial is required.",
                SessionFieldDutPart => "DUT part number is required for this program.",
                SessionFieldDutRevision => "DUT revision is required for this program.",
                SessionFieldTechnician => "Technician name is required.",
                _ => "Confirm the updated session information before Run."
            };
            if (!TryRequireCredential(req, out var pendingCredentialError))
            {
                field = SessionFieldCredential;
                message = pendingCredentialError;
            }
            if (field is not null) ApplyFieldError(message, field);
            _setStatus(message);
            SessionBlocked = true;
            ShowSessionForm = true;
            return;
        }
        if (RequireOperator
            && string.IsNullOrWhiteSpace(OperatorInput)
            && string.IsNullOrWhiteSpace(_session.OperatorName))
        {
            OperatorError = "Technician name is required.";
            this.RaisePropertyChanged(nameof(HasOperatorError));
            _setStatus(OperatorError);
            SessionBlocked = true;
            ShowSessionForm = true;
            ShowStaleTechnicianField = true;
            RequestFocusSessionField?.Invoke(SessionFieldTechnician);
            return;
        }

        if (!string.IsNullOrWhiteSpace(OperatorInput)
            && ChangedKnownIdentity(_session.OperatorName, OperatorInput))
        {
            // Same DUT does not mean the outgoing technician owns the next run.
            // Keep this DUT in the form, but require the incoming session's identity.
            RetainSameDutInputs();
            PrepareChangedSessionIdentity(requireTypedOperator: true);
            ConfirmSession();
            return;
        }

        if (!string.IsNullOrWhiteSpace(OperatorInput))
        {
            OperatorInput = NormalizeScan(OperatorInput);
            _session.OperatorName = OperatorInput;
        }

        if (!TryRequireCredential(req, out var credentialError))
        {
            ApplyFieldError(credentialError, SessionFieldCredential);
            _setStatus(credentialError);
            SessionBlocked = true;
            ShowSessionForm = true;
            return;
        }

        _session.ConfirmSameDut();
        InvalidateCredentialCapture();
        PendingConfirmEveryRun = false;
        DutSerialInput = _session.DutSerial;
        DutPartInput = _session.DutPartNumber ?? string.Empty;
        DutRevisionInput = _session.DutRevision ?? string.Empty;
        OperatorInput = _session.OperatorName ?? string.Empty;
        ShowSessionForm = !_session.CanRun;
        RefreshSessionSummary();
        UpdateIdleTimer();
        _setStatus(_session.CanRun ? $"Still testing {_session.DutSerial}." : "Confirm DUT, then Run.");
        if (_session.CanRun) RequestFocusRun?.Invoke(this, EventArgs.Empty);
    }

    private void ChangeSession()
    {
        if (_isRunning())
        {
            return;
        }

        StartNewSession(preserveInputs: false);
        _setStatus(RequireOperator
            ? "Enter the DUT and technician for the next session, then confirm. Saved runs remain in Results."
            : "Enter the DUT for the next session, then confirm. Saved runs remain in Results.");
        RequestFocusDutSerial?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareChangedSessionIdentity(bool requireTypedOperator)
    {
        if (_session.ConfirmedAt is null) return;
        var requirements = CurrentRequirements;
        if (requirements.RequireSerial && string.IsNullOrWhiteSpace(NormalizeScan(DutSerialInput))
            || requirements.RequirePartNumber && string.IsNullOrWhiteSpace(NormalizeScan(DutPartInput))
            || requirements.RequireRevision && string.IsNullOrWhiteSpace(NormalizeScan(DutRevisionInput))
            || requireTypedOperator && requirements.RequireOperator && string.IsNullOrWhiteSpace(NormalizeScan(OperatorInput)))
            return;
        var changed = !string.Equals(_session.DutSerial, NormalizeScan(DutSerialInput), StringComparison.Ordinal)
            || ChangedKnownIdentity(_session.DutPartNumber, DutPartInput)
            || ChangedKnownIdentity(_session.DutRevision, DutRevisionInput)
            || (requireTypedOperator || !string.IsNullOrWhiteSpace(OperatorInput))
                && ChangedKnownIdentity(_session.OperatorName, OperatorInput);
        if (changed) StartNewSession(preserveInputs: true);
    }

    private void RetainSameDutInputs()
    {
        DutSerialInput = _session.DutSerial;
        DutPartInput = _session.DutPartNumber ?? string.Empty;
        DutRevisionInput = _session.DutRevision ?? string.Empty;
    }

    private static bool ChangedKnownIdentity(string? confirmed, string input)
        => !string.IsNullOrWhiteSpace(confirmed)
            && !string.Equals(confirmed, NormalizeScan(input), StringComparison.Ordinal);

    private void StartNewSession(bool preserveInputs)
    {
        // A full reconfirmation form can be reopened by reference/policy changes.
        // Editing a known DUT or technician crosses the same boundary as Change Session.
        // Completing previously unknown metadata with the same identity does not.
        _session.ChangeSession();
        _requiresFullConfirmation = false;
        PendingConfirmEveryRun = false;
        ClearFieldErrors();
        _onSessionCleared();
        if (!preserveInputs)
        {
            DutSerialInput = string.Empty;
            DutPartInput = string.Empty;
            DutRevisionInput = string.Empty;
            OperatorInput = string.Empty;
        }
        CredentialStatus = _credentialBroker?.StatusText ?? string.Empty;
        ShowSessionForm = true;
        RefreshSessionSummary();
        UpdateIdleTimer();
    }
}
