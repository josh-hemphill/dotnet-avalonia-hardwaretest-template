using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    private const string PendingExport = "export";
    private const string PendingOpenDefault = "open-default";
    private const string PendingOpenItem = "open-item";
    private const string PendingReprintOpen = "reprint";

    private string? _pendingAttestationAction;
    private string? _pendingAttestationKind;
    private RunReportItemViewModel? _pendingReportItem;
    private OperatorCredential? _capturedAttestationCredential;

    [Reactive] private bool _showAttestationPrompt;
    [Reactive] private bool _showAttestationPin;
    [Reactive] private string _attestationPin = string.Empty;
    [Reactive] private string _attestationPromptStatus = string.Empty;
    [Reactive] private bool _isCapturingAttestation;
    [Reactive] private bool _allowPresenceFallback;

    /// True when site policy requires a badge and this run/kind is not yet attested.
    private bool TryBeginCertifiedAction(
        TestRunRecord run,
        string reportKind,
        string pendingAction,
        RunReportItemViewModel? item = null)
    {
        if (_attestation is null || !_attestation.NeedsAttestation(run, reportKind))
        {
            return true;
        }

        if (_attestation.HasValidAttestation(run, reportKind))
        {
            return true;
        }

        _pendingAttestationAction = pendingAction;
        _pendingAttestationKind = reportKind;
        _pendingReportItem = item;
        _capturedAttestationCredential = null;
        ShowAttestationPin = false;
        AttestationPin = string.Empty;
        AllowPresenceFallback = _settings?.AllowPresenceInLieuOfSigning == true;
        ShowAttestationPrompt = true;
        AttestationPromptStatus = AllowPresenceFallback
            ? "Tap or insert a badge to sign. Presence is only a site-policy fallback."
            : "Tap or insert a badge to sign this report.";
        Status = "Certify this report before export or print.";
        return false;
    }

    private void DismissAttestationPrompt()
    {
        ShowAttestationPrompt = false;
        ShowAttestationPin = false;
        IsCapturingAttestation = false;
        AttestationPin = string.Empty;
        _capturedAttestationCredential = null;
        _pendingAttestationAction = null;
        _pendingAttestationKind = null;
        _pendingReportItem = null;
        AttestationPromptStatus = string.Empty;
    }

    private Task CaptureAttestationAsync() => CompleteAttestationAsync(skipSigning: false);

    private Task UsePresenceAttestationAsync() => CompleteAttestationAsync(skipSigning: true);

    private async Task CompleteAttestationAsync(bool skipSigning)
    {
        if (_attestation is null || OpenedRun is null)
        {
            DismissAttestationPrompt();
            return;
        }

        var kind = _pendingAttestationKind ?? ReportKinds.Certification;
        var pending = _pendingAttestationAction;
        var item = _pendingReportItem;
        var pin = ShowAttestationPin ? AttestationPin : null;
        IsCapturingAttestation = true;
        if (skipSigning)
        {
            AttestationPromptStatus = "Recording presence…";
        }
        else if (ShowAttestationPin)
        {
            AttestationPromptStatus = "Signing…";
        }
        else
        {
            AttestationPromptStatus = "Waiting for chip or tap…";
        }
        try
        {
            var result = await _attestation
                .AttestAsync(OpenedRun, kind, _capturedAttestationCredential, pin, skipSigning)
                .ConfigureAwait(true);
            if (result.PinRequired && !skipSigning)
            {
                _capturedAttestationCredential = result.Credential;
                ShowAttestationPin = true;
                AttestationPin = string.Empty;
                AttestationPromptStatus = result.Message;
                Status = result.Message;
                return;
            }

            if (!result.Succeeded)
            {
                AttestationPromptStatus = result.Message;
                Status = result.Message;
                return;
            }

            Status = result.Message;
            DismissAttestationPrompt();
            await ContinuePendingActionAsync(pending, item).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            AttestationPromptStatus = "Badge capture cancelled.";
        }
        finally
        {
            IsCapturingAttestation = false;
            AttestationPin = string.Empty;
        }
    }

    private async Task ContinuePendingActionAsync(string? pending, RunReportItemViewModel? item)
    {
        if (string.Equals(pending, PendingExport, StringComparison.Ordinal))
        {
            ExportPackageCore();
            return;
        }

        if (string.Equals(pending, PendingOpenDefault, StringComparison.Ordinal))
        {
            await OpenDefaultReportAsync().ConfigureAwait(true);
            return;
        }

        if (string.Equals(pending, PendingOpenItem, StringComparison.Ordinal) && item is not null)
        {
            await OpenReportAsync(item).ConfigureAwait(true);
            return;
        }

        if (string.Equals(pending, PendingReprintOpen, StringComparison.Ordinal) && OpenedRun is not null)
        {
            var path = ResolveDefaultReportPath(OpenedRun);
            if (path is not null && File.Exists(path))
            {
                ReportOpened?.Invoke(this, path);
            }
        }
    }
}
