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

    [Reactive] private bool _showAttestationPrompt;
    [Reactive] private string _attestationPromptStatus = string.Empty;
    [Reactive] private bool _isCapturingAttestation;

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
        ShowAttestationPrompt = true;
        var presence = _settings?.AllowPresenceInLieuOfSigning == true
            ? "Tap or insert a badge (signing optional)."
            : "Tap or insert a badge that can sign.";
        AttestationPromptStatus = presence;
        Status = "Certify this report before export or print.";
        return false;
    }

    private void DismissAttestationPrompt()
    {
        ShowAttestationPrompt = false;
        IsCapturingAttestation = false;
        _pendingAttestationAction = null;
        _pendingAttestationKind = null;
        _pendingReportItem = null;
        AttestationPromptStatus = string.Empty;
    }

    private async Task CaptureAttestationAsync()
    {
        if (_attestation is null || OpenedRun is null)
        {
            DismissAttestationPrompt();
            return;
        }

        var kind = _pendingAttestationKind ?? ReportKinds.Certification;
        var pending = _pendingAttestationAction;
        var item = _pendingReportItem;
        IsCapturingAttestation = true;
        AttestationPromptStatus = "Waiting for chip or tap…";
        try
        {
            var result = await _attestation.AttestAsync(OpenedRun, kind).ConfigureAwait(true);
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
