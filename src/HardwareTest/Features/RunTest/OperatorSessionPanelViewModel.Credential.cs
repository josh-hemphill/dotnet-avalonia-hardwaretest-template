using HardwareTest.Core.Credentials;
using HardwareTest.OpenTap.Host;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class OperatorSessionPanelViewModel
{
    /// Waits for a chip insert or contactless tap and fills technician identity.
    private async Task CaptureCredentialAsync()
    {
        if (_credentialBroker is null)
        {
            return;
        }

        IsCapturingCredential = true;
        CredentialStatus = _credentialBroker.StatusText;
        try
        {
            var result = await _credentialBroker
                .WaitForPresenceAsync(ReportAttestationService.DefaultPresenceTimeout)
                .ConfigureAwait(true);
            if (!result.Succeeded || result.Credential is null)
            {
                CredentialStatus = result.Error ?? "Present a badge: insert chip or tap the reader.";
                OperatorError = CredentialStatus;
                this.RaisePropertyChanged(nameof(HasOperatorError));
                return;
            }

            var credential = result.Credential;
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
            CredentialStatus = "Badge capture cancelled.";
        }
        finally
        {
            IsCapturingCredential = false;
        }
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
