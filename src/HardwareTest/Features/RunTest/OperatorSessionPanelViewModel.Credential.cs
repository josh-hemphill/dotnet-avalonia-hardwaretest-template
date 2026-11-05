using HardwareTest.Core.Credentials;
using HardwareTest.OpenTap.Host;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class OperatorSessionPanelViewModel
{
    private CancellationTokenSource? _credentialCaptureCancellation;

    private void InvalidateCredentialCapture()
    {
        var capture = _credentialCaptureCancellation;
        if (capture is null) return;
        // Release ownership before cancellation can resume an older request's continuation.
        _credentialCaptureCancellation = null;
        IsCapturingCredential = false;
        CredentialStatus = _credentialBroker?.StatusText ?? string.Empty;
        capture.Cancel();
    }

    /// Starts badge presence wait when Settings auto-probe is on and the technician field is empty.
    public void OnTechnicianFocused()
    {
        if (!_settings.ProbeBadgeWhenTechnicianFocused
            || !ShowCredentialCapture
            || IsCapturingCredential
            || _credentialBroker is null
            || !string.IsNullOrWhiteSpace(OperatorInput))
        {
            return;
        }

        _ = CaptureCredentialAsync();
    }

    /// Waits for a chip insert or contactless tap and fills technician identity.
    private async Task CaptureCredentialAsync()
    {
        if (_credentialBroker is null || IsCapturingCredential || _isRunning())
        {
            return;
        }

        // Bind fresh presence to an edited incoming DUT before awaiting the reader.
        // Confirm then preserves this new context rather than clearing its fresh badge.
        PrepareChangedSessionIdentity(requireTypedOperator: false);
        using var capture = new CancellationTokenSource();
        _credentialCaptureCancellation = capture;
        IsCapturingCredential = true;
        CredentialStatus = _credentialBroker.StatusText;
        try
        {
            var result = await _credentialBroker
                .WaitForPresenceAsync(ReportAttestationService.DefaultPresenceTimeout, capture.Token)
                // A faulty reader may ignore cancellation. Do not keep the incoming
                // operator's command unavailable until that reader eventually returns.
                .WaitAsync(capture.Token)
                .ConfigureAwait(true);
            if (!ReferenceEquals(_credentialCaptureCancellation, capture) || capture.IsCancellationRequested || _isRunning())
            {
                return;
            }
            if (!result.Succeeded || result.Credential is null)
            {
                CredentialStatus = result.Error ?? "Present a badge: insert chip or tap the reader.";
                OperatorError = CredentialStatus;
                this.RaisePropertyChanged(nameof(HasOperatorError));
                return;
            }

            var credential = result.Credential;
            PrepareCapturedTechnicianSession(credential);
            _session.ApplyOperatorCredential(credential.Serial, credential.Transport, credential.DisplayName);
            OperatorInput = credential.DisplayName;
            ClearFieldErrors();
            var verb = string.Equals(credential.Transport, CredentialTransport.Contact, StringComparison.OrdinalIgnoreCase)
                ? "Chip"
                : "Tap";
            CredentialStatus = $"{verb}: {credential.DisplayName} ({credential.Serial}).";
            _setStatus(CredentialStatus);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_credentialCaptureCancellation, capture))
                CredentialStatus = "Badge capture cancelled.";
        }
        catch (Exception)
        {
            // Focus-triggered capture is fire-and-forget; do not crash the UI thread.
            if (ReferenceEquals(_credentialCaptureCancellation, capture))
                CredentialStatus = "Badge capture failed.";
        }
        finally
        {
            if (ReferenceEquals(_credentialCaptureCancellation, capture))
            {
                _credentialCaptureCancellation = null;
                IsCapturingCredential = false;
            }
        }
    }

    private void PrepareCapturedTechnicianSession(OperatorCredential credential)
    {
        if (_session.ConfirmedAt is null || !ChangedKnownIdentity(_session.OperatorName, credential.DisplayName)) return;
        if (ShowSameDutPrompt) RetainSameDutInputs();
        // This owned response identifies the incoming technician. Keep its in-flight
        // ownership while the session change cancels any other departing context.
        var capture = _credentialCaptureCancellation;
        _credentialCaptureCancellation = null;
        try { StartNewSession(preserveInputs: true); }
        finally { _credentialCaptureCancellation = capture; }
    }

    /// True when site policy requires a presented badge for this program's operator field.
    private bool TryRequireCredential(ProgramRequirements requirements, out string error)
    {
        error = string.Empty;
        if (!requirements.RequireOperator || !_settings.RequireCredentialForOperator)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_session.OperatorCredentialSerial))
        {
            return true;
        }

        error = "Tap or insert a badge to identify the technician.";
        return false;
    }
}
