using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// Gates certification export/print on chip/tap presence, with optional signing.
public interface IReportAttestationService
{
    TimeSpan PresenceTimeout { get; }

    /// True when site policy requires a credential before exporting or printing this kind.
    bool NeedsAttestation(TestRunRecord run, string reportKind);

    bool HasValidAttestation(TestRunRecord run, string reportKind);
    bool HasValidAttestation(TestRunRecord run, string reportKind, string? revisionId)
        => revisionId is null && HasValidAttestation(run, reportKind);
    bool HasValidAttestationForPdf(TestRunRecord run, string reportKind, string pdfPath)
        => ReportAttestationService.PathEquals(ReportAttestationService.ResolveIssuedPdfPath(run, reportKind), pdfPath)
           && HasValidAttestation(run, reportKind);

    Task<ReportAttestationResult> AttestAsync(
        TestRunRecord run,
        string reportKind,
        OperatorCredential? credential = null,
        string? pin = null,
        bool skipSigning = false,
        CancellationToken cancellationToken = default);
}

/// Captures a badge, writes a sidecar, and stamps the run record.
public sealed class ReportAttestationService : IReportAttestationService
{
    public const string PackageKind = "package";
    public static readonly TimeSpan DefaultPresenceTimeout = TimeSpan.FromSeconds(20);

    private readonly IOperatorCredentialBroker _broker;
    private readonly IRunStore _runStore;
    private readonly AppSettings _settings;
    private readonly IClock _clock;
    private readonly Lazy<IReportService>? _reports;
    private readonly IReportRevisionStore _revisions;

    public ReportAttestationService(
        IOperatorCredentialBroker broker,
        IRunStore runStore,
        AppSettings settings,
        IClock? clock = null,
        Lazy<IReportService>? reports = null,
        IReportRevisionStore? revisions = null)
    {
        _broker = broker;
        _runStore = runStore;
        _settings = settings;
        _clock = clock ?? SystemClock.Instance;
        _reports = reports;
        _revisions = revisions ?? new FileReportRevisionStore(runStore, _clock);
        PresenceTimeout = DefaultPresenceTimeout;
    }

    public TimeSpan PresenceTimeout { get; init; }

    public bool NeedsAttestation(TestRunRecord run, string reportKind)
    {
        if (!_settings.RequireAttestationBeforeExport)
        {
            return false;
        }

        if (string.Equals(reportKind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase))
        {
            return run.Reports.Any(r =>
                string.Equals(r.Kind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public bool HasValidAttestation(TestRunRecord run, string reportKind)
        => HasValidAttestation(run, reportKind, null);

    public bool HasValidAttestation(TestRunRecord run, string reportKind, string? revisionId)
    {
        var lookupKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var artifact = revisionId is null ? ReportRevisions.Latest(run, lookupKind) : run.Reports.FirstOrDefault(r =>
            ReportArtifactRoles.IsIssued(r.Role) && string.Equals(r.Kind, lookupKind, StringComparison.OrdinalIgnoreCase)
            && r.RevisionId == revisionId);
        return HasValidArtifact(run, artifact);
    }

    public bool HasValidAttestationForPdf(TestRunRecord run, string reportKind, string pdfPath)
    {
        var kind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification : reportKind;
        var artifact = run.Reports.FirstOrDefault(r => ReportArtifactRoles.IsIssued(r.Role)
            && string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)
            && PathEquals(r.PdfPath, pdfPath));
        return HasValidArtifact(run, artifact);
    }

    private bool HasValidArtifact(TestRunRecord run, RunReportArtifact? artifact)
    {
        var attestation = artifact is null ? null : FindForArtifact(run, artifact);
        if (attestation is null)
        {
            return false;
        }

        var pdfPath = artifact?.PdfPath;
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return false;
        }

        if (artifact!.RevisionId is not null && (string.IsNullOrWhiteSpace(artifact.RunSnapshotPath)
            || !File.Exists(artifact.RunSnapshotPath)
            || !string.Equals(HashFile(artifact.RunSnapshotPath), attestation.RunJsonSha256, StringComparison.OrdinalIgnoreCase)
            || !SidecarMatches(attestation, artifact.SidecarSha256))) return false;

        var pdfHash = HashFile(pdfPath);
        if (!string.Equals(pdfHash, attestation.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(attestation.Kind, AttestationKind.Signed, StringComparison.OrdinalIgnoreCase)
            && attestation.EmbeddedInPdf)
        {
            return EmbeddedSignatureMatches(attestation, pdfPath, pdfHash);
        }

        if (string.Equals(attestation.Kind, AttestationKind.Signed, StringComparison.OrdinalIgnoreCase))
        {
            return SignatureMatches(attestation, pdfHash);
        }

        return string.Equals(attestation.Kind, AttestationKind.Presence, StringComparison.OrdinalIgnoreCase)
            && _settings.AllowPresenceInLieuOfSigning;
    }

    public async Task<ReportAttestationResult> AttestAsync(
        TestRunRecord run,
        string reportKind,
        OperatorCredential? credential = null,
        string? pin = null,
        bool skipSigning = false,
        CancellationToken cancellationToken = default)
    {
        RequireReportWritable(run, _runStore.GetRunDirectory(run.RunId));
        using var operation = await ReportRevisions.LockAsync(_runStore.GetRunDirectory(run.RunId), cancellationToken).ConfigureAwait(false);
        var candidate = ReportRevisions.Clone(run);
        await ReportRevisions.RefreshHistoryAsync(candidate, _runStore, cancellationToken).ConfigureAwait(false);
        var result = await AttestCoreAsync(candidate, reportKind, credential, pin, skipSigning, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) ReportRevisions.PublishHistory(run, candidate);
        return result;
    }

    private async Task<ReportAttestationResult> AttestCoreAsync(
        TestRunRecord run, string reportKind, OperatorCredential? credential, string? pin,
        bool skipSigning, CancellationToken cancellationToken)
    {
        RequireReportWritable(run, _runStore.GetRunDirectory(run.RunId));
        var broker = _broker is SettingsBackedCredentialBroker configured ? configured.Snapshot() : _broker;
        var allowPresence = _settings.AllowPresenceInLieuOfSigning;
        var targetKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var captured = credential;
        if (captured is null)
        {
            var capture = await broker.WaitForPresenceAsync(PresenceTimeout, cancellationToken).ConfigureAwait(false);
            if (!capture.Succeeded || capture.Credential is null)
            {
                return Failure(null, capture.Error ?? "Present a badge to certify this report.");
            }
            captured = capture.Credential;
        }

        if (skipSigning || !broker.CanSign)
        {
            return await AttestPresenceAsync(run, targetKind, captured, allowPresence, cancellationToken)
                .ConfigureAwait(false);
        }
        if (!broker.IsMock && (!broker.CanSignPdf || broker is not IEmbeddedPdfSigningBroker))
        {
            return Failure(captured, "The physical credential does not support embedded PDF signing.");
        }

        var runHash = HashBytes(JsonSerializer.SerializeToUtf8Bytes(run, AppJsonContext.Default.TestRunRecord));
        if (string.IsNullOrEmpty(pin))
        {
            var probe = await broker.TrySignPayloadAsync(
                Encoding.UTF8.GetBytes($"pin-probe:{runHash}"), captured, pin, cancellationToken).ConfigureAwait(false);
            if (probe.PinRequired || probe.PinRetriesRemaining is not null)
            {
                return Failure(captured, probe.Error ?? "Enter badge PIN to sign.", pinRequired: probe.PinRetriesRemaining != 0);
            }
            if (!probe.Succeeded)
            {
                return probe.PresenceFallbackAllowed
                    ? await AttestPresenceAsync(run, targetKind, captured, allowPresence, cancellationToken).ConfigureAwait(false)
                    : Failure(captured, probe.Error ?? "Signing failed.");
            }
        }

        var candidate = await CompileCandidateAsync(run, targetKind, captured, AttestationKind.Signed, cancellationToken)
            .ConfigureAwait(false);
        if (candidate.Error is not null || candidate.Pdf is not { Length: > 0 })
        {
            return Failure(captured, candidate.Error ?? "Certification PDF is missing. Generate reports first.");
        }

        if (!broker.IsMock)
        {
            var embedded = (IEmbeddedPdfSigningBroker)broker;
            var signingTime = captured.CapturedAt == default ? _clock.UtcNow : captured.CapturedAt;
            var sign = await embedded.TrySignPdfAsync(candidate.Pdf, captured, pin, signingTime, cancellationToken)
                .ConfigureAwait(false);
            if (sign.PinRequired || sign.PinRetriesRemaining is not null)
            {
                return Failure(captured, sign.Error ?? "Enter badge PIN to sign.", pinRequired: sign.PinRetriesRemaining != 0);
            }
            if (!sign.Succeeded)
            {
                return sign.PresenceFallbackAllowed
                    ? await AttestPresenceAsync(run, targetKind, captured, allowPresence, cancellationToken).ConfigureAwait(false)
                    : Failure(captured, sign.Error ?? "Signing failed.");
            }
            if (!PhysicalResultMatches(sign, captured, out var error))
            {
                return Failure(captured, error ?? "Embedded PDF signature did not verify.");
            }
            return await PublishAsync(run, targetKind, sign.Credential!, sign.SignedPdf!, runHash,
                AttestationKind.Signed, sign, embedded: true, cancellationToken).ConfigureAwait(false);
        }

        var payload = Encoding.UTF8.GetBytes($"{HashBytes(candidate.Pdf)}:{runHash}");
        var mockSign = await broker.TrySignPayloadAsync(payload, captured, pin, cancellationToken).ConfigureAwait(false);
        if (mockSign.PinRequired || mockSign.PinRetriesRemaining is not null)
        {
            return Failure(captured, mockSign.Error ?? "Enter badge PIN to sign.", pinRequired: mockSign.PinRetriesRemaining != 0);
        }
        if (!mockSign.Succeeded)
        {
            return mockSign.PresenceFallbackAllowed
                ? await AttestPresenceAsync(run, targetKind, captured, allowPresence, cancellationToken).ConfigureAwait(false)
                : Failure(captured, mockSign.Error ?? "Signing failed.");
        }
        if (!string.Equals(mockSign.Algorithm, AttestationAlgorithm.MockHmac, StringComparison.Ordinal)
            || mockSign.Signature is not { Length: > 0 }
            || !MockOperatorCredentialBroker.VerifyMockSignature(payload, mockSign.Signature))
        {
            return Failure(captured, "The mock report signature did not verify.");
        }
        return await PublishAsync(run, targetKind, mockSign.Credential ?? captured, candidate.Pdf, runHash,
            AttestationKind.Signed, mockSign, embedded: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(byte[]? Pdf, string? Error)> CompileCandidateAsync(
        TestRunRecord run, string kind, OperatorCredential captured, string overlayKind,
        CancellationToken cancellationToken)
    {
        var candidate = await TryCompileOverlayAsync(run, kind, captured, overlayKind, cancellationToken)
            .ConfigureAwait(false);
        if (candidate.Error is not null || candidate.Pdf is not null)
        {
            return candidate;
        }
        var path = ResolveWorkingPdfPath(run, kind);
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            ? (null, "Certification PDF is missing. Generate reports first.")
            : (await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), null);
    }

    private async Task<ReportAttestationResult> AttestPresenceAsync(
        TestRunRecord run, string kind, OperatorCredential captured, bool allowPresence,
        CancellationToken cancellationToken)
    {
        if (!allowPresence)
        {
            return Failure(captured, "This badge cannot sign, and presence-only attestation is disabled.");
        }
        var candidate = await CompileCandidateAsync(run, kind, captured, AttestationKind.Presence, cancellationToken)
            .ConfigureAwait(false);
        if (candidate.Error is not null || candidate.Pdf is not { Length: > 0 })
        {
            return Failure(captured, candidate.Error ?? "Certification PDF is missing. Generate reports first.");
        }
        var runHash = HashBytes(JsonSerializer.SerializeToUtf8Bytes(run, AppJsonContext.Default.TestRunRecord));
        return await PublishAsync(run, kind, captured, candidate.Pdf, runHash,
            AttestationKind.Presence, null, embedded: false, cancellationToken).ConfigureAwait(false);
    }

    private static bool PhysicalResultMatches(CredentialSignResult sign, OperatorCredential captured, out string? error)
    {
        error = "The signing badge does not match the badge that was captured.";
        if (sign.SignedPdf is not { Length: > 0 } || sign.Signature is not { Length: > 0 }
            || sign.CertificateDer is not { Length: > 0 } || sign.Credential is null)
        {
            error = "The physical credential did not return a complete signed PDF and certificate identity.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(captured.Serial) || string.IsNullOrWhiteSpace(captured.Thumbprint)
            || !string.Equals(captured.Serial, sign.Credential.Serial, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(captured.Thumbprint, sign.Thumbprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(captured.Thumbprint, sign.Credential.Thumbprint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(captured.Thumbprint, Convert.ToHexString(SHA1.HashData(sign.CertificateDer)),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return ITextPadesSignature.TryVerify(sign.SignedPdf, captured.Thumbprint, out error);
    }

    private async Task<ReportAttestationResult> PublishAsync(
        TestRunRecord run, string kind, OperatorCredential party, byte[] pdf, string runHash,
        string attestationKind, CredentialSignResult? sign, bool embedded, CancellationToken cancellationToken)
    {
        var dir = _runStore.GetRunDirectory(run.RunId);
        RequireReportWritable(run, dir);
        cancellationToken.ThrowIfCancellationRequested();
        var document = new ReportAttestation
        {
            Kind = attestationKind,
            ReportKind = kind,
            DisplayName = party.DisplayName,
            Serial = party.Serial,
            Transport = party.Transport,
            Thumbprint = sign?.Thumbprint ?? party.Thumbprint,
            PdfSha256 = HashBytes(pdf),
            RunJsonSha256 = runHash,
            Algorithm = sign?.Algorithm ?? AttestationAlgorithm.Presence,
            SignatureFormat = sign is null ? null
                : embedded ? AttestationSignatureFormat.PadesBasic : AttestationSignatureFormat.DetachedSidecar,
            EmbeddedInPdf = embedded,
            CapturedAt = party.CapturedAt == default ? _clock.UtcNow : party.CapturedAt,
        };
        var sidecar = new ReportAttestationSidecar
        {
            Attestation = document,
            SignatureBase64 = sign?.Signature is { Length: > 0 } signature ? Convert.ToBase64String(signature) : null,
            CertificateBase64 = sign?.CertificateDer is { Length: > 0 } certificate ? Convert.ToBase64String(certificate) : null,
        };
        try
        {
            await _revisions.CommitAsync(run, kind, pdf, sidecar, cancellationToken, operationLockHeld: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Failure(party, "Could not publish the issued report. " + ex.Message); }
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = party,
            Message = $"{(embedded || sign is not null ? "Signed" : "Recorded presence")} for {party.DisplayName} ({party.Transport}).",
            Attestation = run.Attestations.Last(a => string.Equals(a.ReportKind, kind, StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static ReportAttestationResult Failure(OperatorCredential? credential, string message, bool pinRequired = false)
        => new() { Succeeded = false, Credential = credential, Message = message, PinRequired = pinRequired };

    private async Task<(byte[]? Pdf, string? Error)> TryCompileOverlayAsync(
        TestRunRecord run,
        string targetKind,
        OperatorCredential captured,
        string overlayKind,
        CancellationToken cancellationToken)
    {
        if (_reports is null)
        {
            return (null, null);
        }

        var overlay = new ReportAttestation
        {
            Kind = overlayKind,
            ReportKind = targetKind,
            DisplayName = captured.DisplayName,
            Serial = captured.Serial,
            Transport = captured.Transport,
            CapturedAt = captured.CapturedAt == default ? _clock.UtcNow : captured.CapturedAt,
        };

        try
        {
            var bytes = await _reports.Value
                .CompileReportAsync(run, targetKind, cancellationToken, overlay)
                .ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return (bytes, "Certification PDF compile produced no bytes.");
            }

            return (bytes, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, "Could not stamp the certification PDF. " + ex.Message);
        }
    }

    /// Working regeneration preserves immutable issued reports and their evidence.
    public static void InvalidateForKinds(TestRunRecord run, string runDirectory, IEnumerable<string> kinds)
        => RequireReportWritable(run, runDirectory);

    public static ReportAttestation? Find(TestRunRecord run, string reportKind)
    {
        var artifact = ReportRevisions.Latest(run, reportKind);
        return artifact is null ? null : FindForArtifact(run, artifact);
    }

    public static ReportAttestation? FindForArtifact(TestRunRecord run, RunReportArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.PdfPath) || !File.Exists(artifact.PdfPath)) return null;
        try
        {
            var hash = HashFile(artifact.PdfPath);
            var sidecarPath = Path.ChangeExtension(artifact.PdfPath, ".attestation.json");
            return run.Attestations.LastOrDefault(a =>
                string.Equals(a.ReportKind, artifact.Kind, StringComparison.OrdinalIgnoreCase)
                && a.RevisionId == artifact.RevisionId
                && PathEquals(a.SidecarPath, sidecarPath)
                && string.Equals(a.PdfSha256, hash, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// Validates the run and its persisted header before touching report files or stamps.
    public static void RequireReportWritable(TestRunRecord run, string runDirectory)
    {
        if (run.IsSchemaReadOnly)
        {
            var storedVersion = Math.Max(run.StoredSchemaVersion, run.SchemaVersion);
            if (storedVersion > SchemaVersions.TestRunRecord)
            {
                throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
                    SchemaDocumentTypes.TestRunRecord, storedVersion, SchemaVersions.TestRunRecord, run.AppVersion));
            }
            throw new InvalidOperationException("This run is read-only; reports cannot be changed.");
        }
        DocumentSchemaGate.RequireWritable(SchemaDocumentTypes.TestRunRecord, run.SchemaVersion,
            SchemaVersions.TestRunRecord, Path.Combine(runDirectory, "run.json"), run.AppVersion);
        CurrentDocumentFile.ValidateWriteDestinationAsync(Path.Combine(runDirectory, "run.json"),
                AppJsonContext.Default.TestRunRecord, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord)
            .GetAwaiter().GetResult();
    }

    public static string? ResolveDefaultWorkingPdfPath(TestRunRecord run, string defaultKind)
        => ResolveWorkingPdfPath(run, defaultKind)
           ?? ResolveWorkingPdfPath(run, ReportKinds.Status)
           ?? run.Reports.FirstOrDefault(r => ReportArtifactRoles.IsWorking(r.Role)
               && !string.IsNullOrWhiteSpace(r.PdfPath))?.PdfPath;

    public static string? ResolvePdfPath(TestRunRecord run, string reportKind)
    {
        var issued = ReportRevisions.Latest(run, reportKind);
        return issued is null ? ResolveWorkingPdfPath(run, reportKind) : issued.PdfPath;
    }

    public static string? ResolveIssuedPdfPath(TestRunRecord run, string reportKind)
        => ReportRevisions.Latest(run, reportKind)?.PdfPath;

    public static string? ResolveWorkingPdfPath(TestRunRecord run, string reportKind)
    {
        var match = run.Reports.FirstOrDefault(r =>
            string.Equals(r.Kind, reportKind, StringComparison.OrdinalIgnoreCase)
            && ReportArtifactRoles.IsWorking(r.Role)
            && !string.IsNullOrWhiteSpace(r.PdfPath));
        if (match is not null && !string.IsNullOrWhiteSpace(match.PdfPath))
        {
            return match.PdfPath;
        }

        return null;
    }

    /// Run folder name for `runs/{id}/{kind}.pdf` or `runs/{id}/issued/{kind}.pdf`.
    public static string? GuessRunIdFromPdfPath(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(pdfPath));
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var current = new DirectoryInfo(dir);
        for (var depth = 0; current is not null && depth < 4; depth++, current = current.Parent)
            if (string.Equals(current.Name, ReportArtifactRoles.DirectoryName, StringComparison.OrdinalIgnoreCase))
                return current.Parent?.Name;
        return Path.GetFileName(dir);
    }

    /// Report paths follow Windows casing rules only on Windows; distinct Unix files stay distinct.
    public static bool PathEquals(string? left, string? right)
        => string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool RunOwnsPdf(TestRunRecord run, string pdfPath)
    {
        return run.Reports.Any(r => PathEquals(r.PdfPath, pdfPath));
    }

    public static string KindForPdf(TestRunRecord run, string pdfPath)
    {
        var match = run.Reports.FirstOrDefault(r =>
            PathEquals(r.PdfPath, pdfPath));
        if (match is not null && !string.IsNullOrWhiteSpace(match.Kind))
        {
            return match.Kind;
        }

        throw new InvalidOperationException("The PDF is not a report artifact owned by this run.");
    }

    /// Issued PDF when present for this path's kind; otherwise the given path.
    public static string ResolvePrintOrExportPdfPath(TestRunRecord run, string pdfPath)
    {
        if (run.Reports.Any(r => ReportArtifactRoles.IsIssued(r.Role)
            && PathEquals(r.PdfPath, pdfPath))) return pdfPath;
        var kind = KindForPdf(run, pdfPath);
        var issued = ReportRevisions.Latest(run, kind);
        return issued is null ? pdfPath : issued.PdfPath ?? throw new IOException("The issued revision has no PDF path.");
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool EmbeddedSignatureMatches(ReportAttestation attestation, string pdfPath, string pdfHash)
    {
        if (!string.Equals(pdfHash, attestation.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var pdf = File.ReadAllBytes(pdfPath);
            return string.Equals(attestation.SignatureFormat, AttestationSignatureFormat.PadesBasic, StringComparison.Ordinal)
                && ITextPadesSignature.TryVerify(pdf, attestation.Thumbprint, out _);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool SignatureMatches(ReportAttestation attestation, string pdfHash)
    {
        if (string.IsNullOrWhiteSpace(attestation.SidecarPath) || !File.Exists(attestation.SidecarPath))
        {
            return false;
        }

        using var stream = File.OpenRead(attestation.SidecarPath);
        var sidecar = JsonSerializer.Deserialize(stream, AppJsonContext.Default.ReportAttestationSidecar);
        if (sidecar?.SignatureBase64 is null
            || !string.Equals(attestation.SignatureFormat, AttestationSignatureFormat.DetachedSidecar, StringComparison.Ordinal)
            || !string.Equals(pdfHash, attestation.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(sidecar.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes($"{attestation.PdfSha256}:{attestation.RunJsonSha256}");
        var algorithm = attestation.Algorithm;
        if (string.Equals(algorithm, AttestationAlgorithm.MockHmac, StringComparison.Ordinal))
        {
            return MockOperatorCredentialBroker.VerifyMockSignature(payload, signature);
        }

        return false;
    }

    private static bool SidecarMatches(ReportAttestation attestation, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(attestation.SidecarPath) || string.IsNullOrWhiteSpace(expectedHash)) return false;
        try
        {
            if (!string.Equals(HashFile(attestation.SidecarPath), expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
            using var stream = File.OpenRead(attestation.SidecarPath);
            var sidecar = JsonSerializer.Deserialize(stream, AppJsonContext.Default.ReportAttestationSidecar);
            return sidecar is not null && JsonSerializer.Serialize(sidecar.Attestation, AppJsonContext.Default.ReportAttestation)
                == JsonSerializer.Serialize(attestation, AppJsonContext.Default.ReportAttestation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

}

/// Disk sidecar next to the PDF (signature bytes stay off the run JSON).
public sealed class ReportAttestationSidecar
{
    public ReportAttestation Attestation { get; set; } = new();
    public string? SignatureBase64 { get; set; }
    public string? CertificateBase64 { get; set; }
}
