using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Reporting;

namespace HardwareTest.Features.ReportPreview;

public partial class ReportPreviewViewModel
{
    private async Task RequestActionAsync(ActionKind action, long? expectedVersion = null, CancellationToken? expectedToken = null)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false))
        {
            await RunOnUiAsync(() => { Status = ReportCaptureGate.WaitingMessage; SigningPromptStatus = Status; }).ConfigureAwait(false);
            return;
        }
        long version;
        CancellationToken token;
        string? path;
        lock (_selectionLock)
        {
            if (_selectionLoading || (expectedVersion is not null && expectedVersion != _selectionVersion)
                || (expectedToken is not null && expectedToken != _selectionCancellation.Token)) { _actionGate.Release(); return; }
            version = _selectionVersion;
            token = _selectionCancellation.Token;
            path = PdfPath;
        }
        var selectedPath = path;
        try
        {
            await BeginActionAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "No PDF selected."; }).ConfigureAwait(false);
                return;
            }
            var run = await Task.Run(() => FindRunForPdfAsync(path)).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            if (action == ActionKind.Print && run is not null)
                path = ReportAttestationService.ResolvePrintOrExportPdfPath(run, path);
            var artifact = run?.Reports.FirstOrDefault(r => ReportAttestationService.PathEquals(r.PdfPath, path));
            var kind = run is null ? ReportKinds.Status : ReportAttestationService.KindForPdf(run, path);
            var issued = artifact is not null && ReportArtifactRoles.IsIssued(artifact.Role);
            var needsAuthorization = action is ActionKind.Save or ActionKind.Print
                && run is not null && _attestation?.NeedsAttestation(run, kind) == true;
            var valid = needsAuthorization && issued
                && await Task.Run(() => _attestation!.HasValidAttestationForPdf(run!, kind, path), token).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            var needsSignature = action == ActionKind.Sign || (needsAuthorization && !valid);
            if (needsSignature)
            {
                if (run is null || _attestation is null || run.IsSchemaReadOnly || issued)
                {
                    await RunOnUiAsync(() =>
                    {
                        if (version != _selectionVersion || token.IsCancellationRequested) return;
                        Status = run?.IsSchemaReadOnly == true
                        ? "This run uses a newer schema and cannot be signed."
                        : issued ? "Select a working copy to create a new signed revision. This issued revision remains unchanged."
                        : "Signing is unavailable for this PDF.";
                    }).ConfigureAwait(false);
                    return;
                }
                lock (_selectionLock)
                {
                    if (version != _selectionVersion || token.IsCancellationRequested) return;
                    _signingCancellation?.Cancel();
                    _signingCancellation?.Dispose();
                    _signingCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _pending = new PendingAction(run, kind, action, version);
                    _capturedCredential = null;
                }
                await RunOnUiAsync(() =>
                {
                    if (version != _selectionVersion) return;
                    ShowSigningPrompt = true;
                    ShowSigningPin = false;
                    SignButtonLabel = action == ActionKind.Sign ? "Sign report" : "Sign and continue";
                    SigningPromptStatus = "Tap or insert a badge to sign this report.";
                    Status = action == ActionKind.Sign ? "Sign this working report." : "Sign this report to continue the requested action.";
                }).ConfigureAwait(false);
                return;
            }
            if (action == ActionKind.Print && !ReportAttestationService.PathEquals(selectedPath, path))
            {
                var selection = await LoadFromPathCoreAsync(path, version).ConfigureAwait(false);
                if (selection is null) return;
                version = selection.Version;
                token = selection.Token;
            }
            await PerformActionAsync(action, path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (version == _selectionVersion && !token.IsCancellationRequested) Status = "Report action failed: " + ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            try { await EndActionAsync().ConfigureAwait(false); }
            finally { _actionGate.Release(); }
        }
    }

    private Task BeginActionAsync() => RunOnUiAsync(() =>
    {
        _actionInProgress = true;
        IsBusy = true;
    });

    private Task EndActionAsync() => RunOnUiAsync(() =>
    {
        _actionInProgress = false;
        IsBusy = _selectionLoading;
    });

    private async Task CompleteSigningAsync(bool presence)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false))
        {
            await RunOnUiAsync(() => { Status = ReportCaptureGate.WaitingMessage; SigningPromptStatus = Status; }).ConfigureAwait(false);
            return;
        }
        try
        {
            await BeginActionAsync().ConfigureAwait(false);
            await CompleteSigningCoreAsync(presence).ConfigureAwait(false);
        }
        finally
        {
            try { await EndActionAsync().ConfigureAwait(false); }
            finally { _actionGate.Release(); }
        }
    }

    private async Task CompleteSigningCoreAsync(bool presence)
    {
        if (_attestation is null) return;
        SigningOperation? operation = null;
        await RunOnUiAsync(() =>
        {
            lock (_selectionLock)
            {
                var pendingAction = _pending;
                var cancellation = _signingCancellation;
                if (pendingAction is null || cancellation is null || pendingAction.Version != _selectionVersion
                    || cancellation.IsCancellationRequested) return;
                operation = new SigningOperation(pendingAction, cancellation.Token,
                    ShowSigningPin ? SigningPin : null, _capturedCredential);
            }
        }).ConfigureAwait(false);
        if (operation is null) return;
        var pending = operation.Pending;
        var token = operation.Token;
        using var capture = ReportCaptureGate.TryEnter(_attestation);
        if (capture is null)
        {
            await RunOnUiAsync(() =>
            {
                if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                SigningPromptStatus = ReportCaptureGate.WaitingMessage;
                Status = SigningPromptStatus;
            }).ConfigureAwait(false);
            return;
        }
        var effectVersion = pending.Version;
        try
        {
            ReportAttestationResult result;
            try
            {
                result = await Task.Run(() => _attestation.AttestAsync(pending.Run, pending.Kind, operation.Credential,
                    operation.Pin, presence, token), token).ConfigureAwait(false);
            }
            // The per-preview action gate still owns the resumed desktop operation.
            finally { capture.Dispose(); }
            if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
            if (!result.Succeeded)
            {
                await RunOnUiAsync(() =>
                {
                    if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                    SigningPromptStatus = result.Message;
                    Status = result.Message;
                    if (result.PinRequired) { _capturedCredential = result.Credential; ShowSigningPin = true; }
                    SigningPin = string.Empty;
                }).ConfigureAwait(false);
                return;
            }
            var revision = result.Attestation?.RevisionId is { } revisionId
                ? pending.Run.Reports.FirstOrDefault(r => r.RevisionId == revisionId
                    && ReportArtifactRoles.IsIssued(r.Role) && string.Equals(r.Kind, pending.Kind, StringComparison.OrdinalIgnoreCase))
                : null;
            if (revision is null) throw new InvalidOperationException("Signing did not commit a report revision.");
            // Consume the pending action before previewing or invoking any external operation.
            lock (_selectionLock)
            {
                if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                _pending = null;
            }
            var selection = await LoadFromPathCoreAsync(revision.PdfPath, pending.Version).ConfigureAwait(false);
            if (selection is null) return;
            effectVersion = selection.Version;
            if (pending.Action != ActionKind.Sign)
                await PerformActionAsync(pending.Action, revision.PdfPath, selection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (effectVersion == _selectionVersion && (effectVersion != pending.Version || !token.IsCancellationRequested))
                    Status = "Signing failed: " + ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            if (effectVersion == _selectionVersion) await RunOnUiAsync(() =>
            {
                if (effectVersion == _selectionVersion) SigningPin = string.Empty;
            }).ConfigureAwait(false);
        }
    }

}
