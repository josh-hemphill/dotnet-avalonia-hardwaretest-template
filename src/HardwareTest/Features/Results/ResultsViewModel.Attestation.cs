using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.Reporting;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    private const string PendingExport = "export";
    private const string PendingPrint = "print";

    private string? _pendingAttestationAction;
    private string? _pendingAttestationKind;
    private string? _pendingPrintPath;
    private OperatorCredential? _capturedAttestationCredential;
    private TestRunRecord? _pendingAttestationRun;
    private CancellationTokenSource? _pendingAttestationCancellation;
    private long _pendingAttestationVersion;
    public void CancelPendingReportAction() => DismissAttestationPrompt();

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
        string pendingAction)
    {
        if (_attestation is null || !_attestation.NeedsAttestation(run, reportKind))
        {
            return true;
        }

        var selected = pendingAction == PendingPrint ? run.Reports.FirstOrDefault(r => ReportArtifactRoles.IsIssued(r.Role)
            && string.Equals(r.Kind, reportKind, StringComparison.OrdinalIgnoreCase)
            && ReportAttestationService.PathEquals(r.PdfPath, _pendingPrintPath)) : null;
        if (selected is not null
            ? _attestation.HasValidAttestationForPdf(run, reportKind, _pendingPrintPath!)
            : _attestation.HasValidAttestation(run, reportKind))
        {
            return true;
        }

        if (selected is not null)
        {
            Status = "Verification failed for this issued revision. Select a working report to create a new revision.";
            return false;
        }
        if (run.IsSchemaReadOnly)
        {
            Status = "This run uses a newer schema and cannot be signed.";
            return false;
        }
        _pendingAttestationCancellation?.Cancel();
        _pendingAttestationCancellation?.Dispose();
        _pendingAttestationCancellation = new CancellationTokenSource();
        _pendingAttestationVersion++;
        _pendingAttestationRun = run;
        _pendingAttestationAction = pendingAction;
        _pendingAttestationKind = reportKind;
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
        _pendingAttestationVersion++;
        _pendingAttestationCancellation?.Cancel();
        _pendingAttestationRun = null;
        ShowAttestationPrompt = false;
        ShowAttestationPin = false;
        IsCapturingAttestation = false;
        AttestationPin = string.Empty;
        _capturedAttestationCredential = null;
        _pendingAttestationAction = null;
        _pendingAttestationKind = null;
        _pendingPrintPath = null;
        AttestationPromptStatus = string.Empty;
    }

    private Task CaptureAttestationAsync() => CompleteAttestationAsync(skipSigning: false);

    private Task UsePresenceAttestationAsync() => CompleteAttestationAsync(skipSigning: true);

    private async Task CompleteAttestationAsync(bool skipSigning)
    {
        if (_attestation is null || _pendingAttestationRun is null)
        {
            DismissAttestationPrompt();
            return;
        }

        using var capture = ReportCaptureGate.TryEnter(_attestation);
        if (capture is null)
        {
            AttestationPromptStatus = ReportCaptureGate.WaitingMessage;
            Status = AttestationPromptStatus;
            return;
        }

        var run = _pendingAttestationRun;
        var version = _pendingAttestationVersion;
        var token = _pendingAttestationCancellation!.Token;
        var kind = _pendingAttestationKind ?? ReportKinds.Certification;
        var pending = _pendingAttestationAction;
        var printPath = _pendingPrintPath;
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
            var result = await Task.Run(() => _attestation.AttestAsync(run, kind, _capturedAttestationCredential,
                pin, skipSigning, token), token).ConfigureAwait(true);
            if (version != _pendingAttestationVersion || token.IsCancellationRequested) return;
            LoadAttestation(run);
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
            LoadReportItems(run);
            DismissAttestationPrompt();
            ContinuePendingAction(pending, printPath, kind, run, result.Attestation?.RevisionId);
        }
        catch (OperationCanceledException)
        {
            if (version == _pendingAttestationVersion) AttestationPromptStatus = "Badge capture cancelled.";
        }
        finally
        {
            if (version == _pendingAttestationVersion)
            {
                IsCapturingAttestation = false;
                AttestationPin = string.Empty;
            }
        }
    }

    private void ContinuePendingAction(string? pending, string? printPath, string reportKind, TestRunRecord run, string? revisionId)
    {
        if (string.Equals(pending, PendingExport, StringComparison.Ordinal))
        {
            ExportPackageCore(run);
            return;
        }

        if (string.Equals(pending, PendingPrint, StringComparison.Ordinal))
        {
            var path = revisionId is null ? null : run.Reports.FirstOrDefault(r =>
                ReportArtifactRoles.IsIssued(r.Role) && r.RevisionId == revisionId
                && string.Equals(r.Kind, reportKind, StringComparison.OrdinalIgnoreCase))?.PdfPath;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                CertifiedPrintReady?.Invoke(this, path);
            }
        }
    }

    /// Shows the badge overlay for a certification PDF print. Preview itself is not gated.
    public async Task RequestCertifiedPrintAsync(string pdfPath)
    {
        DismissAttestationPrompt();
        var requestVersion = _pendingAttestationVersion;
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            Status = "Report PDF not found.";
            return;
        }

        var run = OpenedRun;
        if (run is null || !ReportAttestationService.RunOwnsPdf(run, pdfPath))
        {
            run = await LoadRunForPdfAsync(pdfPath).ConfigureAwait(true);
        }

        if (run is null)
        {
            Status = "Open a run first.";
            return;
        }

        if (requestVersion != _pendingAttestationVersion) return;
        OpenedRun = run;
        LoadReportItems(run);
        var kind = ReportAttestationService.KindForPdf(run, pdfPath);
        var printPath = ReportAttestationService.ResolvePrintOrExportPdfPath(run, pdfPath);
        _pendingPrintPath = printPath;
        if (TryBeginCertifiedAction(run, kind, PendingPrint))
        {
            CertifiedPrintReady?.Invoke(this, printPath);
        }
    }

    private async Task<TestRunRecord?> LoadRunForPdfAsync(string pdfPath)
    {
        var runId = ReportAttestationService.GuessRunIdFromPdfPath(pdfPath);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            var byFolder = await _runStore.LoadAsync(runId).ConfigureAwait(true);
            if (byFolder is not null && ReportAttestationService.RunOwnsPdf(byFolder, pdfPath))
            {
                return byFolder;
            }
        }

        foreach (var summary in await _runStore.ListAsync().ConfigureAwait(true))
        {
            var loaded = await _runStore.LoadAsync(summary.RunId).ConfigureAwait(true);
            if (loaded is not null && ReportAttestationService.RunOwnsPdf(loaded, pdfPath))
            {
                return loaded;
            }
        }

        return null;
    }
}
