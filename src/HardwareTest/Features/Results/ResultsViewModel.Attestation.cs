using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
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

        if (_attestation.HasValidAttestation(run, reportKind))
        {
            return true;
        }

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
        if (_attestation is null || OpenedRun is null)
        {
            DismissAttestationPrompt();
            return;
        }

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
            var result = await _attestation
                .AttestAsync(OpenedRun, kind, _capturedAttestationCredential, pin, skipSigning)
                .ConfigureAwait(true);
            LoadAttestation(OpenedRun);
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
            LoadReportItems(OpenedRun);
            DismissAttestationPrompt();
            ContinuePendingAction(pending, printPath, kind);
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

    private void ContinuePendingAction(string? pending, string? printPath, string reportKind)
    {
        if (string.Equals(pending, PendingExport, StringComparison.Ordinal))
        {
            ExportPackageCore();
            return;
        }

        if (string.Equals(pending, PendingPrint, StringComparison.Ordinal))
        {
            var path = printPath;
            if (OpenedRun is not null)
            {
                path = ReportAttestationService.ResolveIssuedPdfPath(OpenedRun, reportKind) ?? path;
            }

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                CertifiedPrintReady?.Invoke(this, path);
            }
        }
    }

    /// Shows the badge overlay for a certification PDF print. Preview itself is not gated.
    public async Task RequestCertifiedPrintAsync(string pdfPath)
    {
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
