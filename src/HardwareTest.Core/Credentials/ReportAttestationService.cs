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
        _revisions = revisions ?? new FileReportRevisionStore(runStore);
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
        var attestation = artifact is null ? (revisionId is null ? Find(run, lookupKind) : null)
            : run.Attestations.LastOrDefault(a => string.Equals(a.ReportKind, lookupKind, StringComparison.OrdinalIgnoreCase)
                && a.RevisionId == artifact.RevisionId);
        if (attestation is null)
        {
            return false;
        }

        var pdfPath = artifact is not null ? artifact.PdfPath : (revisionId is null ? ResolvePdfPath(run, lookupKind) : null);
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return false;
        }

        if (artifact is not null && !(artifact.RevisionId?.StartsWith("legacy-", StringComparison.Ordinal) ?? false)
            && (string.IsNullOrWhiteSpace(artifact.RunSnapshotPath) || !SidecarMatches(attestation, artifact.SidecarSha256))) return false;

        if (artifact?.RunSnapshotPath is { Length: > 0 } snapshotPath
            && (!File.Exists(snapshotPath) || !string.Equals(HashFile(snapshotPath), attestation.RunJsonSha256, StringComparison.OrdinalIgnoreCase)))
            return false;

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
        var targetKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var captured = credential;
        if (captured is null)
        {
            var capture = await _broker.WaitForPresenceAsync(PresenceTimeout, cancellationToken).ConfigureAwait(false);
            if (!capture.Succeeded || capture.Credential is null)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Message = capture.Error ?? "Present a badge to certify this report.",
                };
            }

            captured = capture.Credential;
        }

        var runJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var runHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runJson)));
        CredentialSignResult? signingProbe = null;
        if (!skipSigning && string.IsNullOrEmpty(pin))
        {
            signingProbe = await _broker.TrySignPayloadAsync(
                    Encoding.UTF8.GetBytes($"pin-probe:{runHash}"),
                    captured,
                    pin,
                    cancellationToken)
                .ConfigureAwait(false);
            if (signingProbe.PinRequired)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    PinRequired = true,
                    Credential = captured,
                    Message = signingProbe.Error ?? "Enter badge PIN to sign.",
                };
            }
        }

        var overlayKind = skipSigning || !_broker.CanSign || signingProbe?.PresenceFallbackAllowed == true
            ? AttestationKind.Presence
            : AttestationKind.Signed;
        var stamped = await TryCompileOverlayAsync(run, targetKind, captured, overlayKind, cancellationToken)
            .ConfigureAwait(false);
        if (stamped.Error is not null)
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Message = stamped.Error,
                Credential = captured,
            };
        }

        var stampedPdf = stamped.Pdf;
        var pdfPath = ResolveWorkingPdfPath(run, targetKind) ?? ResolvePdfPath(run, targetKind);
        if (stampedPdf is null
            && (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)))
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Message = "Certification PDF is missing. Generate reports first.",
                Credential = captured,
            };
        }

        if (_broker.ProducesCms && !skipSigning)
        {
            return await AttestPadesAsync(run, targetKind, captured, pdfPath, stampedPdf, pin, cancellationToken)
                .ConfigureAwait(false);
        }

        var pdfHash = stampedPdf is not null ? HashBytes(stampedPdf) : HashFile(pdfPath!);
        var payload = Encoding.UTF8.GetBytes($"{pdfHash}:{runHash}");
        CredentialSignResult? sign = null;
        if (!skipSigning)
        {
            sign = await _broker.TrySignPayloadAsync(payload, captured, pin, cancellationToken)
                .ConfigureAwait(false);
            if (sign.PinRequired)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    PinRequired = true,
                    Credential = captured,
                    Message = sign.Error ?? "Enter badge PIN to sign.",
                };
            }

            if (!sign.Succeeded && sign.PinRetriesRemaining is not null)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    PinRequired = true,
                    Credential = captured,
                    Message = sign.Error ?? "Incorrect PIN.",
                };
            }
        }

        var signature = sign is { Succeeded: true } ? sign.Signature : null;
        var kind = signature is { Length: > 0 } ? AttestationKind.Signed : AttestationKind.Presence;
        if (kind == AttestationKind.Presence)
        {
            if (!CanRecordPresence(skipSigning, pin, sign))
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Credential = captured,
                    Message = sign?.Error ?? "Signing failed.",
                };
            }

            if (!_settings.AllowPresenceInLieuOfSigning)
            {
                var detail = string.IsNullOrWhiteSpace(sign?.Error)
                    ? string.Empty
                    : " " + sign.Error;
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Credential = captured,
                    Message = "This badge cannot sign, and presence-only attestation is disabled." + detail,
                };
            }

            if (_reports is not null && overlayKind != AttestationKind.Presence)
            {
                var presence = await TryCompileOverlayAsync(
                        run,
                        targetKind,
                        captured,
                        AttestationKind.Presence,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (presence.Error is not null)
                {
                    return new ReportAttestationResult
                    {
                        Succeeded = false,
                        Message = presence.Error,
                        Credential = captured,
                    };
                }

                if (presence.Pdf is { Length: > 0 })
                {
                    stampedPdf = presence.Pdf;
                    pdfHash = HashBytes(stampedPdf);
                }
            }
        }

        var party = sign?.Credential ?? captured;
        var issuedBytes = stampedPdf ?? await File.ReadAllBytesAsync(pdfPath!, cancellationToken).ConfigureAwait(false);
        pdfHash = HashBytes(issuedBytes);
        var document = new ReportAttestation
        {
            Kind = kind,
            ReportKind = targetKind,
            DisplayName = party.DisplayName,
            Serial = party.Serial,
            Transport = party.Transport,
            Thumbprint = sign?.Thumbprint ?? party.Thumbprint,
            PdfSha256 = pdfHash,
            RunJsonSha256 = runHash,

            Algorithm = signature is { Length: > 0 }
                ? (sign?.Algorithm ?? _broker.SigningAlgorithm ?? AttestationAlgorithm.MockHmac)
                : AttestationAlgorithm.Presence,
            SignatureFormat = signature is { Length: > 0 }
                ? AttestationSignatureFormat.DetachedSidecar
                : null,
            EmbeddedInPdf = false,
            CapturedAt = party.CapturedAt == default ? _clock.UtcNow : party.CapturedAt,
        };

        var sidecar = new ReportAttestationSidecar
        {
            Attestation = document,
            SignatureBase64 = signature is { Length: > 0 } ? Convert.ToBase64String(signature) : null,
            CertificateBase64 = sign?.CertificateDer is { Length: > 0 }
                ? Convert.ToBase64String(sign.CertificateDer)
                : null,
        };
        await _revisions.CommitAsync(run, targetKind, issuedBytes, sidecar, cancellationToken, operationLockHeld: true).ConfigureAwait(false);

        var verb = kind == AttestationKind.Signed ? "Signed" : "Recorded presence";
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = party,
            Message = $"{verb} for {party.DisplayName} ({party.Transport}).",
            Attestation = Find(run, targetKind),
        };
    }

    private async Task<ReportAttestationResult> AttestPadesAsync(
        TestRunRecord run,
        string targetKind,
        OperatorCredential captured,
        string? pdfPath,
        byte[]? stampedPdf,
        string? pin,
        CancellationToken cancellationToken)
    {
        var pdfBytes = stampedPdf ?? await File.ReadAllBytesAsync(pdfPath!, cancellationToken).ConfigureAwait(false);
        var signingTime = captured.CapturedAt == default ? _clock.UtcNow : captured.CapturedAt;
        PreparedPdfSignature? prepared = null;
        CredentialSignResult sign;
        if (_broker is IEmbeddedPdfSigningBroker embedded)
        {
            sign = await embedded
                .TrySignPdfAsync(pdfBytes, captured, pin, signingTime, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (!PdfPadesSignature.TryPrepare(
                    pdfBytes,
                    captured.DisplayName,
                    signingTime,
                    out prepared,
                    out var prepareError))
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Credential = captured,
                    Message = prepareError ?? "Could not prepare a PDF signature placeholder.",
                };
            }

            sign = await _broker
                .TrySignDocumentAsync(prepared.SignedBytes, captured, pin, signingTime, cancellationToken)
                .ConfigureAwait(false);
        }
        if (sign.PinRequired)
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                PinRequired = true,
                Credential = captured,
                Message = sign.Error ?? "Enter badge PIN to sign.",
            };
        }

        if (!sign.Succeeded && sign.PinRetriesRemaining is not null)
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                PinRequired = true,
                Credential = captured,
                Message = sign.Error ?? "Incorrect PIN.",
            };
        }

        if (sign is not { Succeeded: true, Signature: { Length: > 0 }, CertificateDer: { Length: > 0 } })
        {
            return await PersistPresenceOrFailAsync(
                    run,
                    targetKind,
                    captured,
                    pdfPath,
                    stampedPdf,
                    pin,
                    sign,
                    skipSigning: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        byte[] signedPdf;
        string? embedError = null;
        if (sign.SignedPdf is { Length: > 0 } embeddedPdf)
        {
            signedPdf = embeddedPdf;
        }
        else if (prepared is not null
                 && prepared.TryEmbed(sign.Signature, out signedPdf, out embedError))
        {
            // Compatibility path for non-iText CMS brokers used by existing deployments/tests.
        }
        else
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Credential = sign.Credential ?? captured,
                Message = embedError ?? "Could not inject the CMS signature into the PDF.",
            };
        }

        var verifies = sign.SignedPdf is { Length: > 0 }
            ? ITextPadesSignature.TryVerify(signedPdf, out var verifyError)
            : PdfPadesSignature.TryVerify(signedPdf, out verifyError);
        if (!verifies)
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Credential = sign.Credential ?? captured,
                Message = verifyError ?? "Embedded PDF signature did not verify.",
            };
        }

        var runJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var runHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runJson)));
        var pdfHash = HashBytes(signedPdf);
        var party = sign.Credential ?? captured;
        var document = new ReportAttestation
        {
            Kind = AttestationKind.Signed,
            ReportKind = targetKind,
            DisplayName = party.DisplayName,
            Serial = party.Serial,
            Transport = party.Transport,
            Thumbprint = sign.Thumbprint ?? party.Thumbprint,
            PdfSha256 = pdfHash,
            RunJsonSha256 = runHash,

            Algorithm = sign.Algorithm ?? _broker.SigningAlgorithm ?? AttestationAlgorithm.PivRsaPkcs1Sha256,
            SignatureFormat = AttestationSignatureFormat.PadesBasic,
            EmbeddedInPdf = true,
            CapturedAt = party.CapturedAt == default ? signingTime : party.CapturedAt,
        };
        var sidecar = new ReportAttestationSidecar
        {
            Attestation = document,
            SignatureBase64 = Convert.ToBase64String(sign.Signature),
            CertificateBase64 = Convert.ToBase64String(sign.CertificateDer),
        };
        await _revisions.CommitAsync(run, targetKind, signedPdf, sidecar, cancellationToken, operationLockHeld: true).ConfigureAwait(false);
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = party,
            Message = $"Signed for {party.DisplayName} ({party.Transport}).",
            Attestation = Find(run, targetKind),
        };
    }

    private async Task<ReportAttestationResult> PersistPresenceOrFailAsync(
        TestRunRecord run,
        string targetKind,
        OperatorCredential captured,
        string? pdfPath,
        byte[]? stampedPdf,
        string? pin,
        CredentialSignResult? sign,
        bool skipSigning,
        CancellationToken cancellationToken)
    {
        if (!CanRecordPresence(skipSigning, pin, sign))
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Credential = captured,
                Message = sign?.Error ?? "Signing failed.",
            };
        }

        if (!_settings.AllowPresenceInLieuOfSigning)
        {
            var detail = string.IsNullOrWhiteSpace(sign?.Error)
                ? string.Empty
                : " " + sign.Error;
            return new ReportAttestationResult
            {
                Succeeded = false,
                Credential = captured,
                Message = "This badge cannot sign, and presence-only attestation is disabled." + detail,
            };
        }

        if (_reports is not null && !skipSigning && !string.IsNullOrEmpty(pin))
        {
            var presence = await TryCompileOverlayAsync(
                    run,
                    targetKind,
                    captured,
                    AttestationKind.Presence,
                    cancellationToken)
                .ConfigureAwait(false);
            if (presence.Error is not null)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Message = presence.Error,
                    Credential = captured,
                };
            }

            if (presence.Pdf is { Length: > 0 })
            {
                stampedPdf = presence.Pdf;
            }
        }

        var issuedBytes = stampedPdf ?? await File.ReadAllBytesAsync(pdfPath!, cancellationToken).ConfigureAwait(false);
        var pdfHash = HashBytes(issuedBytes);
        var runJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var runHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runJson)));
        var document = new ReportAttestation
        {
            Kind = AttestationKind.Presence,
            ReportKind = targetKind,
            DisplayName = captured.DisplayName,
            Serial = captured.Serial,
            Transport = captured.Transport,
            Thumbprint = captured.Thumbprint,
            PdfSha256 = pdfHash,
            RunJsonSha256 = runHash,

            Algorithm = AttestationAlgorithm.Presence,
            CapturedAt = captured.CapturedAt == default ? _clock.UtcNow : captured.CapturedAt,
        };
        var sidecar = new ReportAttestationSidecar
        {
            Attestation = document,
        };
        await _revisions.CommitAsync(run, targetKind, issuedBytes, sidecar, cancellationToken, operationLockHeld: true).ConfigureAwait(false);
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = captured,
            Message = $"Recorded presence for {captured.DisplayName} ({captured.Transport}).",
            Attestation = Find(run, targetKind),
        };
    }

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

    /// Compatibility hook: working regeneration preserves immutable issued history.
    public static void InvalidateForKinds(TestRunRecord run, string runDirectory, IEnumerable<string> kinds)
    {
        // Issued revisions and their attestations are immutable. Working generation needs no invalidation.
    }

    public static ReportAttestation? Find(TestRunRecord run, string reportKind)
    {
        var latest = ReportRevisions.Latest(run, reportKind);
        return run.Attestations.LastOrDefault(a =>
            string.Equals(a.ReportKind, reportKind, StringComparison.OrdinalIgnoreCase)
            && (latest is null || a.RevisionId == latest.RevisionId));
    }

    public static string? ResolvePdfPath(TestRunRecord run, string reportKind)
    {
        var issued = ReportRevisions.Latest(run, reportKind);
        return issued is not null ? issued.PdfPath : ResolveWorkingPdfPath(run, reportKind);
    }

    public static string? ResolveIssuedPdfPath(TestRunRecord run, string reportKind)
        => ReportRevisions.Latest(run, reportKind)?.PdfPath;

    public static string? ResolveWorkingPdfPath(TestRunRecord run, string reportKind)
    {
        var match = run.Reports.FirstOrDefault(r =>
            string.Equals(r.Kind, reportKind, StringComparison.OrdinalIgnoreCase)
            && ReportArtifactRoles.IsWorking(r.Role));
        if (match is not null && !string.IsNullOrWhiteSpace(match.PdfPath))
        {
            return match.PdfPath;
        }

        return string.Equals(reportKind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase)
            ? null
            : run.ReportPdfPath;
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
        {
            if (string.Equals(current.Name, ReportArtifactRoles.DirectoryName, StringComparison.OrdinalIgnoreCase))
                return current.Parent?.Name;
        }
        return Path.GetFileName(dir);
    }

    public static bool RunOwnsPdf(TestRunRecord run, string pdfPath)
    {
        if (string.Equals(run.ReportPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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

        var name = Path.GetFileNameWithoutExtension(pdfPath);
        return string.IsNullOrWhiteSpace(name) ? ReportKinds.Certification : name;
    }

    /// Issued PDF when present for this path's kind; otherwise the given path.
    public static string ResolvePrintOrExportPdfPath(TestRunRecord run, string pdfPath)
    {
        if (run.Reports.Any(r => ReportArtifactRoles.IsIssued(r.Role)
            && string.Equals(r.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))) return pdfPath;
        var kind = KindForPdf(run, pdfPath);
        var issued = ReportRevisions.Latest(run, kind);
        return issued is null ? pdfPath : issued.PdfPath ?? throw new IOException("The issued revision has no PDF path.");
    }

    private static bool CanRecordPresence(bool skipSigning, string? pin, CredentialSignResult? sign)
    {
        if (skipSigning)
        {
            return true;
        }

        if (sign?.PresenceFallbackAllowed == true)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(pin))
        {
            return false;
        }

        var error = sign?.Error;
        if (string.IsNullOrWhiteSpace(error))
        {
            return true;
        }

        return error.Contains("cannot sign", StringComparison.OrdinalIgnoreCase)
            || error.Contains("No PIV signing certificate", StringComparison.OrdinalIgnoreCase)
            || error.Contains("PIV applet not found", StringComparison.OrdinalIgnoreCase);
    }

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
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
            return ITextPadesSignature.TryVerify(pdf, out _)
                   || PdfPadesSignature.TryVerify(pdf, out _);
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
        var algorithm = attestation.Algorithm ?? sidecar.Attestation.Algorithm;
        if (string.Equals(algorithm, AttestationAlgorithm.MockHmac, StringComparison.Ordinal))
        {
            return MockOperatorCredentialBroker.VerifyMockSignature(payload, signature);
        }

        if (string.IsNullOrWhiteSpace(sidecar.CertificateBase64))
        {
            return false;
        }

        byte[] cert;
        try
        {
            cert = Convert.FromBase64String(sidecar.CertificateBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return PivSigner.Verify(payload, signature, cert, algorithm ?? string.Empty);
    }


}

/// Disk sidecar next to the PDF (signature bytes stay off the run JSON).
public sealed class ReportAttestationSidecar
{
    public ReportAttestation Attestation { get; set; } = new();
    public string? SignatureBase64 { get; set; }
    public string? CertificateBase64 { get; set; }
}
