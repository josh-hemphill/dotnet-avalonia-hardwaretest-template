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

    public ReportAttestationService(
        IOperatorCredentialBroker broker,
        IRunStore runStore,
        AppSettings settings,
        IClock? clock = null,
        Lazy<IReportService>? reports = null)
    {
        _broker = broker;
        _runStore = runStore;
        _settings = settings;
        _clock = clock ?? SystemClock.Instance;
        _reports = reports;
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
    {
        var lookupKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var attestation = Find(run, lookupKind);
        if (attestation is null)
        {
            return false;
        }

        var pdfPath = ResolvePdfPath(run, lookupKind);
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return false;
        }

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
        var issueId = Guid.NewGuid().ToString("N");
        var issuedDir = Path.Combine(dir, ReportArtifactRoles.DirectoryName);
        var issuedPath = Path.Combine(issuedDir, $"{kind}-{issueId}.pdf");
        var sidecarPath = Path.Combine(issuedDir, $"{kind}-{issueId}.attestation.json");
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
            SidecarPath = sidecarPath,
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
        // Unique issued paths preserve every previously issued byte. The run's atomic save
        // is the publication point; neither its in-memory metadata nor its old issue changes first.
        var candidate = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(run, AppJsonContext.Default.TestRunRecord),
            AppJsonContext.Default.TestRunRecord)!;
        candidate.Attestations.Add(document);
        candidate.Reports.Add(new RunReportArtifact
        {
            Kind = kind,
            Title = ReportKinds.Title(kind),
            PdfPath = issuedPath,
            GeneratedAt = _clock.UtcNow,
            Role = ReportArtifactRoles.Issued,
        });
        if (string.IsNullOrWhiteSpace(candidate.OperatorName))
        {
            candidate.OperatorName = party.DisplayName;
        }
        var pdfWritten = false;
        var sidecarWritten = false;
        try
        {
            await AtomicFile.WriteAllBytesAsync(issuedPath, pdf, cancellationToken).ConfigureAwait(false);
            pdfWritten = true;
            await AtomicFile.WriteJsonAsync(sidecarPath, sidecar, AppJsonContext.Default.ReportAttestationSidecar, cancellationToken)
                .ConfigureAwait(false);
            sidecarWritten = true;
            RequireReportWritable(run, dir);
            await _runStore.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (sidecarWritten) TryDeleteSidecar(sidecarPath);
            if (pdfWritten) TryDeleteSidecar(issuedPath);
            if (ex is OperationCanceledException) throw;
            return Failure(party, "Could not publish the issued report. " + ex.Message);
        }
        run.Reports = candidate.Reports;
        run.Attestations = candidate.Attestations;
        run.OperatorName = candidate.OperatorName;
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = party,
            Message = $"{(embedded || sign is not null ? "Signed" : "Recorded presence")} for {party.DisplayName} ({party.Transport}).",
            Attestation = document,
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

    /// Drops stamps and sidecars for kinds whose attested PDF is about to change.
    public static void InvalidateForKinds(TestRunRecord run, string runDirectory, IEnumerable<string> kinds)
    {
        RequireReportWritable(run, runDirectory);
        foreach (var kind in kinds)
        {
            var existing = run.Attestations
                .Where(a => string.Equals(a.ReportKind, kind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var attestation in existing)
            {
                TryDeleteSidecar(attestation.SidecarPath);
            }

            run.Attestations.RemoveAll(a =>
                string.Equals(a.ReportKind, kind, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(runDirectory))
            {
                TryDeleteSidecar(Path.Combine(runDirectory, $"{kind}.attestation.json"));
            }
        }
    }

    public static ReportAttestation? Find(TestRunRecord run, string reportKind)
    {
        var issuedPath = ResolveIssuedPdfPath(run, reportKind);
        if (string.IsNullOrWhiteSpace(issuedPath) || !File.Exists(issuedPath))
        {
            return null;
        }
        try
        {
            var issuedHash = HashFile(issuedPath);
            var issuedSidecarPath = Path.ChangeExtension(issuedPath, ".attestation.json");
            // The unique issuance stem binds evidence even when different issues have
            // identical PDF bytes. Artifact time, not append order, selects the issue.
            return run.Attestations.LastOrDefault(a =>
                string.Equals(a.ReportKind, reportKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.SidecarPath, issuedSidecarPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.PdfSha256, issuedHash, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
        => ResolveIssuedPdfPath(run, reportKind) ?? ResolveWorkingPdfPath(run, reportKind);

    public static string? ResolveIssuedPdfPath(TestRunRecord run, string reportKind)
    {
        var match = run.Reports.Select((artifact, index) => (artifact, index))
            .Where(item => string.Equals(item.artifact.Kind, reportKind, StringComparison.OrdinalIgnoreCase)
                && ReportArtifactRoles.IsIssued(item.artifact.Role)
                && !string.IsNullOrWhiteSpace(item.artifact.PdfPath))
            .OrderByDescending(item => item.artifact.GeneratedAt)
            .ThenByDescending(item => item.index)
            .Select(item => item.artifact)
            .FirstOrDefault();
        return match is not null && !string.IsNullOrWhiteSpace(match.PdfPath)
            ? match.PdfPath
            : null;
    }

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

        var name = Path.GetFileName(dir);
        if (string.Equals(name, ReportArtifactRoles.DirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(Path.GetDirectoryName(dir));
        }

        return name;
    }

    public static bool RunOwnsPdf(TestRunRecord run, string pdfPath)
    {
        return run.Reports.Any(r => string.Equals(r.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase));
    }

    public static string KindForPdf(TestRunRecord run, string pdfPath)
    {
        var match = run.Reports.FirstOrDefault(r =>
            string.Equals(r.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !string.IsNullOrWhiteSpace(match.Kind))
        {
            return match.Kind;
        }

        throw new InvalidOperationException("The PDF is not a report artifact owned by this run.");
    }

    /// Issued PDF when present for this path's kind; otherwise the given path.
    public static string ResolvePrintOrExportPdfPath(TestRunRecord run, string pdfPath)
    {
        var kind = KindForPdf(run, pdfPath);
        return ResolveIssuedPdfPath(run, kind) ?? pdfPath;
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

    private static void TryDeleteSidecar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reprint must still proceed; a leftover sidecar is dropped from the run record.
        }
    }
}

/// Disk sidecar next to the PDF (signature bytes stay off the run JSON).
public sealed class ReportAttestationSidecar
{
    public ReportAttestation Attestation { get; set; } = new();
    public string? SignatureBase64 { get; set; }
    public string? CertificateBase64 { get; set; }
}
